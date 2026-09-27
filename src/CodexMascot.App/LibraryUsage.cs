using System.Diagnostics;
using System.Text.Json;
using CodexMascot.Core;

namespace CodexMascot.App;

// Usage is not part of undo snapshots. Deleted packages keep their score so undo
// can restore them; duplicates contribute one second per source, not per copy.
internal sealed class LibraryUsage
{
    private readonly string _path;
    private readonly Func<double> _seconds;
    private Dictionary<string, double> _totals = new();
    private HashSet<string> _selected = new();
    private double _last;
    private bool _dirty;
    public string? Warning { get; }
    public LibraryUsage(string path, Func<double>? seconds = null)
    {
        _path = path;
        var clock = Stopwatch.StartNew(); _seconds = seconds ?? (() => clock.Elapsed.TotalSeconds);
        _last = _seconds();
        try
        {
            if (File.Exists(path)) _totals = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path)) ?? new();
            _totals = _totals.Where(p => double.IsFinite(p.Value) && p.Value >= 0).ToDictionary(p => p.Key, p => p.Value);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Warning = "선호 기록을 읽지 못했습니다. " + e.Message;
            // Preserve corrupt data rather than overwriting the only copy.
            try { if (File.Exists(path)) File.Copy(path, path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            { Warning += " 백업 실패: " + backupError.Message; _canSave = false; }
        }
    }
    public void Track(IEnumerable<LibraryMascot> selected)
    {
        Accrue(); _selected = selected.Select(m => m.SourceId).OfType<string>().ToHashSet();
    }
    private double Pending => Math.Clamp(_seconds() - _last, 0, 60); // Ignore long suspension gaps between 30-second ticks.
    private void Accrue()
    {
        var elapsed = Pending; _last = _seconds();
        foreach (var id in _selected) { _totals[id] = _totals.GetValueOrDefault(id) + elapsed; _dirty |= elapsed > 0; }
    }
    public double Score(string id) => _totals.GetValueOrDefault(id) + (_selected.Contains(id) ? Pending : 0);
    public void Save()
    {
        Accrue(); if (!_dirty || !_canSave) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_totals));
        File.Move(_path + ".tmp", _path, true); _dirty = false;
    }
    private readonly bool _canSave = true;
    public IEnumerable<LibraryMascot> Sort(MascotLibrary library)
    {
        var pending = Pending;
        return library.InstalledSort switch
        {
            "name" => library.Installed.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase),
            "preference" => library.Installed.OrderByDescending(m => _totals.GetValueOrDefault(m.Id) + (_selected.Contains(m.Id) ? pending : 0)).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => library.Installed.OrderByDescending(m => m.InstalledAt ?? DateTimeOffset.MinValue)
        };
    }
}
