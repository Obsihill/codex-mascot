using System.Text;

namespace CodexMascot.App;

// Original, short synthesized chimes. No external recordings or licenses required.
internal static class DemoMascotSound
{
    internal static byte[] Create(string key)
    {
        const int rate = 44100;
        var parts = key.Split('/');
        var pitch = 360 + parts[0].Sum(c => (int)c) % 260;
        var state = parts.Last();
        double[] notes = state switch
        {
            "completed" => new[] { 1d, 1.25, 1.5 },
            "failed" => new[] { 1d, .8, .65 },
            "needsAttention" => new[] { 1d, 1.4, 1d },
            "interrupted" => new[] { 1d, .75 },
            "running" => new[] { .8, 1d },
            _ => new[] { .65 }
        };
        const int samplesPerNote = 6615;
        var count = samplesPerNote * notes.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        foreach (var note in notes)
            for (var i = 0; i < samplesPerNote; i++)
            {
                var envelope = Math.Sin(Math.PI * i / samplesPerNote);
                writer.Write((short)(Math.Sin(2 * Math.PI * pitch * note * i / rate) * envelope * envelope * 5500));
            }
        return stream.ToArray();
    }
}
