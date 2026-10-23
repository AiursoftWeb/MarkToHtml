using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Internal opt-in composition for a future queue caller, not production DI/AgentService wiring.
/// One instance owns one trusted conversation; all session writes must go through this facade.
/// Initial legacy history must be completed and is imported lossily once. UI projections are
/// never replayed. Continue and legacy cancellation semantics are deliberately not supported.
/// Production blockers: ModelFailure has no legacy exception details (never expose provider
/// bodies); factory-caught write exceptions are string outputs and can project Executed rather
/// than legacy Failed. Do not infer business success from canonical Succeeded or parse error text.
/// </summary>
internal sealed class AgentSessionFacade
{
    private readonly AgentConversation _conversation;
    private readonly AdviceService _advice;
    private readonly ClaudeAgentModelClient _model;
    private readonly OrderedAgentSessionHost _host;
    private readonly AdviceCheckpointMapper _mapper;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OrderedAgentRunResult? _result;
    private bool _started;
    private bool _faulted;

    internal AgentSessionFacade(AgentConversation conversation, ClaudeClient client,
        IServiceScopeFactory scopes, ToolRegistry registry, AdviceService advice)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(advice);
        if (conversation.PendingAdviceIds.Count != 0 || advice.GetPendingForConversation(conversation.Id).Count != 0)
            throw new ArgumentException("A new session cannot import pending Advice.", nameof(conversation));
        _conversation = conversation;
        _advice = advice;
        var initial = AgentHistoryMapper.ImportCompletedLegacyHistoryLossy(conversation.Messages);
        _model = new ClaudeAgentModelClient(client, registry.AllTools.Select(tool => tool.ProtocolTool.Name),
            initialReasoning: initial.InitialReasoning);
        _host = new OrderedAgentSessionHost(initial.Transcript,
            AgentSessionToolFactory.Create(scopes, conversation, registry), _model);
        _mapper = new AdviceCheckpointMapper(conversation.Id, advice, registry);
    }

    internal async Task<OrderedAgentRunResult> StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_started) throw new InvalidOperationException("This session has already started.");
            _started = true;
            _conversation.State = AgentState.Thinking;
            try
            {
                Synchronize(await _host.StartAsync());
                return Copy(_result!);
            }
            catch
            {
                Fault();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    internal async Task<OrderedAgentRunResult> ResolveAsync(Guid adviceId, ApprovalDecision decision)
    {
        await _gate.WaitAsync();
        try
        {
            if (_faulted) throw new InvalidOperationException("This session cannot resume after a dispatch or projection failure.");
            if (decision is not (ApprovalDecision.Execute or ApprovalDecision.Reject))
                throw new ArgumentOutOfRangeException(nameof(decision));
            var current = _result ?? throw new InvalidOperationException("This session has not started.");
            var callId = _mapper.RequireCurrentPendingCall(adviceId, current);
            // Validate association first, then change status before any handler executes. This
            // gate spans status, dispatch and both projections. The private host never calls back
            // into this gate, so its own serialization cannot invert the lock order.
            _advice.UpdateStatus(adviceId, decision == ApprovalDecision.Execute ? AdviceStatus.Approved : AdviceStatus.Rejected);
            _conversation.PendingAdviceIds.RemoveAll(id => id == adviceId);
            _conversation.State = AgentState.Thinking;
            try
            {
                Synchronize(await _host.ResolveAsync(callId, decision));
                return Copy(_result!);
            }
            catch
            {
                // A write may already have happened; no retry from the old checkpoint.
                Fault();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    internal async Task<OrderedAgentRunResult?> GetSnapshotAsync()
    {
        await _gate.WaitAsync();
        try { return _result == null ? null : Copy(_result); }
        finally { _gate.Release(); }
    }

    private void Synchronize(OrderedAgentRunResult result)
    {
        _result = Copy(result); // Canonical evidence survives independently of the lossy UI.
        // A completion rejected by runner validation may have adapter reasoning but no
        // appended assistant. Pending reasoning is not canonical evidence and cannot project.
        var reasoning = _model.GetReasoningSnapshot()
            .Where(pair => pair.Key >= 0 && pair.Key < result.Transcript.Count &&
                result.Transcript[pair.Key].Role == TranscriptRole.Assistant)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var messages = AgentHistoryMapper.ProjectForUi(result, reasoning);
        _mapper.Synchronize(_conversation, result);
        if (result.Outcome == OrderedAgentRunOutcome.MaxIterations)
            messages.Add(new ToolMessagesItem
            {
                Role = "assistant",
                Content = "I've reached the maximum number of steps. Please refine your request or approve pending actions."
            });
        _conversation.Messages = messages;
        _conversation.LoopCount = result.Iterations;
        _conversation.LastActivity = DateTime.UtcNow;
        _conversation.ErrorMessage = result.Outcome == OrderedAgentRunOutcome.ModelFailure ? "Model execution failed." : null;
        _conversation.State = result.Outcome switch
        {
            OrderedAgentRunOutcome.AwaitingApproval => AgentState.AwaitingApproval,
            OrderedAgentRunOutcome.Completed or OrderedAgentRunOutcome.MaxIterations => AgentState.Completed,
            _ => AgentState.Error // Unsupported terminal outcomes are not silently presented as success.
        };
    }

    private void Fault()
    {
        _faulted = true;
        _conversation.State = AgentState.Error;
        _conversation.ErrorMessage = "Session execution failed.";
        _conversation.LastActivity = DateTime.UtcNow;
    }

    private static OrderedAgentRunResult Copy(OrderedAgentRunResult result) => result with
    {
        Checkpoint = result.Checkpoint?.DeepCopy(),
        Results = result.Results.Select(item => item.DeepCopy()).ToArray(),
        Traces = result.Traces.ToArray(),
        Transcript = result.Transcript.Select(message => message.DeepCopy()).ToArray()
    };
}
