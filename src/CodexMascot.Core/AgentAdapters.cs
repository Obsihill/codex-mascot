namespace CodexMascot.Core;

public enum AgentKind { Codex, Claude }

// Claude transcripts carry no turn identifier, so the adapter derives one from the
// last human prompt and keeps it here per file. Codex adapters ignore this state.
public sealed class TranscriptContext
{
    public required SessionIdentity Identity { get; init; }
    public string? CurrentTurn { get; set; }
    // Claude writes one assistant response as several records that all repeat its stop reason.
    public string? LastRequest { get; set; }
}

// One agent's on-disk conventions. Nothing here writes to the agent or controls it.
public interface IAgentAdapter
{
    AgentKind Kind { get; }
    string DisplayName { get; }
    string DefaultHome { get; }
    string HookConfigFileName { get; }
    string TranscriptSource { get; }
    string HookSource { get; }
    int HeadLines { get; }
    string SessionsRoot(string home);
    string MissingRootMessage { get; }
    SessionIdentity? ReadIdentity(IReadOnlyList<string> head);
    CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay);
    CodexEvent? ParseHook(string json, bool replay);
}

public static class AgentAdapters
{
    public static IAgentAdapter Codex { get; } = new CodexAgentAdapter();
    public static IAgentAdapter Claude { get; } = new ClaudeAgentAdapter();
    public static IAgentAdapter For(AgentKind kind) => kind == AgentKind.Claude ? Claude : Codex;
    public static AgentKind Parse(string? name) =>
        string.Equals(name, "claude", StringComparison.OrdinalIgnoreCase) ? AgentKind.Claude : AgentKind.Codex;
}

public sealed class CodexAgentAdapter : IAgentAdapter
{
    public AgentKind Kind => AgentKind.Codex;
    public string DisplayName => "Codex";
    public string DefaultHome => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public string HookConfigFileName => "hooks.json";
    public string TranscriptSource => "Codex 기록";
    public string HookSource => "Codex Hook";
    public int HeadLines => 1;
    public string SessionsRoot(string home) => Path.Combine(home, "sessions");
    public string MissingRootMessage => "sessions 폴더가 없습니다. Codex 데이터 폴더(.codex)를 선택하세요.";
    public SessionIdentity? ReadIdentity(IReadOnlyList<string> head)
        => head.Count == 0 ? null : DesktopEventParser.ReadIdentity(head[0]);
    public CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay)
        => DesktopEventParser.ParseTranscript(line, context.Identity, replay);
    public CodexEvent? ParseHook(string json, bool replay) => DesktopEventParser.ParseHook(json, replay);
}

public sealed class ClaudeAgentAdapter : IAgentAdapter
{
    public AgentKind Kind => AgentKind.Claude;
    public string DisplayName => "Claude Code";
    public string DefaultHome => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    // Claude Code merges hooks from settings.json; settings.local.json stays the user's own file.
    public string HookConfigFileName => "settings.json";
    public string TranscriptSource => "Claude 기록";
    public string HookSource => "Claude Hook";
    // The session id appears on the first record but cwd only from the first real message.
    public int HeadLines => 24;
    public string SessionsRoot(string home) => Path.Combine(home, "projects");
    public string MissingRootMessage => "projects 폴더가 없습니다. Claude 데이터 폴더(.claude)를 선택하세요.";
    public SessionIdentity? ReadIdentity(IReadOnlyList<string> head) => ClaudeEventParser.ReadIdentity(head);
    public CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay)
        => ClaudeEventParser.ParseTranscript(line, context, replay);
    public CodexEvent? ParseHook(string json, bool replay) => ClaudeEventParser.ParseHook(json, replay);
}
