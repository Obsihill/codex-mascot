using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using CodexMascot.App;
using CodexMascot.Core;

internal static class ExternalAgentTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "integration", "Send-MascotEvent.ps1");
        foreach (var (kind, start, complete, attention) in new[]
        {
            (AgentKind.Gemini, "BeforeAgent", "AfterAgent", "Notification"),
            (AgentKind.Qwen, "UserPromptSubmit", "Stop", "PermissionRequest"),
            (AgentKind.Copilot, "userPromptSubmitted", "agentStop", "notification")
        })
        {
            var home = Path.Combine(root, kind.ToString());
            Directory.CreateDirectory(home);
            var config = HookIntegration.Install(home, kind);
            check(File.Exists(config) && HookIntegration.IsInstalled(home, kind), kind + " hook installed");
            HookIntegration.EnsureInstalled(home, kind);
            var installed = JsonNode.Parse(File.ReadAllText(config))!;
            check(installed["hooks"]![complete] is not null, kind + " completion hook configured");
            if (kind == AgentKind.Copilot)
            {
                check(installed["version"]!.GetValue<int>() == 1, "Copilot native hook schema");
                installed["hooks"]!["agentStop"]!.AsArray().Add(new JsonObject
                { ["type"] = "command", ["exec"] = "foreign-tool", ["args"] = new JsonArray("--foreign") });
                File.WriteAllText(config, installed.ToJsonString());
                HookIntegration.EnsureInstalled(home, kind);
            }

            var events = Path.Combine(root, kind + "-events");
            var monitor = new DesktopSessionMonitor(home, events, AgentAdapters.For(kind));
            var received = new List<CodexEvent>();
            monitor.EventReceived += (_, e) => received.Add(e);
            foreach (var (name, expected, type) in new[]
            {
                (start, CodexEventKind.TurnStarted, ""),
                (attention, CodexEventKind.ApprovalRequired, kind == AgentKind.Gemini ? "ToolPermission" : "permission_prompt"),
                (complete, CodexEventKind.TurnCompleted, "")
            })
            {
                var processInfo = new ProcessStartInfo("powershell.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-EventDirectory", events, "-HookEventName", name })
                    processInfo.ArgumentList.Add(arg);
                using var process = Process.Start(processInfo)!;
                process.StandardInput.Write("{\"session_id\":\"test-" + kind + "\",\"sessionId\":\"test-" + kind +
                    "\",\"cwd\":\"C:/fixture\",\"notification_type\":\"" + type +
                    "\",\"prompt\":\"PRIVATE_PROMPT\",\"tool_input\":\"PRIVATE_TOOL\"}");
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                check(process.ExitCode == 0 && output.Trim() == "{}", kind + " relay runs: " + error);
                var record = Directory.EnumerateFiles(events, "*.json").OrderByDescending(f => f).First();
                var raw = File.ReadAllText(record);
                check(!raw.Contains("PRIVATE_"), kind + " relay keeps prompts private");
                check(ExternalHookParser.Parse(kind, raw)?.Kind == expected, kind + " " + name + " is " + expected);
                monitor.Poll();
            }
            check(received.Count == 3 && received[0].Kind == CodexEventKind.TurnStarted &&
                received[1].Kind == CodexEventKind.ApprovalRequired && received[2].Kind == CodexEventKind.TurnCompleted,
                kind + " monitor consumes hook-only lifecycle");
            monitor.Poll();
            check(received.Count == 3, kind + " monitor does not replay hook files");
            HookIntegration.Uninstall(home, kind);
            check(!HookIntegration.IsInstalled(home, kind), kind + " hook removed");
            if (kind == AgentKind.Copilot)
                check(JsonNode.Parse(File.ReadAllText(config))!["hooks"]!["agentStop"]!.AsArray().Count == 1,
                    "Copilot uninstall preserves foreign hook");
        }

        RunNewAgentTests(check, root, script);
        RunKiroTests(check, root, script);
        RunAmpTests(check, root);

        var openHome = Path.Combine(root, "OpenCode");
        Directory.CreateDirectory(openHome);
        var plugin = HookIntegration.Install(openHome, AgentKind.OpenCode);
        check(File.Exists(plugin) && HookIntegration.IsInstalled(openHome, AgentKind.OpenCode), "OpenCode plugin installed");
        var openEvents = Path.Combine(root, "opencode-events");
        var openMonitor = new DesktopSessionMonitor(openHome, openEvents, AgentAdapters.OpenCode);
        var openReceived = new List<CodexEvent>();
        openMonitor.EventReceived += (_, e) => openReceived.Add(e);
        // Load the exact installed plugin in a non-interactive Node process and deliver mock events.
        var nodeCode = "import {readFile} from 'node:fs/promises';" +
            "const source=await readFile(process.argv[1],'utf8');" +
            "const mod=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));" +
            "const bridge=await mod.AgentMascotPlugin({directory:'C:/fixture'});" +
            "for(const type of ['session.status','permission.asked','permission.replied','session.idle'])" +
            "await bridge.event({event:{type,properties:{sessionID:'open-test',status:{type:'busy'},requestID:'req-1',prompt:'PRIVATE_PROMPT'}}});";
        var node = new ProcessStartInfo("node")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        node.Environment["AGENT_MASCOT_EVENTS_OPENCODE"] = openEvents;
        foreach (var arg in new[] { "--input-type=module", "--eval", nodeCode, plugin }) node.ArgumentList.Add(arg);
        using (var process = Process.Start(node)!)
        {
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            check(process.ExitCode == 0, "OpenCode plugin executes: " + error);
        }
        var delivered = Directory.EnumerateFiles(openEvents, "*.json")
            .Where(f => File.ReadAllText(f).Contains("open-test")).ToArray();
        check(delivered.Length == 4, "OpenCode plugin emits four lifecycle events");
        var parsed = delivered.Select(f => (Raw: File.ReadAllText(f), Event: ExternalHookParser.Parse(AgentKind.OpenCode, File.ReadAllText(f)))).ToArray();
        check(parsed.All(p => !p.Raw.Contains("PRIVATE_PROMPT")), "OpenCode plugin does not persist prompts");
        check(parsed.Where(p => p.Event is not null).Select(p => p.Event!.Kind).ToHashSet().IsSupersetOf(new[]
            { CodexEventKind.TurnStarted, CodexEventKind.ApprovalRequired, CodexEventKind.ServerRequestResolved, CodexEventKind.TurnCompleted }),
            "OpenCode lifecycle maps to mascot states");
        openMonitor.Poll();
        check(openReceived.Count == 4, "OpenCode monitor consumes plugin events");
        foreach (var file in delivered) File.Delete(file);
        HookIntegration.Uninstall(openHome, AgentKind.OpenCode);
        check(!File.Exists(plugin), "OpenCode plugin removed");

        var cursorHome = Path.Combine(root, "Cursor");
        Directory.CreateDirectory(cursorHome);
        var cursorConfig = Path.Combine(cursorHome, "hooks.json");
        File.WriteAllText(cursorConfig, """{"version":1,"hooks":{"stop":[{"command":"foreign-command"}]}}""");
        HookIntegration.Install(cursorHome, AgentKind.Cursor);
        check(HookIntegration.IsInstalled(cursorHome, AgentKind.Cursor), "Cursor hook installed");
        check(JsonNode.Parse(File.ReadAllText(cursorConfig))!["hooks"]!["stop"]!.AsArray().Count == 2,
            "Cursor existing hook preserved");
        var cursorEvents = Path.Combine(root, "cursor-events");
        var cursorMonitor = new DesktopSessionMonitor(cursorHome, cursorEvents, AgentAdapters.Cursor);
        var cursorReceived = new List<CodexEvent>();
        cursorMonitor.EventReceived += (_, e) => cursorReceived.Add(e);
        foreach (var name in new[] { "beforeSubmitPrompt", "stop" })
        {
            var info = new ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-EventDirectory", cursorEvents, "-HookEventName", name }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            process.StandardInput.Write("""{"conversation_id":"cursor-test","generation_id":"turn-1","workspace_roots":["C:/fixture"],"prompt":"PRIVATE_PROMPT"}""");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            check(process.ExitCode == 0 && output.Trim() == (name == "beforeSubmitPrompt" ? "{\"continue\":true}" : "{}"),
                "Cursor relay runs without blocking prompt: " + error);
            cursorMonitor.Poll();
        }
        check(cursorReceived.Count == 2 && cursorReceived[0].Kind == CodexEventKind.TurnStarted &&
            cursorReceived[1].Kind == CodexEventKind.TurnCompleted && cursorReceived[0].ProjectPath == "C:/fixture",
            "Cursor conversation and workspace parsed");
        check(Directory.EnumerateFiles(cursorEvents, "*.json").All(f => !File.ReadAllText(f).Contains("PRIVATE_PROMPT")),
            "Cursor relay discards prompts");
        check(CodexDesktopActivator.IsCursorDesktopExecutable(@"D:\CodexMascotTools\Cursor\Cursor.exe") &&
            !CodexDesktopActivator.IsCursorDesktopExecutable(@"D:\tools\node_modules\Cursor.exe"),
            "Cursor desktop selection excludes package shims");
        HookIntegration.Uninstall(cursorHome, AgentKind.Cursor);
        check(JsonNode.Parse(File.ReadAllText(cursorConfig))!["hooks"]!["stop"]!.AsArray().Count == 1,
            "Cursor uninstall retains foreign hook");
    }

    private static void RunNewAgentTests(Action<bool, string> check, string root, string script)
    {
        foreach (var kind in new[] { AgentKind.Antigravity, AgentKind.Kimi, AgentKind.Windsurf })
        {
            var home = Path.Combine(root, kind.ToString());
            Directory.CreateDirectory(home);
            var config = Path.Combine(home, AgentAdapters.For(kind).HookConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            if (kind == AgentKind.Antigravity)
                File.WriteAllText(config, """{"foreign-hook":{"Stop":[{"command":"foreign-command"}]}}""");
            else if (kind == AgentKind.Kimi)
                File.WriteAllText(config, "[[hooks]]\nevent = \"SessionStart\"\ncommand = \"foreign-command\"\n");
            else
                File.WriteAllText(config, """{"hooks":{"pre_user_prompt":[{"powershell":"foreign-command"}]}}""");

            HookIntegration.Install(home, kind);
            check(HookIntegration.IsInstalled(home, kind), kind + " hook installed");
            var firstInstall = File.ReadAllText(config);
            HookIntegration.EnsureInstalled(home, kind);
            check(File.ReadAllText(config) == firstInstall, kind + " healthy hook not rewritten");
            if (kind == AgentKind.Antigravity)
            {
                var json = JsonNode.Parse(firstInstall)!;
                check(json["foreign-hook"] is not null &&
                    json["agent-mascot"]?["PreInvocation"] is JsonArray && json["agent-mascot"]?["Stop"] is JsonArray,
                    "Antigravity native schema preserves foreign hook");
            }
            else if (kind == AgentKind.Kimi)
                check(firstInstall.Contains("foreign-command") && firstInstall.Contains("event = \"TurnStarted\"") &&
                    firstInstall.Contains("event = \"PermissionRequest\""), "Kimi native TOML retains foreign hook");
            else
            {
                var json = JsonNode.Parse(firstInstall)!;
                check(json["hooks"]?["pre_user_prompt"]?.AsArray().Count == 2 &&
                    json["hooks"]?["post_cascade_response"] is JsonArray,
                    "Windsurf native schema retains foreign hook");
            }

            var events = Path.Combine(root, kind + "-events");
            var monitor = new DesktopSessionMonitor(home, events, AgentAdapters.For(kind));
            var received = new List<CodexEvent>();
            monitor.EventReceived += (_, e) => received.Add(e);
            var start = kind switch { AgentKind.Antigravity => "PreInvocation", AgentKind.Kimi => "TurnStarted", _ => "pre_user_prompt" };
            var stop = kind switch { AgentKind.Antigravity => "Stop", AgentKind.Kimi => "Stop", _ => "post_cascade_response" };
            var payload = kind switch
            {
                AgentKind.Antigravity => """{"conversationId":"new-test","workspacePaths":["C:/fixture"],"invocationNum":0,"fullyIdle":true,"executionNum":1,"prompt":"PRIVATE_PROMPT"}""",
                AgentKind.Kimi => """{"session_id":"new-test","cwd":"C:/fixture","turn_id":"turn-1","prompt":"PRIVATE_PROMPT"}""",
                _ => """{"trajectory_id":"new-test","execution_id":"turn-1","agent_action_name":"pre_user_prompt","tool_info":{"user_prompt":"PRIVATE_PROMPT"}}"""
            };
            check(RunRelay(script, events, start, payload, root) == "{}", kind + " start relay");
            monitor.Poll();
            check(received.Count == 1 && received[0].Kind == CodexEventKind.TurnStarted &&
                received[0].ProjectPath == (kind == AgentKind.Windsurf ? root : "C:/fixture"),
                kind + " start delivered to monitor");
            if (kind == AgentKind.Kimi)
            {
                check(RunRelay(script, events, "PermissionRequest", payload, root) == "{}", "Kimi permission relay");
                monitor.Poll();
                check(received[^1].Kind == CodexEventKind.ApprovalRequired, "Kimi permission delivered");
                check(RunRelay(script, events, "PermissionResult", payload, root) == "{}", "Kimi permission result relay");
                monitor.Poll();
                check(received[^1].Kind == CodexEventKind.ServerRequestResolved, "Kimi permission resolved");
            }
            var expectedOutput = kind == AgentKind.Antigravity ? "{\"decision\":\"allow\"}" : "{}";
            check(RunRelay(script, events, stop, payload, root, kind == AgentKind.Antigravity) == expectedOutput,
                kind + " stop relay contract");
            monitor.Poll();
            check(received[^1].Kind == CodexEventKind.TurnCompleted, kind + " completion delivered");
            check(Directory.EnumerateFiles(events, "*.json").All(f => !File.ReadAllText(f).Contains("PRIVATE_PROMPT")),
                kind + " prompt not persisted");
            if (kind == AgentKind.Antigravity)
            {
                check(received[^1].TurnId is null, "Antigravity execution attempt is not treated as a turn ID");
                var count = Directory.EnumerateFiles(events, "*.json").Count();
                RunRelay(script, events, "PreInvocation", payload.Replace("\"invocationNum\":0", "\"invocationNum\":1"), root);
                RunRelay(script, events, "Stop", payload.Replace("\"fullyIdle\":true", "\"fullyIdle\":false"), root, true);
                check(Directory.EnumerateFiles(events, "*.json").Count() == count,
                    "Antigravity ignores intermediate invocations and non-idle stops");
                RunRelay(script, events, "PreInvocation", payload, root);
                RunRelay(script, events, "Stop", payload, root, true);
                monitor.Poll();
                var aggregate = new StatusAggregator();
                var completed = received.Select(aggregate.Apply).Count(result => result.ShouldNotify);
                check(completed == 2, "Antigravity consecutive turns both notify");
            }
            HookIntegration.Uninstall(home, kind);
            check(!HookIntegration.IsInstalled(home, kind) && File.ReadAllText(config).Contains("foreign-command"),
                kind + " uninstall preserves foreign hook");
        }
    }

    private static string RunRelay(string script, string events, string eventName, string payload, string workingDirectory,
        bool antigravityStop = false, bool quietOutput = false)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-EventDirectory", events, "-HookEventName", eventName }) info.ArgumentList.Add(arg);
        if (antigravityStop) info.ArgumentList.Add("-AntigravityStop");
        if (quietOutput) info.ArgumentList.Add("-QuietOutput");
        using var process = Process.Start(info)!;
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || error.Length > 0) throw new Exception(eventName + " relay failed: " + error);
        return output;
    }

    private static void RunKiroTests(Action<bool, string> check, string root, string script)
    {
        var home = Path.Combine(root, "Kiro");
        Directory.CreateDirectory(home);
        var config = HookIntegration.Install(home, AgentKind.Kiro);
        check(HookIntegration.IsInstalled(home, AgentKind.Kiro), "Kiro CLI V3 hook installed");
        var json = JsonNode.Parse(File.ReadAllText(config))!;
        check(json["version"]?.GetValue<string>() == "v1" && json["hooks"]?.AsArray().Count == 3 &&
            json["hooks"]!.AsArray().OfType<JsonObject>().All(h => h["action"]?["command"]?.GetValue<string>().Contains("-QuietOutput") == true),
            "Kiro native schema and silent relay");
        json["hooks"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "Foreign", ["trigger"] = "Stop",
            ["action"] = new JsonObject { ["type"] = "command", ["command"] = "foreign-command" }
        });
        File.WriteAllText(config, json.ToJsonString());
        var withForeign = File.ReadAllText(config);
        HookIntegration.EnsureInstalled(home, AgentKind.Kiro);
        check(File.ReadAllText(config) == withForeign, "Kiro healthy hook is not rewritten");

        var events = Path.Combine(root, "kiro-events");
        var monitor = new DesktopSessionMonitor(home, events, AgentAdapters.Kiro);
        var received = new List<CodexEvent>();
        monitor.EventReceived += (_, e) => received.Add(e);
        var payload = """{"session_id":"kiro-test","cwd":"C:/fixture","prompt":"PRIVATE_PROMPT"}""";
        foreach (var name in new[] { "UserPromptSubmit", "Stop", "SessionEnd" })
        {
            check(RunRelay(script, events, name, payload, root, quietOutput: true) == "", "Kiro " + name + " is silent");
            monitor.Poll();
        }
        check(received.Select(e => e.Kind).SequenceEqual(new[]
            { CodexEventKind.TurnStarted, CodexEventKind.TurnCompleted, CodexEventKind.ThreadClosed }) &&
            received.All(e => e.ProjectPath == "C:/fixture"), "Kiro lifecycle delivered");
        check(Directory.EnumerateFiles(events, "*.json").All(f => !File.ReadAllText(f).Contains("PRIVATE_PROMPT")),
            "Kiro prompt not persisted");
        HookIntegration.Uninstall(home, AgentKind.Kiro);
        check(!HookIntegration.IsInstalled(home, AgentKind.Kiro) &&
            JsonNode.Parse(File.ReadAllText(config))!["hooks"]!.AsArray().Count == 1,
            "Kiro uninstall preserves foreign hook");

        var foreignHome = Path.Combine(root, "KiroForeign");
        var foreignConfig = Path.Combine(foreignHome, "hooks", "agent-mascot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(foreignConfig)!);
        File.WriteAllText(foreignConfig, """{"version":"v1","hooks":[{"name":"Foreign","trigger":"Stop","action":{"type":"command","command":"foreign-command"}}]}""");
        var before = File.ReadAllText(foreignConfig);
        try
        {
            HookIntegration.Install(foreignHome, AgentKind.Kiro);
            check(false, "Kiro foreign hook file must not be overwritten");
        }
        catch (InvalidDataException)
        {
            check(File.ReadAllText(foreignConfig) == before, "Kiro foreign hook file remains untouched");
        }
    }

    private static void RunAmpTests(Action<bool, string> check, string root)
    {
        var home = Path.Combine(root, "Amp");
        Directory.CreateDirectory(home);
        var plugin = HookIntegration.Install(home, AgentKind.Amp);
        check(HookIntegration.IsInstalled(home, AgentKind.Amp), "Amp local plugin installed");
        var first = File.ReadAllText(plugin);
        HookIntegration.EnsureInstalled(home, AgentKind.Amp);
        check(File.ReadAllText(plugin) == first, "Amp healthy plugin not rewritten");

        var events = Path.Combine(root, "amp-events");
        var monitor = new DesktopSessionMonitor(home, events, AgentAdapters.Amp);
        var received = new List<CodexEvent>();
        monitor.EventReceived += (_, e) => received.Add(e);
        var nodeCode = "import {readFile} from 'node:fs/promises';" +
            "const source=await readFile(process.argv[1],'utf8');" +
            "const mod=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));" +
            "const handlers={};const amp={on:(name,handler)=>{handlers[name]=handler},helpers:{filePathFromURI:()=> 'C:/fixture'}};" +
            "mod.default(amp);const ctx={system:{workspaceRoot:{toString:()=> 'file:///C:/fixture'}}};" +
            "for(const [id,status] of [['turn-1','done'],['turn-2','error'],['turn-3','cancelled']]){" +
            "const event={thread:{id:'amp-test'},id,status,message:'PRIVATE_PROMPT',messages:['PRIVATE_MESSAGE']};" +
            "await handlers['agent.start'](event,ctx);await handlers['agent.end'](event,ctx)}";
        var node = new ProcessStartInfo("node")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        node.Environment["AGENT_MASCOT_EVENTS_AMP"] = events;
        foreach (var arg in new[] { "--input-type=module", "--eval", nodeCode, plugin }) node.ArgumentList.Add(arg);
        using (var process = Process.Start(node)!)
        {
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            check(process.ExitCode == 0, "Amp plugin executes: " + error);
        }
        var files = Directory.EnumerateFiles(events, "*.json").OrderBy(f => f).ToArray();
        check(files.Length == 6 && files.All(f =>
            !File.ReadAllText(f).Contains("PRIVATE_PROMPT") && !File.ReadAllText(f).Contains("PRIVATE_MESSAGE")),
            "Amp plugin emits private lifecycle metadata only");
        monitor.Poll();
        check(received.Count == 6 && received[0].Kind == CodexEventKind.TurnStarted &&
            received[1].Kind == CodexEventKind.TurnCompleted && received[1].Status == "completed" &&
            received[2].Kind == CodexEventKind.TurnStarted && received[3].Status == "failed" &&
            received[4].Kind == CodexEventKind.TurnStarted && received[5].Status == "interrupted" &&
            received.All(e => e.ProjectPath == "C:/fixture"), "Amp lifecycle and failures delivered");
        HookIntegration.Uninstall(home, AgentKind.Amp);
        check(!File.Exists(plugin), "Amp plugin removed");

        var foreignHome = Path.Combine(root, "AmpForeign");
        var foreignPlugin = Path.Combine(foreignHome, "plugins", "agent-mascot.js");
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPlugin)!);
        File.WriteAllText(foreignPlugin, "export default function foreign() {}");
        try
        {
            HookIntegration.Install(foreignHome, AgentKind.Amp);
            check(false, "Amp foreign plugin must not be overwritten");
        }
        catch (InvalidDataException)
        {
            check(File.ReadAllText(foreignPlugin).Contains("foreign()"), "Amp foreign plugin remains untouched");
        }
    }
}
