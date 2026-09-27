namespace CodexMascot.Core;

public sealed class MascotConfiguration
{
    public int Version { get; set; } = 1;
    public string Profile { get; set; } = "default";
    public GlobalConfiguration Global { get; set; } = new();
    public MonitorConfiguration Monitor { get; set; } = new();
    public Dictionary<string, StateConfiguration> States { get; set; } = CreateDefaults();

    public StateConfiguration For(MascotState state)
    {
        var key = StateKey(state);
        return States.TryGetValue(key, out var configuration) ? configuration : new StateConfiguration();
    }

    public static string StateKey(MascotState state) => state switch
    {
        MascotState.NeedsAttention => "needsAttention",
        MascotState.Running => "running",
        MascotState.Completed => "completed",
        MascotState.Failed => "failed",
        MascotState.Interrupted => "interrupted",
        _ => "idle"
    };

    private static Dictionary<string, StateConfiguration> CreateDefaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new() { Image = "assets/images/idle.png", ShowDurationMs = 0, Loop = true },
        ["running"] = new() { Image = "assets/images/running.png", ShowDurationMs = 1200, Loop = false },
        ["needsAttention"] = new() { Image = "assets/images/attention.png", Sound = "assets/sounds/attention.wav", ShowDurationMs = 0, Loop = true },
        ["completed"] = new() { Image = "assets/images/completed.png", Sound = "assets/sounds/completed.wav", ShowDurationMs = 4000, Loop = false },
        ["failed"] = new() { Image = "assets/images/failed.png", Sound = "assets/sounds/failed.wav", ShowDurationMs = 6000, Loop = false, Volume = 1.0 },
        ["interrupted"] = new() { Image = "assets/images/interrupted.png", ShowDurationMs = 3000, Loop = false }
    };
}

public sealed class GlobalConfiguration
{
    public double Scale { get; set; } = 1.0;
    public string Position { get; set; } = "bottom-right";
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; } = false;
    public bool KeepCompletedVisibleUntilClick { get; set; } = true;
    public bool BringCodexToFrontOnClick { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public double MasterVolume { get; set; } = 1;
    public string? MonitorDevice { get; set; }
    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }
    public bool ShowIdle { get; set; }
    public bool StartWithWindows { get; set; }
}

public sealed class StateConfiguration
{
    public double PlaybackSpeed { get; set; } = 1;
    public string? Image { get; set; }
    public string? Sound { get; set; }
    public int ShowDurationMs { get; set; }
    public bool Loop { get; set; }
    public double Volume { get; set; } = 1;
    public int SpriteColumns { get; set; } = 1;
    public int SpriteRows { get; set; } = 1;
    public int FrameDurationMs { get; set; } = 100;
}

public sealed class MonitorConfiguration
{
    public string? CodexHome { get; set; }
    public string? ClaudeHome { get; set; }
    // "codex", "claude" or "both": which agents the monitor watches.
    public string Provider { get; set; } = "both";
    public string? ProjectFilter { get; set; }
    public bool AutoStart { get; set; } = true;
    public string? CodexExecutable { get; set; }
    public List<string> RecentProjects { get; set; } = new();
}
