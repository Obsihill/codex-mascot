using System.Text.Json;

namespace CodexMascot.Core;

// Only lifecycle metadata from the relay is accepted; prompts and tool arguments are never read.
public static class ExternalHookParser
{
    public static CodexEvent? Parse(AgentKind agent, string json, bool replay = false)
    {
        if (agent is not (AgentKind.Gemini or AgentKind.Qwen or AgentKind.Copilot or AgentKind.OpenCode or AgentKind.Cursor
            or AgentKind.Antigravity or AgentKind.Kimi or AgentKind.Windsurf or AgentKind.Kiro or AgentKind.Amp)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object) return null;
            var id = Text(value, "session_id");
            var name = Text(value, "hook_event_name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
            var notification = Text(value, "notification_type");
            var kind = name switch
            {
                "BeforeAgent" or "UserPromptSubmit" or "userPromptSubmitted" or "beforeSubmitPrompt" or "session.busy"
                    or "PreInvocation" or "pre_user_prompt" or "TurnStarted" or "agent.start" => CodexEventKind.TurnStarted,
                "AfterAgent" or "Stop" or "agentStop" or "stop" or "session.idle" or "post_cascade_response" or "agent.end" => CodexEventKind.TurnCompleted,
                "StopFailure" or "session.error" or "Interrupt" => CodexEventKind.TurnCompleted,
                "PermissionRequest" or "permissionRequest" or "permission.asked" => CodexEventKind.ApprovalRequired,
                "PermissionResult" => CodexEventKind.ServerRequestResolved,
                "question.asked" => CodexEventKind.UserInputRequired,
                "permission.replied" or "question.replied" => CodexEventKind.ServerRequestResolved,
                "Notification" or "notification" when notification is "ToolPermission" or "permission_prompt" => CodexEventKind.ApprovalRequired,
                "Notification" or "notification" when notification == "elicitation_dialog" => CodexEventKind.UserInputRequired,
                "PreToolUse" when Text(value, "tool_name") is "AskUserQuestion" or "ask_user" => CodexEventKind.UserInputRequired,
                "PostToolUse" or "permissionReplied" => CodexEventKind.ServerRequestResolved,
                "SessionEnd" or "sessionEnd" or "session.deleted" => CodexEventKind.ThreadClosed,
                _ => CodexEventKind.Unknown
            };
            if (kind == CodexEventKind.Unknown) return null;
            var source = AgentAdapters.For(agent).HookSource;
            var time = DateTimeOffset.TryParse(Text(value, "timestamp"), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            var status = name == "Interrupt" || name == "agent.end" && Text(value, "agent_status") == "cancelled" ? "interrupted"
                : name == "agent.end" && Text(value, "agent_status") == "error" ? "failed"
                : name is "StopFailure" or "session.error" ||
                (name == "Stop" && Text(value, "termination_reason") == "error")
                ? "failed" : kind == CodexEventKind.TurnCompleted ? "completed" : null;
            return new CodexEvent(kind, source, id, TurnId: Text(value, "turn_id"), Status: status, Message: name,
                RequestId: Text(value, "request_id"), OccurredAt: time)
            {
                ProjectPath = Text(value, "cwd"), IsReplay = replay
            };
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
