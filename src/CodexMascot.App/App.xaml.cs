using System.Windows;
namespace CodexMascot.App;
public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--diagnose")
        {
            try
            {
                if (e.Args.Length < 2) throw new ArgumentException("--diagnose requires an output JSON path.");
                PackageDiagnostic.Run(e.Args[1], e.Args.Length > 2 ? e.Args[2] : HookIntegration.DefaultCodexHome);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                if (e.Args.Length > 1) File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
                Shutdown(1);
            }
            return;
        }
        _instance = new Mutex(true, @"Local\CodexMascot.v02", out _ownsInstance);
        if (!_ownsInstance)
        {
            MessageBox.Show("Codex Mascot가 이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘에서 열어 주세요.", "Codex Mascot");
            Shutdown(); return;
        }
        try { AppPaths.EnsureFolders(); base.OnStartup(e); }
        catch (Exception ex)
        {
            MessageBox.Show("앱을 시작하지 못했습니다: " + ex.Message, "Codex Mascot");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
