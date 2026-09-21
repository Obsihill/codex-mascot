using System.Text.Json;

namespace CodexMascot.Core;

// Read-only observer for Claude Code's local transcript format. Claude records no explicit
// task_started/task_complete event, so a turn is inferred from the human prompt and the
// assistant stop reason. Nothing here guesses a result the transcript does not state.
public static class ClaudeEventParser
{
    private const string TranscriptSource = "Claude 기록";
    private const string HookSource = "Claude Hook";
    internal const string InterruptMarker = "[Request interrupted by user";

    public static SessionIdentity? ReadIdentity(IReadOnlyList<string> head)
    {
        string? id = null, cwd = null, version = null;
        foreach (var line in head)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var d = JsonDocument.Parse(line);
                var root = d.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                id ??= Clean(root.GetStringOrNull("sessionId"));
                cwd ??= Clean(root.GetStringOrNull("cwd"));
                version ??= Clean(root.GetStringOrNull("version"));
                if (id is not null && cwd is not null) break;
            }
            catch (JsonException) { }
        }
        return id is null ? null : new SessionIdentity(id, cwd, version is null ? "Claude Code" : "Claude Code " + version);
    }

    public static CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay = false)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var root = d.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // Subagent traffic shares the session file; the mascot reports the main task only.
            if (IsTrue(root, "isSidechain") || IsTrue(root, "isMeta") || IsTrue(root, "isCompactSummary")) return null;
            var type = root.GetStringOrNull("type");
            if (type is not ("user" or "assistant")) return null;
            var time = DateTimeOffset.TryParse(root.GetStringOrNull("timestamp"), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            var project = Clean(root.GetStringOrNull("cwd")) ?? context.Identity.Cwd;

            CodexEvent Event(CodexEventKind kind, string? status, string message, string? turn)
                => new(kind, TranscriptSource, context.Identity.Id, turn, Status: status, Message: message, OccurredAt: time)
                    { ProjectPath = project, IsReplay = replay };

            if (type == "user")
            {
                if (IsInterrupt(root))
                {
                    var aborted = context.CurrentTurn;
                    context.CurrentTurn = null;
                    return Event(CodexEventKind.TurnCompleted, "interrupted", "사용자가 응답을 중단함", aborted);
                }
                if (!IsHumanPrompt(root)) return Event(CodexEventKind.ItemCompleted, null, "도구 결과 수신", context.CurrentTurn);
                context.CurrentTurn = Clean(root.GetStringOrNull("promptId")) ?? Clean(root.GetStringOrNull("uuid"));
                return Event(CodexEventKind.TurnStarted, null, "Claude 응답 시작", context.CurrentTurn);
            }

            // One assistant response is split across several records that all carry the same
            // requestId and the same stop reason, so only its first record is reported.
            var request = Clean(root.GetStringOrNull("requestId"));
            if (request is not null && request == context.LastRequest) return null;
            context.LastRequest = request;
            var stop = Child(root, "message") is { } assistant ? assistant.GetStringOrNull("stop_reason") : null;
            if (stop is "end_turn" or "stop_sequence")
            {
                var finished = context.CurrentTurn;
                context.CurrentTurn = null;
                return Event(CodexEventKind.TurnCompleted, "completed", "응답 종료 (목표 달성 여부와는 별개)", finished);
            }
            // max_tokens and refusal end the request without stating a result, so the turn stays open
            // and the staleness timer reports 확인 필요 instead of a guessed completion.
            return Event(CodexEventKind.ItemCompleted, null,
                stop == "tool_use" ? "도구 실행 진행 중" : "작업 진행 이벤트 수신", context.CurrentTurn);
        }
        catch (JsonException) { return null; }
    }

    public static CodexEvent? ParseHook(string json, bool replay = false)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var p = d.RootElement;
            if (p.ValueKind != JsonValueKind.Object) return null;
            var id = Clean(p.GetStringOrNull("session_id"));
            if (id is null) return null;
            var name = p.GetStringOrNull("hook_event_name");
            var tool = Clean(p.GetStringOrNull("tool_name"));
            // SubagentStop, SessionStart and PreCompact stay silent: they are not the user's task result.
            var kind = name switch
            {
                "UserPromptSubmit" => CodexEventKind.TurnStarted,
                "Notification" => CodexEventKind.ApprovalRequired,
                "PreToolUse" when IsQuestionTool(tool) => CodexEventKind.UserInputRequired,
                "PostToolUse" => CodexEventKind.ServerRequestResolved,
                "Stop" => CodexEventKind.TurnCompleted,
                "SessionEnd" => CodexEventKind.ThreadClosed,
                _ => CodexEventKind.Unknown
            };
            if (kind == CodexEventKind.Unknown) return null;
            var time = DateTimeOffset.TryParse(p.GetStringOrNull("timestamp"), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            var message = name switch
            {
                "Notification" => "Claude에서 확인해 주세요" + (tool is null ? "" : ": " + tool),
                "PreToolUse" => "Claude가 질문을 표시했습니다: " + (tool ?? "질문"),
                "Stop" => "응답 종료 신호 수신",
                "UserPromptSubmit" => "프롬프트 전송 수신",
                _ => name ?? "Hook"
            };
            // Claude's Notification carries no tool name, so a PostToolUse cannot be paired with it
            // by name. A tool that ran means the prompt in front of it was answered, so PostToolUse
            // clears every pending request for the session instead of one by name.
            var requestId = name == "PostToolUse" ? null : "hook:" + (tool ?? name ?? "unknown");
            return new CodexEvent(kind, HookSource, id, Clean(p.GetStringOrNull("turn_id")),
                Status: name == "Stop" ? "completed" : null, Message: message,
                RequestId: requestId, OccurredAt: time)
                { ProjectPath = Clean(p.GetStringOrNull("cwd")), IsReplay = replay };
        }
        catch (JsonException) { return null; }
    }

    // Claude asks the user through these tools; everything else is ordinary work.
    private static bool IsQuestionTool(string? tool) => tool is not null &&
        (tool.Contains("AskUserQuestion", StringComparison.OrdinalIgnoreCase) ||
         tool.Contains("ExitPlanMode", StringComparison.OrdinalIgnoreCase));

    private static bool IsHumanPrompt(JsonElement root)
    {
        if (Child(root, "origin") is { ValueKind: JsonValueKind.Object } origin)
            return origin.GetStringOrNull("kind") == "human";
        // Records written before origin existed: a plain string prompt with no tool result is the user's own turn.
        if (root.TryGetProperty("toolUseResult", out _)) return false;
        return Child(root, "message") is { } message && Child(message, "content") is { ValueKind: JsonValueKind.String };
    }

    // A real interrupt is the user's own record: a text block that begins with the marker, with no
    // tool result attached. Tool output that merely quotes the phrase must not end the turn, so the
    // marker is never looked for inside tool_result blocks or anywhere but the start of the text.
    private static bool IsInterrupt(JsonElement root)
    {
        if (root.TryGetProperty("toolUseResult", out _)) return false;
        if (Child(root, "message") is not { } message) return false;
        if (Child(message, "content") is not { } content) return false;
        if (content.ValueKind == JsonValueKind.String) return StartsWithMarker(content.GetString());
        if (content.ValueKind != JsonValueKind.Array) return false;
        foreach (var block in content.EnumerateArray())
            if (block.ValueKind == JsonValueKind.Object && block.GetStringOrNull("type") == "text"
                && StartsWithMarker(block.GetStringOrNull("text"))) return true;
        return false;
    }

    private static bool StartsWithMarker(string? text)
        => text is not null && text.TrimStart().StartsWith(InterruptMarker, StringComparison.Ordinal);

    private static JsonElement? Child(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value : null;

    private static bool IsTrue(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
