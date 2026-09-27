using CodexMascot.Core;

namespace CodexMascot.App;

// Each selected instance owns a window and audio player. No rotation or shared
// player can replace another selected mascot's media while an event is presented.
internal sealed class MascotPresentationGroup : IDisposable
{
    private sealed record Presentation(string Id, OverlayWindow Overlay, SoundPlayerService Sound);
    private readonly List<Presentation> _active = new();
    public event EventHandler? Clicked;
    public event EventHandler<string>? Feedback;
    public MascotState State { get; private set; }
    public bool IsPresenting => _active.Any(p => p.Overlay.IsPresenting);
    internal IReadOnlyList<OverlayWindow> Windows => _active.Select(p => p.Overlay).ToArray();
    internal static int CompletionVisibleMilliseconds(LibraryStore store, CustomizationManager manager) =>
        store.Library.Eligible(MascotState.Completed)
            .Select(m => LibraryStore.Playback(m, MascotState.Completed, manager.Configuration.For(MascotState.Completed)).ShowDurationMs)
            .Select(ms => ms > 0 ? ms : 4000).DefaultIfEmpty(4000).Max();

    public void Show(LibraryStore store, CustomizationManager manager, MascotState state, bool sound)
    {
        Clear(); State = state;
        foreach (var mascot in store.Library.Eligible(state))
        {
            var overlay = new OverlayWindow(); var player = new SoundPlayerService();
            var config = LibraryStore.Playback(mascot, state, manager.Configuration.For(state));
            var placement = LibraryStore.Placement(mascot, manager.Configuration.Global, state);
            var media = store.MediaPath(mascot, manager, state);
            _active.Add(new(mascot.Id, overlay, player));
            overlay.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
            overlay.IsVisibleChanged += (_, _) => { if (!overlay.IsVisible) player.Stop(); };
            overlay.AudioError += (_, message) => Feedback?.Invoke(this, message);
            player.Feedback += (_, message) => Feedback?.Invoke(this, message);
            overlay.ApplyGlobal(placement);
            overlay.ShowState(state, config, media, sound);
            if (sound && placement.SoundEnabled && config.Volume > 0 && !MascotMedia.IsVideo(media) &&
                state is MascotState.Completed or MascotState.Failed or MascotState.NeedsAttention)
                player.Play(manager.ResolveAsset(config.Sound), config.Volume * placement.MasterVolume, config.PlaybackSpeed);
        }
    }
    public void RemoveIneligible(MascotLibrary library)
    {
        var eligible = library.Eligible(State).Select(m => m.Id).ToHashSet();
        foreach (var entry in _active.Where(p => !eligible.Contains(p.Id)).ToArray())
        { _active.Remove(entry); entry.Sound.Dispose(); entry.Overlay.Close(); }
    }
    public void ApplyPreferences(LibraryStore store, CustomizationManager manager)
    {
        foreach (var entry in _active)
        {
            var mascot = store.Library.Find(entry.Id);
            if (mascot is null) continue;
            var global = LibraryStore.Placement(mascot, manager.Configuration.Global, State);
            entry.Overlay.ApplyGlobal(global);
            entry.Sound.SetVolume(global.SoundEnabled ? mascot.Settings(State).Volume * global.MasterVolume : 0);
        }
    }
    public void Clear()
    {
        var entries = _active.ToArray(); _active.Clear();
        foreach (var entry in entries) { entry.Sound.Dispose(); entry.Overlay.Close(); }
    }
    public void Dispose() => Clear();
}
