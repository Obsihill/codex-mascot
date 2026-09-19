using System.Text.Json;
using CodexMascot.Core;
namespace CodexMascot.App;
internal static class PackageDiagnostic
{
    public static void Run(string reportPath, string codexHome)
    {
        var manager = new CustomizationManager();
        var images = CustomizationManager.States.Select(state => new
        {
            state = state.ToString(),
            frames = MascotImageLoader.Load(manager.ResolveImage(state) ?? throw new FileNotFoundException(state + " image missing"), manager.Configuration.For(state)).Count
        }).ToArray();
        var aggregator = new StatusAggregator();
        MonitorHealth? health = null;
        var monitor = new DesktopSessionMonitor(codexHome, HookIntegration.EventDirectory);
        monitor.EventReceived += (_, e) => aggregator.Apply(e);
        monitor.HealthChanged += (_, h) => health = h;
        monitor.Poll();
        var result = new
        {
            version = "0.2.1", runtime = Environment.Version.ToString(), images,
            soundFiles = Directory.GetFiles(AppPaths.SoundsDirectory, "*.wav").Length,
            relayIncluded = File.Exists(Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1")),
            health, state = aggregator.State.ToString(), runningJobs = aggregator.Jobs.Count(j => j.State == MascotState.Running)
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
