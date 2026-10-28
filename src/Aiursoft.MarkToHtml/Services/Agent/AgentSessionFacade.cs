using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Canonical session execution and UI/Advice projection for production and evaluator callers.
/// Callers authorize the owned conversation before execution or snapshots. One facade owns
/// one trusted conversation. Completed initial history is imported lossily once; mutable UI
/// history and Advice arguments are never replayed. Continuations receive fresh ten-step budgets.
/// Queue reservations bind approval decisions under the same gate as dispatch and projection.
/// Removal-only cancellation is handled by the owning service, not in-flight cancellation.
/// </summary>
public sealed class AgentSessionFacade
{
    private const string MaxStepsMessage = "I've reached the maximum number of steps. Please refine your request or approve pending actions.";
    private readonly AgentToolResultMapper _feedback = new();
    private readonly AgentConversation _conversation;
    private readonly AdviceService _advice;
    private ClaudeAgentModelClient _model;
    private OrderedAgentSessionHost _host;
    private readonly ClaudeClient _client;
    private readonly string[] _toolOrder;
    private readonly IReadOnlyList<IAgentTool> _tools;
    private readonly AdviceCheckpointMapper _mapper;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OrderedAgentRunResult? _result;
    private bool _started;
    private bool _faulted;
    private int _turnStartIndex;
    private readonly Dictionary<(int Batch, string CallId), ToolTrace> _observedTraces = new();
    private List<(int Batch, string CallId)> _runnerTraceKeys = [];
    private bool _traceCorrelationLost;

    public AgentSessionFacade(AgentConversation conversation, ClaudeClient client,
        IServiceScopeFactory scopes, ToolRegistry registry, AdviceService advice)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(advice);
        if (conversation.PendingAdviceIds.Count != 0 || advice.GetPendingForConversation(conversation.Id).Count != 0)
            throw new ArgumentException("A new session cannot import pending Advice.", nameof(conversation));
        _conversation = conversation;
        _advice = advice;
        _client = client;
        _toolOrder = registry.AllTools.Select(tool => tool.ProtocolTool.Name).ToArray();
        _tools = AgentSessionToolFactory.Create(scopes, conversation, registry, _feedback);
        var initial = AgentHistoryMapper.ImportCompletedLegacyHistoryLossy(conversation.Messages);
        _model = new ClaudeAgentModelClient(client, _toolOrder,
            initialReasoning: initial.InitialReasoning)
        { ToolResultText = _feedback.GetText };
        _host = new OrderedAgentSessionHost(initial.Transcript, _tools, _model);
        _turnStartIndex = initial.Transcript.Count;
        _mapper = new AdviceCheckpointMapper(conversation.Id, advice, registry, _feedback);
    }

    public async Task<OrderedAgentRunResult> StartAsync()
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

    private readonly object _decisions = new();
    private readonly Dictionary<Guid, (string CallId, ApprovalDecision Decision)> _reserved = new();

    // The short projection/reservation lock never spans handler/model awaits. Sibling decisions
    // can bind against the immutable current checkpoint while serialized dispatch is in flight.
    // Never trust mutable Advice arguments.
    internal bool ReserveDecision(Guid adviceId, ApprovalDecision decision)
    {
        lock (_decisions)
        {
            try
            {
                if (_faulted || _result == null || _reserved.ContainsKey(adviceId)) return false;
                var callId = _mapper.RequireCurrentPendingCall(adviceId, _result);
                _reserved.Add(adviceId, (callId, decision));
                _advice.UpdateStatus(adviceId, decision == ApprovalDecision.Execute ? AdviceStatus.Approved : AdviceStatus.Rejected);
                _conversation.PendingAdviceIds.RemoveAll(id => id == adviceId);
                _conversation.State = AgentState.Thinking;
                _conversation.LastActivity = DateTime.UtcNow;
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
    }

    public Task<OrderedAgentRunResult> ResolveAsync(Guid adviceId, ApprovalDecision decision) =>
        ResolveCoreAsync(adviceId, decision, false);

    internal Task<OrderedAgentRunResult> DispatchReservedAsync(Guid adviceId, ApprovalDecision decision) =>
        ResolveCoreAsync(adviceId, decision, true);

    private async Task<OrderedAgentRunResult> ResolveCoreAsync(Guid adviceId, ApprovalDecision decision, bool reserved)
    {
        await _gate.WaitAsync();
        try
        {
            if (_faulted) throw new InvalidOperationException("This session cannot resume after a dispatch or projection failure.");
            if (decision is not (ApprovalDecision.Execute or ApprovalDecision.Reject))
                throw new ArgumentOutOfRangeException(nameof(decision));
            var current = _result ?? throw new InvalidOperationException("This session has not started.");
            string callId;
            lock (_decisions)
            {
                if (reserved)
                {
                    if (!_reserved.TryGetValue(adviceId, out var reservation) || reservation.Decision != decision)
                        throw new InvalidOperationException("Advice has no queued reservation.");
                    callId = reservation.CallId;
                    _reserved.Remove(adviceId); // Consume before any side effect, including failures.
                }
                else callId = _mapper.RequireCurrentPendingCall(adviceId, current);
                // Validate association first, then change status before any handler executes. This
                // gate spans status, dispatch and both projections. The private host never calls back
                // into this gate, so its own serialization cannot invert the lock order.
                _advice.UpdateStatus(adviceId, decision == ApprovalDecision.Execute ? AdviceStatus.Approved : AdviceStatus.Rejected);
                _conversation.PendingAdviceIds.RemoveAll(id => id == adviceId);
                _conversation.State = AgentState.Thinking;
            }
            try
            {
                var call = current.Checkpoint!.Resolutions.Single(item => item.Call.Id == callId).Call;
                _feedback.Begin(current.Transcript.Count - 1, callId, call.Name);
                // Resolve coalesces *all* earlier IDs, then orders current resolutions.
                // Preserve the already observed ledger; predict only the runner's retained
                // prefix, never correlate a new trace to an earlier call by bare ID.
                var resolutionIds = current.Checkpoint.Resolutions.Select(item => item.Call.Id).ToHashSet(StringComparer.Ordinal);
                var retained = _runnerTraceKeys.Where(key => !resolutionIds.Contains(key.CallId)).ToList();
                foreach (var resolution in current.Checkpoint.Resolutions)
                    if (_runnerTraceKeys.Any(key => key.CallId == resolution.Call.Id) ||
                        resolution.Call.Id == callId && decision == ApprovalDecision.Execute)
                        retained.Add(resolution.Call.Id == callId && decision == ApprovalDecision.Execute
                            ? (current.Transcript.Count - 1, callId)
                            : _runnerTraceKeys.Last(key => key.CallId == resolution.Call.Id));
                try { Synchronize(await _host.ResolveAsync(callId, decision), retained); }
                finally { _feedback.End(); }
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

    // A trusted caller may supply the latest editor snapshot, but validation must precede
    // every mutation. Never rebuild continuation history from the mutable UI projection.
    public async Task<OrderedAgentRunResult> ContinueAsync(string userId, string userMessage,
        string? documentContent = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_faulted) throw new InvalidOperationException("This session cannot continue after a dispatch or projection failure.");
            var current = _result ?? throw new InvalidOperationException("This session has not started.");
            if (userId != _conversation.UserId || string.IsNullOrWhiteSpace(userMessage) ||
                _reserved.Count != 0 || current.Checkpoint != null || current.Outcome is not
                    (OrderedAgentRunOutcome.Completed or OrderedAgentRunOutcome.MaxIterations or OrderedAgentRunOutcome.ModelFailure))
                throw new InvalidOperationException("This session cannot continue with this request.");

            var content = string.IsNullOrWhiteSpace(documentContent) ? _conversation.DocumentContent : documentContent;
            var transcript = current.Transcript.Select(message => message.DeepCopy()).ToList();
            // Preserve the legacy budget notice on the next turn without importing mutable UI.
            // This is host-authored context, not an additional model call/completion.
            if (current.Outcome == OrderedAgentRunOutcome.MaxIterations)
                transcript.Add(TranscriptMessage.Assistant([new TextBlock(MaxStepsMessage)]));
            transcript.Add(new TranscriptMessage(TranscriptRole.User, [new TextBlock(AgentService.DocumentSystemReminder)], IsMeta: true));
            transcript.Add(new TranscriptMessage(TranscriptRole.User,
                [new TextBlock($"<document-content>\n{AgentService.FormatDocumentWithLineNumbers(content)}\n</document-content>")], IsMeta: true));
            transcript.Add(TranscriptMessage.User(userMessage));
            var reasoning = _model.GetReasoningSnapshot()
                .Where(pair => pair.Key >= 0 && pair.Key < current.Transcript.Count &&
                    current.Transcript[pair.Key].Role == TranscriptRole.Assistant)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            _model = new ClaudeAgentModelClient(_client, _toolOrder, initialReasoning: reasoning)
            { ToolResultText = _feedback.GetText };
            _host = new OrderedAgentSessionHost(transcript, _tools, _model);
            _turnStartIndex = transcript.Count;
            _observedTraces.Clear();
            _runnerTraceKeys.Clear();
            _traceCorrelationLost = false;
            _conversation.DocumentContent = content;
            _conversation.State = AgentState.Thinking;
            _conversation.LastActivity = DateTime.UtcNow;
            _conversation.LoopCount = 0;
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

    public async Task<OrderedAgentRunResult?> GetSnapshotAsync()
    {
        await _gate.WaitAsync();
        try { return _result == null ? null : Copy(_result); }
        finally { _gate.Release(); }
    }

    // Capture an independent runtime snapshot under the same
    // gate as canonical history and document updates. The trusted boundary excludes imported
    // history and earlier turns; evaluation belongs to a separate consumer assembly.
    public async Task<AgentSessionRuntimeSnapshot> GetRuntimeSnapshotAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_faulted) throw new InvalidOperationException("Faulted session snapshot is unavailable.");
            return new AgentSessionRuntimeSnapshot(
                Copy(_result ?? throw new InvalidOperationException("This session has not started.")),
                _turnStartIndex, _conversation.DocumentContent, Array.AsReadOnly(_toolOrder.ToArray()))
            {
                ObservedTraces = new Dictionary<(int Batch, string CallId), ToolTrace>(
                    _traceCorrelationLost ? new Dictionary<(int Batch, string CallId), ToolTrace>() : _observedTraces)
            };
        }
        finally { _gate.Release(); }
    }

    private void Synchronize(OrderedAgentRunResult result, List<(int Batch, string CallId)>? retained = null)
    {
        lock (_decisions) SynchronizeCore(result, retained);
    }

    private void SynchronizeCore(OrderedAgentRunResult result, List<(int Batch, string CallId)>? retained)
    {
        CaptureTraces(result, retained ?? []);
        _result = Copy(result); // Canonical evidence survives independently of the lossy UI.
        // A completion rejected by runner validation may have adapter reasoning but no
        // appended assistant. Pending reasoning is not canonical evidence and cannot project.
        var reasoning = _model.GetReasoningSnapshot()
            .Where(pair => pair.Key >= 0 && pair.Key < result.Transcript.Count &&
                result.Transcript[pair.Key].Role == TranscriptRole.Assistant)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var messages = AgentHistoryMapper.ProjectForUi(result, reasoning, _feedback.GetText);
        _mapper.Synchronize(_conversation, result);
        if (result.Outcome == OrderedAgentRunOutcome.MaxIterations)
            messages.Add(new ToolMessagesItem
            {
                Role = "assistant",
                Content = MaxStepsMessage
            });
        _conversation.Messages = messages;
        _conversation.LoopCount = result.Iterations;
        _conversation.LastActivity = DateTime.UtcNow;
        _conversation.ErrorMessage = result.Outcome == OrderedAgentRunOutcome.ModelFailure ? "Model execution failed." : null;
        _conversation.State = result.Outcome switch
        {
            OrderedAgentRunOutcome.AwaitingApproval => _reserved.Count == 0 ? AgentState.AwaitingApproval : AgentState.Thinking,
            OrderedAgentRunOutcome.Completed or OrderedAgentRunOutcome.MaxIterations => AgentState.Completed,
            _ => AgentState.Error // Unsupported terminal outcomes are not silently presented as success.
        };
    }

    private void CaptureTraces(OrderedAgentRunResult result, List<(int Batch, string CallId)> retained)
    {
        if (_traceCorrelationLost) return;
        var dispatched = new Dictionary<(int Batch, string CallId), ToolResult>();
        for (var batch = _turnStartIndex; batch < result.Transcript.Count; batch++)
        {
            var message = result.Transcript[batch];
            if (message.Role != TranscriptRole.Assistant) continue;
            foreach (var call in message.ToolCalls)
            {
                var output = batch + 1 < result.Transcript.Count
                    ? result.Transcript[batch + 1].ToolResults.SingleOrDefault(item => item.CallId == call.Id)
                    : result.Checkpoint?.Resolutions.SingleOrDefault(item => item.Call.Id == call.Id)?.Result;
                if (output?.Outcome is ToolOutcome.Succeeded or ToolOutcome.Failed)
                    dispatched.Add((batch, call.Id), output);
            }
        }
        var keys = retained.Concat(dispatched.Keys.Where(key => !_observedTraces.ContainsKey(key) && !retained.Contains(key))).ToList();
        var traces = result.Checkpoint?.CompletedTraces ?? result.Traces;
        if (keys.Count != traces.Count)
        {
            _traceCorrelationLost = true;
            return;
        }
        for (var i = 0; i < keys.Count; i++)
        {
            var trace = traces[i];
            var key = keys[i];
            if (!dispatched.TryGetValue(key, out var output) || trace.CallId != key.CallId ||
                trace.ToolName != output.Name || trace.Outcome != output.Outcome ||
                _observedTraces.TryGetValue(key, out var observed) && observed != trace)
            {
                _traceCorrelationLost = true;
                return;
            }
        }
        for (var i = 0; i < keys.Count; i++) _observedTraces[keys[i]] = traces[i];
        _runnerTraceKeys = keys;
    }

    private void Fault()
    {
        lock (_decisions) FaultCore();
    }

    private void FaultCore()
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
