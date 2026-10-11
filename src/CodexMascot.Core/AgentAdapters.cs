namespace CodexMascot.Core;

public enum AgentKind { Codex, Claude, Gemini, Qwen, Copilot, OpenCode, Cursor, Antigravity, Kimi, Windsurf, Kiro, Amp }

// Claude transcripts carry no turn identifier, so the adapter derives one from the
// last human prompt and keeps it here per file. Codex adapters ignore this state.
public sealed class TranscriptContext
{
    public required SessionIdentity Identity { get; init; }
    public string? CurrentTurn { get; set; }
    // Claude writes one assistant response as several records that all repeat its stop reason.
    public string? LastRequest { get; set; }
    internal Dictionary<string, bool> QuestionCalls { get; } = new(StringComparer.Ordinal);
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
    public static IAgentAdapter Gemini { get; } = new HookOnlyAgentAdapter(AgentKind.Gemini, "Gemini CLI");
    public static IAgentAdapter Qwen { get; } = new HookOnlyAgentAdapter(AgentKind.Qwen, "Qwen Code");
    public static IAgentAdapter Copilot { get; } = new HookOnlyAgentAdapter(AgentKind.Copilot, "Copilot CLI");
    public static IAgentAdapter OpenCode { get; } = new HookOnlyAgentAdapter(AgentKind.OpenCode, "OpenCode");
    public static IAgentAdapter Cursor { get; } = new HookOnlyAgentAdapter(AgentKind.Cursor, "Cursor");
    public static IAgentAdapter Antigravity { get; } = new HookOnlyAgentAdapter(AgentKind.Antigravity, "Antigravity");
    public static IAgentAdapter Kimi { get; } = new HookOnlyAgentAdapter(AgentKind.Kimi, "Kimi Code");
    public static IAgentAdapter Windsurf { get; } = new HookOnlyAgentAdapter(AgentKind.Windsurf, "Windsurf/Devin Desktop");
    public static IAgentAdapter Kiro { get; } = new HookOnlyAgentAdapter(AgentKind.Kiro, "Kiro CLI V3");
    public static IAgentAdapter Amp { get; } = new HookOnlyAgentAdapter(AgentKind.Amp, "Amp");
    public static IAgentAdapter For(AgentKind kind) => kind switch
    {
        AgentKind.Codex => Codex, AgentKind.Claude => Claude, AgentKind.Gemini => Gemini,
        AgentKind.Qwen => Qwen, AgentKind.Copilot => Copilot, AgentKind.OpenCode => OpenCode,
        AgentKind.Cursor => Cursor, AgentKind.Antigravity => Antigravity,
        AgentKind.Kimi => Kimi, AgentKind.Windsurf => Windsurf, AgentKind.Kiro => Kiro, AgentKind.Amp => Amp,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static AgentKind Parse(string? name) => Enum.TryParse<AgentKind>(name, true, out var kind) && Enum.IsDefined(kind)
        ? kind : throw new ArgumentException("Unknown agent kind", nameof(name));
}

// These agents expose supported hook events but do not share Codex/Claude's JSONL layout.
// Never interpret an unrelated transcript as a Codex transcript.
public sealed class HookOnlyAgentAdapter(AgentKind kind, string displayName) : IAgentAdapter
{
    public AgentKind Kind => kind;
    public string DisplayName => displayName;
    public string DefaultHome => kind switch
    {
        AgentKind.Gemini => Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? "",
        AgentKind.Qwen => Environment.GetEnvironmentVariable("QWEN_HOME") ?? "",
        AgentKind.Copilot => Environment.GetEnvironmentVariable("COPILOT_HOME") ?? "",
        AgentKind.OpenCode => Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR") ?? "",
        AgentKind.Cursor => "",
        AgentKind.Antigravity => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini"),
        AgentKind.Kimi => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code"),
        AgentKind.Windsurf => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf"),
        AgentKind.Kiro => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro"),
        AgentKind.Amp => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "amp"),
        _ => ""
    };
    public string HookConfigFileName => kind switch
    {
        AgentKind.Copilot => Path.Combine("hooks", "agent-mascot.json"),
        AgentKind.OpenCode => Path.Combine("plugins", "agent-mascot.js"),
        AgentKind.Cursor => "hooks.json",
        AgentKind.Antigravity => Path.Combine("config", "hooks.json"),
        AgentKind.Kimi => "config.toml",
        AgentKind.Windsurf => "hooks.json",
        AgentKind.Kiro => Path.Combine("hooks", "agent-mascot.json"),
        AgentKind.Amp => Path.Combine("plugins", "agent-mascot.js"),
        _ => "settings.json"
    };
    public string TranscriptSource => displayName + " 기록";
    public string HookSource => displayName + " Hook";
    public int HeadLines => 0;
    public string SessionsRoot(string home) => Path.Combine(home, ".agent-mascot-no-transcripts");
    public string MissingRootMessage => displayName + ": Hook 수신 대기 중";
    public SessionIdentity? ReadIdentity(IReadOnlyList<string> head) => null;
    public CodexEvent? ParseTranscript(string line, TranscriptContext context, bool replay) => null;
    public CodexEvent? ParseHook(string json, bool replay) => ExternalHookParser.Parse(kind, json, replay);
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
        => DesktopEventParser.ParseTranscript(line, context, replay);
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
