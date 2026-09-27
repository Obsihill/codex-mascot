namespace CodexMascot.Core;

public sealed class MascotLibrary
{
    public int FolderLayoutVersion { get; set; }
    public double InstalledPanelRatio { get; set; } = 4.0 / 7;
    public string InstalledSort { get; set; } = "installed";
    public List<LibraryMascot> Installed { get; set; } = new();
    public List<LibraryMascot> Selected { get; set; } = new();
    // Read the original ID-only selection format once, then save independent instances.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SelectedIds { get; set; }

    public bool Select(string id)
    {
        var original = Installed.FirstOrDefault(m => m.Id == id);
        if (original is null) return false;
        var copy = System.Text.Json.JsonSerializer.Deserialize<LibraryMascot>(System.Text.Json.JsonSerializer.Serialize(original))!;
        copy.Id = Guid.NewGuid().ToString("N"); copy.SourceId = id;
        Selected.Add(copy);
        return true;
    }

    public LibraryMascot? Find(string id) => Installed.Concat(Selected).FirstOrDefault(m => m.Id == id);
    public IReadOnlyList<LibraryMascot> Eligible(MascotState state) => Selected
        .Where(m => m.Events.Contains(MascotConfiguration.StateKey(state))).ToArray();
}

public sealed class LibraryMascot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceId { get; set; }
    public string Name { get; set; } = "새 마스코트";
    // Unknown for libraries saved before install-time tracking was introduced.
    public DateTimeOffset? InstalledAt { get; set; }
    // Kept only for migrating the first, single-file library format.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Media { get; set; }
    public string? CoverImage { get; set; }
    public Dictionary<string, LibraryEventMedia> States { get; set; } = new();
    public double Volume { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public bool Loop { get; set; }
    public List<string> Events { get; set; } = new() { "running", "needsAttention", "completed", "failed", "interrupted" };
    public string Position { get; set; } = "bottom-right";
    public string? MonitorDevice { get; set; }
    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }

    public LibraryEventMedia For(MascotState state) => States[MascotConfiguration.StateKey(state)];
    public MascotPlaybackSettings Settings(MascotState state) => For(state).Playback ??= new()
    {
        Volume = Volume, Speed = Speed, Loop = Loop, Position = Position,
        MonitorDevice = MonitorDevice, CustomLeft = CustomLeft, CustomTop = CustomTop
    };
}

public sealed class MascotPlaybackSettings
{
    // Null preserves the legacy event duration. Only image playback uses this override.
    public int? ImageDurationMs { get; set; }
    public double Volume { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public bool Loop { get; set; }
    public string Position { get; set; } = "bottom-right";
    public string? MonitorDevice { get; set; }
    public double? CustomLeft { get; set; }
    public double? CustomTop { get; set; }
}

public sealed class LibraryEventMedia
{
    public string? Image { get; set; }
    public bool SoundEnabled { get; set; } = true;
    public MascotPlaybackSettings? Playback { get; set; }
    public int SpriteColumns { get; set; } = 1;
    public int SpriteRows { get; set; } = 1;
    public int FrameDurationMs { get; set; } = 100;
}
