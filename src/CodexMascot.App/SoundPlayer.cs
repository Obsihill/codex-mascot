using System.Windows.Media;
namespace CodexMascot.App;
public sealed class SoundPlayerService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private string? _path;
    public event EventHandler<string>? Feedback;
    public SoundPlayerService()
    {
        _player.MediaOpened += (_, _) => { _player.Play(); Feedback?.Invoke(this, "사운드 재생 시작: " + Path.GetFileName(_path)); };
        _player.MediaFailed += (_, e) => Feedback?.Invoke(this, "사운드 재생 실패: " + e.ErrorException.Message);
    }
    public void Play(string? path, double volume)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Feedback?.Invoke(this, "사운드가 없거나 파일을 찾을 수 없습니다."); return; }
        try { _path = path; _player.Volume = Math.Clamp(volume, 0, 1); _player.Open(new Uri(path, UriKind.Absolute)); }
        catch (Exception e) { Feedback?.Invoke(this, "사운드 재생 실패: " + e.Message); }
    }
    public void Stop() { _player.Stop(); _player.Close(); }
    public void Dispose() => Stop();
}
