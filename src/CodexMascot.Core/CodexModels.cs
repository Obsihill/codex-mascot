using System.Text.Json;

namespace CodexMascot.Core;

public enum MascotState
{
    Idle,
    Running,
    NeedsAttention,
    Completed,
    Failed,
    Interrupted,
    Connecting,
    Disconnected
}

public enum CodexEventKind
{
    ThreadStarted,
    ThreadStatusChanged,
    ThreadClosed,
    TurnStarted,
    TurnCompleted,
    ItemStarted,
    ItemCompleted,
    ApprovalRequired,
    UserInputRequired,
    ServerRequestResolved,
    Warning,
    ConnectionChanged,
    Unknown
}

public sealed record CodexEvent(
    CodexEventKind Kind,
    string SourceId,
    string? ThreadId = null,
    string? TurnId = null,
    string? ItemId = null,
    string? Status = null,
    string? ItemType = null,
    string? Message = null,
    string? RequestId = null,
    DateTimeOffset? OccurredAt = null)
{
    public DateTimeOffset Time { get; init; } = OccurredAt ?? DateTimeOffset.UtcNow;
    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();
    public string? ProjectPath { get; init; }
    public bool IsReplay { get; init; }
}

public sealed record JobSnapshot(
    string ThreadId,
    MascotState State,
    string? TurnId,
    string? LastMessage,
    bool HasUnreadResult,
    bool NeedsAttention,
    DateTimeOffset LastUpdated,
    string? ProjectPath = null,
    string Source = "");

public sealed record AggregationResult(
    MascotState State,
    bool StateChanged,
    bool ShouldNotify,
    string? ThreadId,
    string? Message,
    IReadOnlyList<JobSnapshot> Jobs);

public static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    public static JsonElement? GetObjectOrNull(this JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
            return value;
        return null;
    }

    public static string? FindNestedString(this JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return null;
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null
            : current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }
}
