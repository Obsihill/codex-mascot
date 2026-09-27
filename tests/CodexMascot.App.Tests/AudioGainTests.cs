using System.IO;
using CodexMascot.App;
using NAudio.Wave;

internal static class AudioGainTests
{
    public static void Run(Action<bool, string> check)
    {
        // Offline samples verify amplification without playing loud test audio.
        var gain = new GainSampleProvider(new Samples()) { Gain = 2 };
        var buffer = new float[8]; var read = gain.Read(buffer, 2, 4);
        check(read == 4 && buffer[2] == .5f && buffer[3] == -.5f && buffer[4] == 1 && buffer[5] == -1 && buffer[0] == 0, "200 percent doubles samples and safely clips peaks respecting offset");
        gain.Gain = 0; gain.Read(buffer, 0, 4);
        check(buffer.Take(4).All(v => v == 0), "zero volume produces digital silence");
        var sound = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "completed.wav");
        var normal = Decode(sound, 1, 1); var boosted = Decode(sound, 2, 1);
        check(normal.Count == boosted.Count && boosted.Peak > normal.Peak * 1.8, "real WAV decoding applies 200 percent gain");
        var fast = Decode(sound, 1, 2);
        check(Math.Abs(fast.Count * 2 - normal.Count) < 1024, "audio duration follows playback speed");
        var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(video))
        {
            normal = Decode(video, 1, 1); boosted = Decode(video, 2, 1);
            check(normal.Count > 0 && normal.Count == boosted.Count && boosted.Peak > normal.Peak * 1.8, "MP4 audio is decoded and amplified beyond 100 percent");
        }
    }
    private static (int Count, float Peak) Decode(string path, double volume, double speed)
    {
        using var reader = new AudioFileReader(path);
        var gain = new GainSampleProvider(SoundPlayerService.CreateRateProvider(reader, speed)) { Gain = volume };
        var buffer = new float[4096]; var count = 0; var peak = 0f; int read;
        while ((read = gain.Read(buffer, 0, buffer.Length)) > 0)
        { count += read; for (var i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i])); }
        return (count, peak);
    }
    private sealed class Samples : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public int Read(float[] buffer, int offset, int count)
        { var values = new[] { .25f, -.25f, .75f, -.75f }; var n = Math.Min(count, values.Length); Array.Copy(values, 0, buffer, offset, n); return n; }
    }
}
