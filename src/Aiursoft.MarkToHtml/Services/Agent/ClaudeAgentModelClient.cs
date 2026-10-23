using System.Text.Json;
using Aiursoft.AgentKit;
using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Opt-in model boundary only; intentionally not registered with DI or used by AgentService.
/// Supply ToolRegistry order explicitly because AgentModelRequest sorts tool descriptors.
/// One instance per session, sequential calls only, append-only history. Reasoning is stored
/// by absolute transcript index (including system/user/tool messages), not inferred from text/IDs.
/// Repeating the same input prefix is allowed; it replaces the pending completion at that index.
/// A different session or edited history requires a new instance.
/// </summary>
public sealed class ClaudeAgentModelClient : IAgentModelClient
{
    private readonly ClaudeClient _client;
    private readonly string[] _toolOrder;
    private readonly int _maxTokens;
    private readonly Dictionary<int, string?> _reasoning;
    private JArray? _prefix;
    private int _pendingIndex;
    private JToken? _pendingAssistant;

    // initialReasoning imports existing assistant reasoning by absolute transcript index.
    public ClaudeAgentModelClient(ClaudeClient client, IEnumerable<string> toolOrder, int maxTokens = 4096,
        IReadOnlyDictionary<int, string?>? initialReasoning = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(toolOrder);
        if (maxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        _client = client;
        _toolOrder = toolOrder.ToArray();
        _maxTokens = maxTokens;
        _reasoning = initialReasoning?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
    }

    // Independent read-only snapshot; includes pending completion reasoning. Project only with
    // the matching canonical transcript after that completion has been appended. Sequential use only.
    internal IReadOnlyDictionary<int, string?> GetReasoningSnapshot() =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, string?>(new Dictionary<int, string?>(_reasoning));

    public async ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Snapshots guard association at an index; they never search for matching text or IDs.
        var prefix = JArray.FromObject(request.Transcript);
        if (_prefix != null)
        {
            if (prefix.Count < _prefix.Count || !_prefix.Select((message, index) => JToken.DeepEquals(message, prefix[index])).All(equal => equal))
                throw new InvalidOperationException("This adapter requires append-only session history; create a new instance for edited history.");
            if (prefix.Count > _pendingIndex && !JToken.DeepEquals(prefix[_pendingIndex], _pendingAssistant))
                throw new InvalidOperationException("The appended assistant does not match the pending completion; create a new session adapter.");
        }
        foreach (var index in _reasoning.Keys.Where(index => index != _pendingIndex || _prefix == null || prefix.Count > _pendingIndex))
        {
            if (index < 0 || index >= request.Transcript.Count || request.Transcript[index].Role != TranscriptRole.Assistant)
                throw new ArgumentException("Reasoning replay indices must identify assistant transcript messages.", nameof(request));
        }

        var descriptors = request.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        if (_toolOrder.Length != descriptors.Count || _toolOrder.Distinct(StringComparer.Ordinal).Count() != _toolOrder.Length ||
            _toolOrder.Any(name => !descriptors.ContainsKey(name)))
            throw new ArgumentException("Explicit transport tool order must contain each request tool exactly once.", nameof(request));
        var tools = _toolOrder.Select(name =>
        {
            var tool = descriptors[name];
            return new ClaudeTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = JsonSerializer.Deserialize<JsonElement>(tool.InputSchema.ToString(Formatting.None))
            };
        }).ToList();

        var system = request.Transcript.FirstOrDefault(message => message.Role == TranscriptRole.System);
        var systemPrompt = system == null ? "" : GetText(system);
        var messages = new List<ClaudeMessage>();
        for (var index = 0; index < request.Transcript.Count; index++)
        {
            var message = request.Transcript[index];
            switch (message.Role)
            {
                case TranscriptRole.User:
                    messages.Add(ClaudeMessage.User(GetText(message)));
                    break;
                case TranscriptRole.Assistant:
                    var blocks = new List<ClaudeContentBlock>();
                    var text = GetText(message);
                    if (!string.IsNullOrWhiteSpace(text)) blocks.Add(ClaudeContentBlock.TextBlock(text));
                    foreach (var call in message.ToolCalls)
                    {
                        // Old malformed argument strings became {}; AgentKit already requires JSON.
                        var input = call.Arguments is JObject obj
                            ? JsonConvert.DeserializeObject<Dictionary<string, object?>>(obj.ToString(Formatting.None))!
                            : new Dictionary<string, object?>();
                        blocks.Add(ClaudeContentBlock.ToolUse(call.Id, call.Name, input));
                    }
                    _reasoning.TryGetValue(index, out var reasoning);
                    messages.Add(ClaudeMessage.Assistant(blocks, reasoning));
                    break;
                case TranscriptRole.Tool:
                    foreach (var result in message.ToolResults)
                    {
                        if (string.IsNullOrWhiteSpace(result.CallId))
                            throw new NotSupportedException("AgentKit replay requires a nonblank tool result ID; IDs are never synthesized.");
                        messages.Add(ClaudeMessage.ToolResult(result.CallId, AgentToolResultText.GetText(result)));
                    }
                    break;
            }
        }

        var response = await _client.SendAsync(systemPrompt, messages, tools, cancellationToken, _maxTokens);
        var content = new List<AgentContentBlock>();
        // Preserve legacy GetText's newline join, not AgentKit's concatenation of text blocks.
        var responseText = response.GetText();
        if (response.Content.Any(block => block.Type == "text" && block.Text != null))
            content.Add(new TextBlock(responseText));
        foreach (var call in response.GetToolUses())
        {
            if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name))
                throw new NotSupportedException("AgentKit requires nonblank tool call IDs and names; legacy malformed calls cannot be represented and IDs are never synthesized.");
            var arguments = JsonSerializer.Serialize(call.Input ?? new Dictionary<string, object?>());
            content.Add(new ToolCallBlock(new ToolCall(call.Id, call.Name, JToken.Parse(arguments))));
        }
        var completion = new AgentModelResponse(content,
            content.OfType<ToolCallBlock>().Any() ? AgentFinishReason.ToolCalls : AgentFinishReason.Stop);
        _prefix = prefix;
        _pendingIndex = request.Transcript.Count;
        _pendingAssistant = JToken.FromObject(TranscriptMessage.Assistant(completion.Content));
        _reasoning[_pendingIndex] = response.ReasoningContent;
        return completion;
    }

    private static string GetText(TranscriptMessage message) => string.Concat(message.Content.OfType<TextBlock>().Select(block => block.Text));
}

