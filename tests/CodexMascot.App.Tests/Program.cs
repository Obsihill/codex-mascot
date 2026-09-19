using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexMascot.App;
using CodexMascot.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;

internal static class Program
{
    private static int _count;
    private static void Check(bool condition, string message)
    { _count++; if (!condition) throw new Exception(message); }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "app-server") { FakeServer(); return; }
        if (args.FirstOrDefault() == "--probe-desktop")
        { Console.WriteLine("Codex desktop window found: " + (CodexDesktopActivator.FindWindow() != IntPtr.Zero)); return; }
        var dir = Path.Combine(Path.GetTempPath(), "mascot-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            PopupFeatureTests.Run(Check);
            using var image = new Image<Bgra32>(16, 16, new Bgra32(255, 0, 0));
            image.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 12;
            using var second = new Image<Bgra32>(16, 16, new Bgra32(0, 255, 0));
            second.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 23;
            image.Frames.AddFrame(second.Frames.RootFrame);
            var gif = Path.Combine(dir, "two-frame.gif");
            image.SaveAsGif(gif);
            var frames = MascotImageLoader.Load(gif, new());
            Check(frames.Count == 2 && frames[0].DelayMs == 120 && frames[1].DelayMs == 230, "GIF frame count/timing");
            var png = Path.Combine(dir, "sprite.png");
            second.SaveAsPng(png);
            Check(MascotImageLoader.Load(png, new() { SpriteColumns = 2, SpriteRows = 2 }).Count == 4, "sprite sheet");
            var webp = Path.Combine(dir, "static.webp");
            second.SaveAsWebp(webp);
            Check(MascotImageLoader.Load(webp, new())[0].Bitmap.PixelWidth == 16, "WebP decoder");
            var soundPath = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "completed.wav");
            Check(File.Exists(soundPath) && new FileInfo(soundPath).Length > 44, "WAV test asset is present");
            if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                using var sound = new SoundPlayerService();
                string? feedback = null;
                var dispatcherFrame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (_, _) => dispatcherFrame.Continue = false;
                sound.Feedback += (_, text) => { feedback = text; dispatcherFrame.Continue = false; };
                timer.Start();
                sound.Play(soundPath, 0);
                if (feedback is null) System.Windows.Threading.Dispatcher.PushFrame(dispatcherFrame);
                timer.Stop();
                Check(feedback?.Contains("재생 시작") == true, "WAV decoder opens successfully at silent test volume: " + feedback);
            }
            var broken = Path.Combine(dir, "broken.png"); File.WriteAllText(broken, "not an image");
            try { MascotImageLoader.Load(broken, new()); throw new Exception("bad image accepted"); }
            catch (UnknownImageFormatException) { _count++; }

            var home = Path.Combine(dir, "codex"); Directory.CreateDirectory(home);
            var hooksPath = Path.Combine(home, "hooks.json");
            File.WriteAllText(hooksPath, """{"description":"keep me","hooks":{"Stop":[{"hooks":[{"type":"command","command":"existing-command"}]}]}}""");
            HookIntegration.Install(home); HookIntegration.Install(home);
            var hooks = JsonNode.Parse(File.ReadAllText(hooksPath))!;
            Check(hooks["description"]!.GetValue<string>() == "keep me", "preserve foreign metadata");
            Check(hooks["hooks"]!["Stop"]!.AsArray().Count == 2, "idempotent hook installation");
            HookIntegration.Uninstall(home);
            Check(JsonNode.Parse(File.ReadAllText(hooksPath))!["hooks"]!["Stop"]!.AsArray().Count == 1, "remove only mascot hooks");
            Check(Directory.EnumerateFiles(home, "*.mascot-backup-*").Any(), "hook backup");

            var events = Path.Combine(dir, "events");
            var bridge = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", bridge, "-EventDirectory", events }) start.ArgumentList.Add(arg);
            using (var process = Process.Start(start)!)
            {
                process.StandardInput.WriteLine("""{"session_id":"fixture","turn_id":"turn","cwd":"C:/fixture","hook_event_name":"PermissionRequest","tool_name":"Bash","prompt":"SHOULD_NOT_PERSIST","tool_input":{"command":"SECRET_COMMAND"}}""");
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Check(process.ExitCode == 0 && output.Trim() == "{}", "hook relay output: " + error);
            }
            var eventFile = Directory.EnumerateFiles(events, "*.json").Single();
            var record = File.ReadAllText(eventFile);
            Check(!record.Contains("SECRET") && !record.Contains("PERSIST"), "hook relay discards private payloads");
            Check(DesktopEventParser.ParseHook(record)?.Kind == CodexEventKind.ApprovalRequired, "relay to normalized event");
            var manager = new CustomizationManager();
            var original = File.ReadAllText(AppPaths.ConfigFile);
            try
            {
                manager.Configuration.For(MascotState.Completed).Image = manager.ImportAsset(png, false);
                manager.Configuration.Global.KeepCompletedVisibleUntilClick = false;
                manager.Configuration.Global.BringCodexToFrontOnClick = false;
                manager.Save(); var reloaded = new CustomizationManager();
                Check(reloaded.ResolveImage(MascotState.Completed) is not null, "asset copy/save/reload");
                Check(!reloaded.Configuration.Global.KeepCompletedVisibleUntilClick && !reloaded.Configuration.Global.BringCodexToFrontOnClick, "popup preferences persist in config file");
                var zip = Path.Combine(dir, "theme.zip"); manager.Export(zip);
                reloaded.ImportTheme(zip);
                Check(reloaded.ResolveImage(MascotState.Completed) is not null, "theme export/import");
                Check(!reloaded.Configuration.Global.KeepCompletedVisibleUntilClick && !reloaded.Configuration.Global.BringCodexToFrontOnClick, "theme retains popup preferences");
                File.WriteAllText(AppPaths.ConfigFile, "{bad");
                var recovered = new CustomizationManager();
                Check(recovered.LoadWarning is not null && File.Exists(AppPaths.ConfigFile), "corrupt JSON recovery");
            }
            finally { File.WriteAllText(AppPaths.ConfigFile, original); }
            TestTransport(dir).GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + _count + " app assertions (popup lifetimes/clicks, desktop activation, GIF/WebP/sprites, themes, hook merge/relay, RPC approvals/EOF).");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static async Task TestTransport(string dir)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "CodexMascot.App.Tests.exe");
        var approval = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<CodexEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var client = new CodexAppServerClient { ExecutablePath = exe })
        {
            client.EventReceived += (_, e) => { if (e.Kind == CodexEventKind.ApprovalRequired) approval.TrySetResult(e.RequestId!); if (e.Kind == CodexEventKind.TurnCompleted) completion.TrySetResult(e); };
            await client.StartTaskAsync(dir, "fixture only", null);
            await client.ApproveAsync(await approval.Task.WaitAsync(TimeSpan.FromSeconds(5)), true);
            Check((await completion.Task.WaitAsync(TimeSpan.FromSeconds(5))).Status == "completed", "numeric RPC approval and completion");
        }
        Environment.SetEnvironmentVariable("MASCOT_FAKE_EOF", "1");
        try
        {
            await using var client = new CodexAppServerClient { ExecutablePath = exe };
            var stopwatch = Stopwatch.StartNew();
            try { await client.StartTaskAsync(dir, "fixture", null); throw new Exception("EOF was not reported"); }
            catch (IOException) { Check(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "EOF faults requests promptly"); }
        }
        finally { Environment.SetEnvironmentVariable("MASCOT_FAKE_EOF", null); }
    }
    private static void Send(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
    private static void FakeServer()
    {
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            using var d = JsonDocument.Parse(line); var root = d.RootElement;
            if (root.TryGetProperty("method", out var method))
            {
                switch (method.GetString())
                {
                    case "initialize": Send(new { id = root.GetProperty("id"), result = new { userAgent = "fake" } }); break;
                    case "thread/start":
                        if (Environment.GetEnvironmentVariable("MASCOT_FAKE_EOF") == "1") return;
                        Send(new { id = root.GetProperty("id"), result = new { thread = new { id = "fake-thread" } } }); break;
                    case "turn/start":
                        Send(new { id = root.GetProperty("id"), result = new { turn = new { id = "fake-turn" } } });
                        Send(new { method = "turn/started", @params = new { threadId = "fake-thread", turn = new { id = "fake-turn" } } });
                        Send(new { id = 91, method = "item/commandExecution/requestApproval", @params = new { threadId = "fake-thread", turnId = "fake-turn" } }); break;
                }
            }
            else if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 91)
                Send(new { method = "turn/completed", @params = new { threadId = "fake-thread", turn = new { id = "fake-turn", status = "completed" } } });
        }
    }
}
