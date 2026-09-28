using System.Text.Json;

namespace CodexMascot.Core;

public sealed record SessionIdentity(string Id, string? Cwd, string Origin);

public static class DesktopEventParser
{
    public static SessionIdentity? ReadIdentity(string line)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var root = d.RootElement;
            if (root.GetStringOrNull("type") != "session_meta") return null;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            var id = p.GetStringOrNull("id");
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (p.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object) return null;
            return new(id, p.GetStringOrNull("cwd"), p.GetStringOrNull("originator") ?? p.GetStringOrNull("source") ?? "Codex");
        }
        catch (JsonException) { return null; }
    }

    public static CodexEvent? ParseTranscript(string line, SessionIdentity identity, bool replay = false)
        => ParseTranscript(line, new TranscriptContext { Identity = identity }, replay);

    public static CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay = false)
    {
        var identity = context.Identity;
        try
        {
            using var d = JsonDocument.Parse(line);
            var root = d.RootElement;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            var time = DateTimeOffset.TryParse(root.GetStringOrNull("timestamp"), out var t) ? t : DateTimeOffset.UtcNow;
            if (root.GetStringOrNull("type") == "response_item")
                return ParseQuestion(p, context, time, replay);
            if (root.GetStringOrNull("type") != "event_msg") return null;
            var name = p.GetStringOrNull("type");
            if (name == "user_message" && context.QuestionCalls.Values.Any(async => async))
            {
                foreach (var id in context.QuestionCalls.Where(pair => pair.Value).Select(pair => pair.Key).ToArray())
                    context.QuestionCalls.Remove(id);
                return new(CodexEventKind.ServerRequestResolved, "Codex 기록", identity.Id,
                    Status: "asyncQuestions", Message: "사용자 후속 입력 수신", OccurredAt: time)
                    { ProjectPath = identity.Cwd, IsReplay = replay };
            }
            var turn = p.GetStringOrNull("turn_id");
            var kind = name switch
            {
                "task_started" => CodexEventKind.TurnStarted,
                "task_complete" or "turn_aborted" or "turn_failed" => CodexEventKind.TurnCompleted,
                "item_completed" => CodexEventKind.ItemCompleted,
                _ => CodexEventKind.Unknown
            };
            if (kind == CodexEventKind.Unknown) return null;
            if (kind == CodexEventKind.TurnStarted)
            { context.CurrentTurn = turn; context.QuestionCalls.Clear(); }
            else if (kind == CodexEventKind.TurnCompleted) context.CurrentTurn = null;
            var status = name switch
            {
                "turn_aborted" => "interrupted",
                "turn_failed" => "failed",
                "task_complete" => "completed",
                _ => null
            };
            return new(kind, "기록 감시", identity.Id, turn, Status: status,
                Message: name switch { "task_started" => "데스크톱/CLI 응답 시작", "task_complete" => "응답 종료 (목표 달성 여부와는 별개)", "turn_aborted" => "사용자가 응답을 중단함", "turn_failed" => "응답 실패 신호 수신", _ => "작업 진행 이벤트 수신" }, OccurredAt: time)
                { ProjectPath = identity.Cwd, IsReplay = replay };
        }
        catch (JsonException) { return null; }
    }

    private static CodexEvent? ParseQuestion(JsonElement p, TranscriptContext context, DateTimeOffset time, bool replay)
    {
        // Old prompts must not create fresh alerts when the monitor starts.
        if (replay) return null;
        var call = p.GetStringOrNull("call_id");
        if (string.IsNullOrWhiteSpace(call)) return null;
        var type = p.GetStringOrNull("type");
        CodexEventKind kind;
        bool asyncQuestion;
        if (type == "function_call")
        {
            var name = p.GetStringOrNull("name");
            if (name is not ("request_user_input" or "request_user_input_async" or
                "functions.request_user_input" or "functions.request_user_input_async")) return null;
            asyncQuestion = name.EndsWith("_async", StringComparison.Ordinal);
            if (!context.QuestionCalls.TryAdd(call, asyncQuestion)) return null;
            kind = CodexEventKind.UserInputRequired;
        }
        else if (type == "function_call_output" && context.QuestionCalls.TryGetValue(call, out asyncQuestion))
        {
            // Async tool output only acknowledges delivery; it is not the user's answer.
            if (asyncQuestion && p.GetStringOrNull("output") is { } output)
            {
                using var result = JsonDocument.Parse(output);
                if (result.RootElement.TryGetProperty("accepted", out var accepted) && accepted.ValueKind == JsonValueKind.True)
                    return null;
            }
            context.QuestionCalls.Remove(call);
            kind = CodexEventKind.ServerRequestResolved;
        }
        else return null;
        return new(kind, "Codex 기록", context.Identity.Id, context.CurrentTurn,
            RequestId: (asyncQuestion ? "async-question:" : "question:") + call,
            Message: kind == CodexEventKind.UserInputRequired ? "Codex에서 질문에 답해 주세요." : "질문 요청 종료",
            OccurredAt: time) { ProjectPath = context.Identity.Cwd };
    }

    public static CodexEvent? ParseHook(string line, bool replay = false)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var p = d.RootElement;
            var id = p.GetStringOrNull("session_id");
            if (string.IsNullOrWhiteSpace(id)) return null;
            var name = p.GetStringOrNull("hook_event_name");
            var tool = p.GetStringOrNull("tool_name") ?? "unknown";
            var kind = name switch
            {
                "UserPromptSubmit" => CodexEventKind.TurnStarted,
                "PermissionRequest" => CodexEventKind.ApprovalRequired,
                "PreToolUse" when tool.Contains("request_user_input", StringComparison.OrdinalIgnoreCase) => CodexEventKind.UserInputRequired,
                "PostToolUse" => CodexEventKind.ServerRequestResolved,
                "Stop" or "Interrupt" => CodexEventKind.TurnCompleted,
                "SessionEnd" => CodexEventKind.ThreadClosed,
                _ => CodexEventKind.Unknown
            };
            if (kind == CodexEventKind.Unknown) return null;
            var time = DateTimeOffset.TryParse(p.GetStringOrNull("timestamp"), out var t) ? t : DateTimeOffset.UtcNow;
            return new(kind, "Hook", id, p.GetStringOrNull("turn_id"),
                Status: name == "Interrupt" ? "interrupted" : name == "Stop" ? "completed" : null,
                Message: name == "PermissionRequest" ? "Codex에서 승인해 주세요: " + tool : name == "Stop" ? "응답 종료 신호 수신" : name,
                RequestId: "hook:" + tool, OccurredAt: time) { ProjectPath = p.GetStringOrNull("cwd"), IsReplay = replay };
        }
        catch (JsonException) { return null; }
    }
}
