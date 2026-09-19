using System.Text.Json;

namespace CodexMascot.Core;

public static class CodexEventNormalizer
{
    public static CodexEvent? FromJsonLine(string json, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var method = root.GetStringOrNull("method");
            var cliType = root.GetStringOrNull("type");
            var name = method ?? cliType;

            if (string.IsNullOrWhiteSpace(name))
                return null;

            var parameters = root.GetObjectOrNull("params") ?? root;
            var threadId = parameters.FindNestedString("threadId")
                ?? parameters.FindNestedString("thread_id")
                ?? parameters.FindNestedString("thread", "id")
                ?? root.FindNestedString("threadId")
                ?? sourceId;
            var turnId = parameters.FindNestedString("turnId")
                ?? parameters.FindNestedString("turn_id")
                ?? parameters.FindNestedString("turn", "id")
                ?? root.FindNestedString("turnId");
            var itemId = parameters.FindNestedString("itemId")
                ?? parameters.FindNestedString("item", "id")
                ?? root.FindNestedString("itemId");
            var status = parameters.FindNestedString("status", "type")
                ?? parameters.FindNestedString("status")
                ?? parameters.FindNestedString("turn", "status")
                ?? parameters.FindNestedString("item", "status")
                ?? root.FindNestedString("status");
            var itemType = parameters.FindNestedString("item", "type")
                ?? root.FindNestedString("item", "type");
            var requestId = parameters.GetStringOrNull("requestId") ?? root.GetStringOrNull("id");
            var message = parameters.FindNestedString("message")
                ?? parameters.FindNestedString("error", "message")
                ?? parameters.FindNestedString("turn", "error", "message")
                ?? root.FindNestedString("error", "message");

            var kind = NormalizeKind(name);
            if (name == "turn.failed") status = "failed";
            if (kind == CodexEventKind.Unknown)
                return null;

            var activeFlags = Array.Empty<string>();
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("status", out var statusObject)
                && statusObject.ValueKind == JsonValueKind.Object
                && statusObject.TryGetProperty("activeFlags", out var flags)
                && flags.ValueKind == JsonValueKind.Array)
            {
                activeFlags = flags.EnumerateArray()
                    .Where(flag => flag.ValueKind == JsonValueKind.String)
                    .Select(flag => flag.GetString()!)
                    .ToArray();
            }

            return new CodexEvent(
                kind,
                sourceId,
                threadId,
                turnId,
                itemId,
                status,
                itemType,
                message,
                requestId,
                DateTimeOffset.UtcNow)
            {
                ActiveFlags = activeFlags
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CodexEventKind NormalizeKind(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized switch
        {
            "thread/started" or "thread.started" => CodexEventKind.ThreadStarted,
            "thread/status/changed" or "thread.status.changed" => CodexEventKind.ThreadStatusChanged,
            "thread/closed" or "thread.closed" => CodexEventKind.ThreadClosed,
            "turn/started" or "turn.started" => CodexEventKind.TurnStarted,
            "turn/completed" or "turn.completed" or "turn.failed" => CodexEventKind.TurnCompleted,
            "item/started" or "item.started" => CodexEventKind.ItemStarted,
            "item/completed" or "item.completed" => CodexEventKind.ItemCompleted,
            "item/commandexecution/requestapproval" or "item/command_execution/request_approval"
                or "item/filechange/requestapproval"
                or "command_execution/request_approval" => CodexEventKind.ApprovalRequired,
            "item/tool/requestuserinput" or "tool/requestuserinput" or "tool/request_user_input"
                or "item/tool/request_user_input" => CodexEventKind.UserInputRequired,
            "serverrequest/resolved" or "server_request/resolved" => CodexEventKind.ServerRequestResolved,
            "warning" or "configwarning" or "error" => CodexEventKind.Warning,
            _ => CodexEventKind.Unknown
        };
    }
}
