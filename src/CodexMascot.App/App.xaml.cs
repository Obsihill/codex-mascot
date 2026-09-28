using System.Windows;
namespace CodexMascot.App;
public partial class App : System.Windows.Application
{
    // The generated entry point constructs App before loading XAML resources.
    // Resolve saved language first, so even shared caption/tooltip styles agree.
    public App() => Loc.ConfigureFromFile(AppPaths.ConfigFile);
    private Mutex? _instance;
    private bool _ownsInstance;
    private InstanceActivation? _activation;
    private AppTheme? _theme;
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
        _instance = new Mutex(false, @"Local\CodexMascot.v02");
        try { _ownsInstance = _instance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstance = true; }
        if (!_ownsInstance)
        {
            if (!InstanceActivation.NotifyAsync().GetAwaiter().GetResult())
                MessageBox.Show(Loc.T("실행 중인 창을 열지 못했습니다. 이전 버전이 실행 중이면 트레이에서 종료한 뒤 다시 실행해 주세요."), AppBrand.Name);
            Shutdown(); return;
        }
        try
        {
            _activation = new InstanceActivation(() =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(new Action(() => (MainWindow as CodexMascot.App.MainWindow)?.ShowMain()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            });
            AppPaths.EnsureFolders(); base.OnStartup(e);
            _theme = new AppTheme();
            MainWindow = new MainWindow(); MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("앱을 시작하지 못했습니다: ") + ex.Message, AppBrand.Name);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _activation?.Dispose();
        _theme?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
