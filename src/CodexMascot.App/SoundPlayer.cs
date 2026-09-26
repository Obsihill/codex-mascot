using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CodexMascot.App;

public sealed class SoundPlayerService : IDisposable
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private GainSampleProvider? _gain;
    private string? _path;
    public event EventHandler<string>? Feedback;
    internal bool IsPrepared => _output is not null;

    // Prepare separately so boosted video audio can start when WPF opens the video.
    internal bool Prepare(string? path, double volume, double speed = 1, TimeSpan? position = null)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        { Feedback?.Invoke(this, "사운드가 없거나 파일을 찾을 수 없습니다."); return false; }
        try
        {
            _path = path; _reader = new AudioFileReader(path);
            if (position is { } time) _reader.CurrentTime = time;
            _gain = new GainSampleProvider(CreateRateProvider(_reader, speed)) { Gain = volume };
            _output = new WaveOutEvent { DesiredLatency = 80 };
            var output = _output;
            output.PlaybackStopped += (_, e) =>
            {
                if (ReferenceEquals(_output, output) && e.Exception is not null)
                    Feedback?.Invoke(this, "사운드 재생 실패: " + e.Exception.Message);
            };
            _output.Init(_gain.ToWaveProvider());
            return true;
        }
        catch (Exception e) { Stop(); Feedback?.Invoke(this, "사운드 재생 실패: " + e.Message); return false; }
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
        try { _output.Play(); Feedback?.Invoke(this, "사운드 재생 시작: " + Path.GetFileName(_path)); }
        catch (Exception e) { Stop(); Feedback?.Invoke(this, "사운드 재생 실패: " + e.Message); }
    }
    internal void SetVolume(double volume) { if (_gain is not null) _gain.Gain = volume; }
    public void Play(string? path, double volume, double speed = 1, TimeSpan? position = null)
    { if (Prepare(path, volume, speed, position)) Start(); }
    public void Stop()
    {
        var output = _output; _output = null;
        try { output?.Stop(); }
        finally { output?.Dispose(); _reader?.Dispose(); _reader = null; _gain = null; _path = null; }
    }
    public void Dispose() => Stop();

    private sealed class RateSampleProvider(ISampleProvider source, double speed) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(
            (int)Math.Round(source.WaveFormat.SampleRate * speed), source.WaveFormat.Channels);
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
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
