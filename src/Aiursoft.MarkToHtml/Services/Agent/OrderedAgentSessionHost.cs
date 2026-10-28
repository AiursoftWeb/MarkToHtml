using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Single-session OrderedRunner coordinator used by the facade.
/// Callers supply scoped tool handlers. This does not implement MCP execution, persistence,
/// Advice state, legacy error/rejection text, or production terminal-state mapping.
/// </summary>
internal sealed class OrderedAgentSessionHost
{
    private readonly OrderedAgentRunner _runner;
    private readonly TranscriptMessage[] _initialTranscript;
    private readonly IAgentTool[] _tools;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _faulted;
    private OrderedAgentRunResult? _result;

    public OrderedAgentSessionHost(IEnumerable<TranscriptMessage> initialTranscript,
        IEnumerable<IAgentTool> tools, IAgentModelClient model)
    {
        ArgumentNullException.ThrowIfNull(initialTranscript);
        ArgumentNullException.ThrowIfNull(tools);
        _initialTranscript = initialTranscript.Select(message => message.DeepCopy()).ToArray();
        _tools = tools.ToArray();
        _runner = new OrderedAgentRunner(model);
    }

    public async Task<OrderedAgentRunResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_started) throw new InvalidOperationException("This session has already started.");
            _started = true;
            _result = await _runner.RunAsync(new AgentRunRequest(_initialTranscript, _tools,
                new AgentRunOptions(MaxIterations: 10)), cancellationToken: cancellationToken);
            return Copy(_result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OrderedAgentRunResult> ResolveAsync(string callId, ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_faulted) throw new InvalidOperationException("This session cannot resume after a dispatch failure.");
            if (decision is not (ApprovalDecision.Execute or ApprovalDecision.Reject))
                throw new ArgumentOutOfRangeException(nameof(decision), "Resolve requires execute or reject.");
            if (string.IsNullOrWhiteSpace(callId)) throw new ArgumentException("A call ID is required.", nameof(callId));
            var checkpoint = _result?.Checkpoint
                ?? throw new InvalidOperationException("This session is not awaiting approval.");
            var pending = checkpoint.Resolutions.SingleOrDefault(item => item.Call.Id == callId)
                ?? throw new ArgumentException("Tool call is not part of this checkpoint.", nameof(callId));
            if (pending.Result is not null) throw new InvalidOperationException("Tool call was already resolved.");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _result = await _runner.ResolveAsync(checkpoint, _tools, callId, decision, cancellationToken);
            }
            catch
            {
                // A cancelled/failed dispatch may already have written. Never retry an old checkpoint.
                _faulted = true;
                _result = null;
                throw;
            }
            return Copy(_result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OrderedAgentRunResult?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _result is null ? null : Copy(_result);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static OrderedAgentRunResult Copy(OrderedAgentRunResult result) => result with
    {
        Checkpoint = result.Checkpoint?.DeepCopy(),
        Results = result.Results.Select(item => item.DeepCopy()).ToArray(),
        Traces = result.Traces.ToArray(),
        Transcript = result.Transcript.Select(message => message.DeepCopy()).ToArray()
    };
}
