using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CodexMascot.App;

public sealed class SoundPlayerService : IDisposable
{
    // Notification clips are short, so decoding them fully before playback keeps
    // the audio callback free of file decoding. Decoding inside the callback with
    // a small WaveOut buffer (80 ms) stutters as soon as the machine is busy,
    // which is exactly when mascot alerts arrive.
    internal static long PreloadByteLimit = 8L * 1024 * 1024;
    private AudioFileReader? _reader;
    private ISampleProvider? _stream;
    private GainSampleProvider? _gain;
    private CachedSoundProvider? _cached;
    private WaveOutEvent? _output;
    private string? _path;
    public event EventHandler<string>? Feedback;
    internal bool IsPrepared => _output is not null;
    internal bool IsPreloaded => _cached is not null;

    // Prepare separately so boosted video audio can start when WPF opens the video.
    internal bool Prepare(string? path, double volume, double speed = 1, TimeSpan? position = null)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        { Feedback?.Invoke(this, Loc.T("사운드가 없거나 파일을 찾을 수 없습니다.")); return false; }
        try
        {
            _path = path;
            if (TryDecode(path, volume, speed, position, out var cached)) _cached = cached;
            else
            {
                _reader = new AudioFileReader(path);
                if (position is { } time) _reader.CurrentTime = time;
                _stream = CreateRateProvider(_reader, speed);
                _gain = new GainSampleProvider(_stream) { Gain = volume };
            }
            // Three buffers of roughly 80 ms each ride out scheduler hiccups that
            // a two-buffer, 80 ms total queue could not.
            _output = new WaveOutEvent { DesiredLatency = 250, NumberOfBuffers = 3 };
            var output = _output;
            output.PlaybackStopped += (_, e) =>
            {
                if (ReferenceEquals(_output, output) && e.Exception is not null)
                    Feedback?.Invoke(this, Loc.T("사운드 재생 실패: ") + e.Exception.Message);
            };
            _output.Init(_cached is not null ? _cached.ToWaveProvider() : _gain!.ToWaveProvider());
            return true;
        }
        catch (Exception e) { Stop(); Feedback?.Invoke(this, Loc.T("사운드 재생 실패: ") + e.Message); return false; }
    }
    // Returns false when the clip is too long to keep in memory. In that case the
    // caller falls back to streaming, which still benefits from the larger queue.
    internal static bool TryDecode(string path, double volume, double speed, TimeSpan? position, out CachedSoundProvider? cached)
    {
        cached = null;
        using var reader = new AudioFileReader(path);
        var chain = CreateRateProvider(reader, speed);
        var format = chain.WaveFormat;
        var skip = position is { } time && time > TimeSpan.Zero
            ? (long)Math.Round(time.TotalSeconds * format.SampleRate) * format.Channels : 0;
        if ((reader.TotalTime.TotalSeconds * format.SampleRate * format.Channels + skip) * sizeof(float) > PreloadByteLimit)
            return false;
        var chunks = new List<float[]>();
        var buffer = new float[Math.Max(1024, format.SampleRate * format.Channels / 5)];
        long total = 0;
        while (true)
        {
            var read = chain.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            var start = 0;
            if (skip > 0) { var drop = (int)Math.Min(skip, read); skip -= drop; start = drop; if (start == read) continue; }
            total += read - start;
            if (total * sizeof(float) > PreloadByteLimit) return false;
            var copy = new float[read - start];
            Array.Copy(buffer, start, copy, 0, copy.Length);
            chunks.Add(copy);
        }
        var samples = new float[total];
        var offset = 0;
        foreach (var chunk in chunks) { Array.Copy(chunk, 0, samples, offset, chunk.Length); offset += chunk.Length; }
        cached = new CachedSoundProvider(samples, format, volume);
        return true;
    }
    internal static ISampleProvider CreateRateProvider(ISampleProvider source, double speed)
    {
        speed = double.IsFinite(speed) ? Math.Clamp(speed, .25, 3) : 1;
        return Math.Abs(speed - 1) < .001 ? source :
            new WdlResamplingSampleProvider(new RateSampleProvider(source, speed), source.WaveFormat.SampleRate);
    }
    internal void Start()
    {
        if (_output is null) return;
        try { _output.Play(); Feedback?.Invoke(this, Loc.T("사운드 재생 시작: ") + Path.GetFileName(_path)); }
        catch (Exception e) { Stop(); Feedback?.Invoke(this, Loc.T("사운드 재생 실패: ") + e.Message); }
    }
    internal void SetVolume(double volume)
    {
        if (_cached is not null) _cached.Gain = volume;
        else if (_gain is not null) _gain.Gain = volume;
    }
    public void Play(string? path, double volume, double speed = 1, TimeSpan? position = null)
    { if (Prepare(path, volume, speed, position)) Start(); }
    public void Stop()
    {
        var output = _output; _output = null;
        try { output?.Stop(); }
        finally
        {
            output?.Dispose(); _reader?.Dispose(); _reader = null;
            _stream = null; _gain = null; _cached = null; _path = null;
        }
    }
    public void Dispose() => Stop();

    private sealed class RateSampleProvider(ISampleProvider source, double speed) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(
            (int)Math.Round(source.WaveFormat.SampleRate * speed), source.WaveFormat.Channels);
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    }
}

// Plays decoded samples straight from memory: the audio callback only copies
// floats and applies gain, so the queue cannot run dry while a file is decoded.
internal sealed class CachedSoundProvider : ISampleProvider
{
    private readonly float[] _samples;
    private volatile float _gain = 1;
    private int _position;
    public CachedSoundProvider(float[] samples, WaveFormat format, double gain)
    { _samples = samples; WaveFormat = format; Gain = gain; }
    public WaveFormat WaveFormat { get; }
    public double Gain { get => _gain; set => _gain = (float)(double.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1); }
    public int Read(float[] buffer, int offset, int count)
    {
        var available = Math.Min(count, _samples.Length - _position);
        if (available <= 0) return 0;
        var gain = _gain;
        for (var i = 0; i < available; i++) buffer[offset + i] = Math.Clamp(_samples[_position + i] * gain, -1f, 1f);
        _position += available;
        return available;
    }
}

// Software gain, not the OS session-volume control (which is capped at 100%).
internal sealed class GainSampleProvider(ISampleProvider source) : ISampleProvider
{
    private volatile float _gain = 1;
    public double Gain { get => _gain; set => _gain = (float)(double.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1); }
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count); var gain = _gain;
        for (var i = offset; i < offset + read; i++) buffer[i] = Math.Clamp(buffer[i] * gain, -1f, 1f);
        return read;
    }
}
