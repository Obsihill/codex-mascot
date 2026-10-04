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
        // Alerts must not decode inside the audio callback. Preloading decodes the
        // whole clip up front and keeps the sample count identical to streaming.
        check(SoundPlayerService.TryDecode(sound, 1, 1, null, out var cached) && cached is not null, "short clip preloads into memory");
        var preloaded = ReadAll(cached!, 1);
        check(preloaded.Count == normal.Count && Math.Abs(preloaded.Peak - normal.Peak) < .001, "preloaded audio matches streaming samples");
        check(SoundPlayerService.TryDecode(sound, 2, 1, null, out var boostedCache) && boostedCache is not null, "boosted clip preloads into memory");
        var boostedPreload = ReadAll(boostedCache!, 2);
        check(boostedPreload.Peak > preloaded.Peak * 1.8, "preloaded samples honour software gain");
        check(SoundPlayerService.TryDecode(sound, 1, 2, null, out var fastCache) && fastCache is not null, "speed-adjusted clip preloads into memory");
        check(Math.Abs(ReadAll(fastCache!, 1).Count * 2 - normal.Count) < 1024, "preloaded audio follows playback speed");
        var video = Environment.GetEnvironmentVariable("MASCOT_VIDEO_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(video))
        {
            normal = Decode(video, 1, 1); boosted = Decode(video, 2, 1);
            check(normal.Count > 0 && normal.Count == boosted.Count && boosted.Peak > normal.Peak * 1.8, "MP4 audio is decoded and amplified beyond 100 percent");
        }
        var compressed = Environment.GetEnvironmentVariable("MASCOT_MP3_TEST_FILE");
        if (!string.IsNullOrWhiteSpace(compressed))
        {
            normal = Decode(compressed, 1, 1);
            check(normal.Count > 0, "MP3 fixture decodes through the streaming fallback");
            check(SoundPlayerService.TryDecode(compressed, 1, 1, null, out var mp3) && mp3 is not null, "MP3 clip preloads into memory");
            var preloadedMp3 = ReadAll(mp3!, 1);
            check(preloadedMp3.Count == normal.Count && Math.Abs(preloadedMp3.Peak - normal.Peak) < .001, "preloaded MP3 matches streaming samples");
            check(SoundPlayerService.TryDecode(compressed, 1, 1, TimeSpan.FromSeconds(1), out var seeked) && seeked is not null, "seeked MP3 clip preloads into memory");
            var seekedMp3 = ReadAll(seeked!, 1);
            var oneSecond = mp3!.WaveFormat.SampleRate * mp3.WaveFormat.Channels;
            check(seekedMp3.Count < preloadedMp3.Count && Math.Abs(seekedMp3.Count - (preloadedMp3.Count - oneSecond)) < 4096, "seek offset trims the requested head of the clip");
        }
    }
    private static (int Count, float Peak) ReadAll(ISampleProvider provider, double volume)
    {
        var buffer = new float[4096]; var count = 0; var peak = 0f; int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        { count += read; for (var i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i])); }
        return (count, peak);
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
