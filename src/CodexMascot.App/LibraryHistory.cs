namespace CodexMascot.App;

// Session-only history of library settings. Media files are never deleted by undo/redo.
internal sealed class LibraryHistory
{
    private sealed record Entry(string Before, string After, string Description, string? MergeKey);
    private readonly LibraryStore _store;
    private readonly List<Entry> _entries = new();
    private int _cursor;
    private bool _merge;
    public LibraryHistory(LibraryStore store) => _store = store;
    public void BreakMerge() => _merge = false;
    public bool Commit(string description, Action change, string? mergeKey = null)
    {
        var before = _store.Snapshot();
        string after;
        try
        {
            change(); after = _store.Snapshot();
            if (before == after) return false;
            _store.Save();
            after = _store.Snapshot();
        }
        catch { _store.Restore(before); throw; }
        if (_cursor < _entries.Count) _entries.RemoveRange(_cursor, _entries.Count - _cursor);
        if (_merge && mergeKey is not null && _cursor > 0 && _entries[_cursor - 1].MergeKey == mergeKey && _entries[_cursor - 1].After == before)
            _entries[_cursor - 1] = _entries[_cursor - 1] with { After = after, Description = description };
        else { _entries.Add(new(before, after, description, mergeKey)); _cursor++; }
        if (_entries.Count > 100) { _entries.RemoveAt(0); _cursor--; }
        _merge = mergeKey is not null;
        return true;
    }
    public string? Undo()
    {
        BreakMerge(); if (_cursor == 0) return null;
        var entry = _entries[_cursor - 1]; Apply(entry.Before); _cursor--; return entry.Description;
    }
    public string? Redo()
    {
        BreakMerge(); if (_cursor == _entries.Count) return null;
        var entry = _entries[_cursor]; Apply(entry.After); _cursor++; return entry.Description;
    }
    private void Apply(string snapshot)
    {
        var before = _store.Snapshot();
        try { _store.Restore(snapshot); _store.Save(); }
        catch { _store.Restore(before); throw; }
    }
}
