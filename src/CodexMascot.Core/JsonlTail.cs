using System.Text;

namespace CodexMascot.Core;

// A partial UTF-8 line remains on disk until its newline arrives.
public sealed class JsonlTail
{
    public long Offset { get; private set; }
    public bool WasTruncated { get; private set; }
    public JsonlTail(long offset = 0) => Offset = offset;
    public IReadOnlyList<string> Read(string path, int budgetBytes = 4 * 1024 * 1024)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        WasTruncated = file.Length < Offset;
        if (WasTruncated) Offset = 0;
        file.Position = Offset;
        var bytes = new byte[(int)Math.Min(budgetBytes, file.Length - Offset)];
        var n = file.Read(bytes, 0, bytes.Length);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < n; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            lines.Add(Encoding.UTF8.GetString(bytes, start, i - start).TrimEnd('\r'));
            start = i + 1;
        }
        Offset += start;
        if (start == 0 && n == budgetBytes)
        {
            int b;
            while ((b = file.ReadByte()) >= 0) { if (b == 10) { Offset = file.Position; break; } }
        }
        return lines;
    }
}
