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
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var root = d.RootElement;
            if (root.GetStringOrNull("type") != "event_msg") return null;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            var name = p.GetStringOrNull("type");
            var turn = p.GetStringOrNull("turn_id");
            var kind = name switch
            {
                "task_started" => CodexEventKind.TurnStarted,
                "task_complete" or "turn_aborted" => CodexEventKind.TurnCompleted,
                "item_completed" => CodexEventKind.ItemCompleted,
                _ => CodexEventKind.Unknown
            };
            if (kind == CodexEventKind.Unknown) return null;
            var time = DateTimeOffset.TryParse(root.GetStringOrNull("timestamp"), out var t) ? t : DateTimeOffset.UtcNow;
            var status = name == "turn_aborted" ? "interrupted" : name == "task_complete" ? "completed" : null;
            return new(kind, "기록 감시", identity.Id, turn, Status: status,
                Message: name switch { "task_started" => "데스크톱/CLI 응답 시작", "task_complete" => "응답 종료 (목표 달성 여부와는 별개)", "turn_aborted" => "사용자가 응답을 중단함", _ => "작업 진행 이벤트 수신" }, OccurredAt: time)
                { ProjectPath = identity.Cwd, IsReplay = replay };
        }
        catch (JsonException) { return null; }
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
