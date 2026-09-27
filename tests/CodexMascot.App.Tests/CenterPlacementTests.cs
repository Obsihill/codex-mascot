using System.Windows;
using CodexMascot.App;
using CodexMascot.Core;

internal static class CenterPlacementTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var context = SynchronizationContext.Current;
        try { RunCore(check, dir); }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
    private static void RunCore(Action<bool, string> check, string dir)
    {
        var area = new System.Drawing.Rectangle(-1920, -200, 1920, 1040);
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
        foreach (var scale in new[] { .4, 1, 1.61, 3 })
        foreach (var anchor in new[] { new Point(-960, 320), new Point(-1910, -190) })
        {
            var config = new GlobalConfiguration { Position = "custom-center", CustomLeft = anchor.X, CustomTop = anchor.Y, Scale = scale };
            var bounds = OverlayWindow.CalculatePlacementBounds(config, area, dpi);
            var actual = new Point(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d + 9 * dpi);
            check((actual - anchor).Length <= .71, "media center remains fixed across size, edge, negative-coordinate and DPI changes");
        }
        var legacy = new GlobalConfiguration { Position = "custom", CustomLeft = -1700, CustomTop = 100, Scale = 1 };
        var oldBounds = OverlayWindow.CalculatePlacementBounds(legacy, area, 1);
        check(oldBounds.Left == -1700 && oldBounds.Top == 100, "old custom positions still mean top-left until edited");
        var offscreen = OverlayWindow.CalculatePlacementBounds(new() { Position = "custom-center", CustomLeft = -10000, CustomTop = -10000 }, area, 1);
        check(offscreen.Right > area.Left && offscreen.Bottom > area.Top, "removed-monitor anchors are moved to a reachable screen edge");

        var store = TestLibrary.Create(System.IO.Path.Combine(dir, "center-placement.json"));
        var mascot = store.Library.Installed.First();
        var manager = new CustomizationManager();
        mascot.Position = "custom"; mascot.CustomLeft = 100; mascot.CustomTop = 100;
        for (var i = 0; i < CustomizationManager.States.Length; i++)
        {
            var settings = mascot.Settings(CustomizationManager.States[i]);
            settings.Position = i % 2 == 0 ? "custom" : "bottom-right";
            settings.CustomLeft = 100 + i * 10; settings.CustomTop = 150;
            settings.Scale = .7 + i * .1;
        }
        store.Save();
        var centers = CustomizationManager.States.ToDictionary(s => s, s => OverlayWindow.PlacementCenter(LibraryStore.Placement(mascot, manager.Configuration.Global, s)));
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, manager);
        var snapshot = store.Snapshot();
        try
        {
            check(dashboard.SaveScale(mascot.Id, null, 1.8), "first size edit converts legacy anchors as one saved change");
            foreach (var state in CustomizationManager.States)
                check(mascot.Settings(state).Position == "custom-center" && (OverlayWindow.PlacementCenter(LibraryStore.Placement(mascot, manager.Configuration.Global, state)) - centers[state]).Length < 1,
                    "whole-scope resizing preserves each legacy state's own visual center");
            var reloaded = new LibraryStore(System.IO.Path.Combine(dir, "center-placement.json")).Library.Find(mascot.Id)!;
            check(CustomizationManager.States.All(s => reloaded.Settings(s).Position == "custom-center" && reloaded.Settings(s).Scale == 1.8), "center anchors and size survive reloading");
            dashboard.ReplayHistory(false);
            check(store.Snapshot() == snapshot, "undo restores both legacy anchor semantics and size together");
        }
        finally { dashboard.Shutdown(); }
    }
}
