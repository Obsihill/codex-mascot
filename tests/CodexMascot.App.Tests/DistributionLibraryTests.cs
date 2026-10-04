using System.IO;
using CodexMascot.App;
using CodexMascot.Core;

internal static class DistributionLibraryTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        var file = Path.Combine(dir, "distribution-defaults.json");
        var store = new LibraryStore(file);
        var manager = new CustomizationManager();
        check(store.Warning is null && store.Library.Installed.Select(m => m.Id).SequenceEqual(new[] { "original", "mascat" }), "release defaults contain only original images and MasCat");
        check(store.Library.Installed.Single(m => m.Id == "original").Name == "Base mascot", "fresh library names the original artwork Base mascot");
        check(Directory.GetDirectories(store.LibraryDirectory).Length == 2 && store.Library.Selected.Single().SourceId == "mascat", "clean release creates exactly two packages with MasCat active");
        var folders = new MascotFolderLibrary(store.LibraryDirectory);
        check(store.Library.Installed.Concat(store.Library.Selected).All(m => CustomizationManager.States.All(s =>
            m.Settings(s).HoldUntilClick == (s is MascotState.Completed or MascotState.NeedsAttention or MascotState.Failed))), "default mascots hold only completed, failed and attention states");
        using (var group = new MascotPresentationGroup())
        {
            group.Show(store, manager, MascotState.Failed, false);
            check(group.HasHeldNotifications && group.Windows.Single().HoldUntilClick, "failure uses per-mascot hold at runtime");
            group.DismissUnheld();
            check(group.Windows.Count == 1, "foreground dismissal preserves held mascot");
            store.Library.Selected.Single().Settings(MascotState.Failed).HoldUntilClick = false;
            group.ApplyPreferences(store, manager);
            check(!group.HasHeldNotifications, "live checkbox change updates hold policy");
            group.DismissUnheld();
            check(group.Windows.Count == 0, "unchecked mascot can dismiss independently");
            store.Library.Selected.Single().Settings(MascotState.Failed).HoldUntilClick = true;
        }
        check(store.Library.Installed.Concat(store.Library.Selected).Where(m => m.Id == "mascat" || m.SourceId == "mascat")
            .All(m => CustomizationManager.States.All(s => m.Settings(s).ImageDurationMs == 2000)), "all MasCat images including the initially selected copy use two seconds");
        foreach (var mascot in store.Library.Installed)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "assets", mascot.Id == "mascat" ? "MasCat" : "images");
            check(File.ReadAllBytes(manager.ResolveAsset(mascot.CoverImage)!).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "main.png"))), "package cover matches supplied main image");
            foreach (var state in CustomizationManager.States)
            {
                var filename = (state == MascotState.NeedsAttention ? "attention" : MascotConfiguration.StateKey(state)) + ".png";
                check(File.ReadAllBytes(manager.ResolveAsset(mascot.For(state).Image)!).SequenceEqual(File.ReadAllBytes(Path.Combine(source, filename))), "release state image matches supplied asset: " + mascot.Name + "/" + filename);
            }
            check(MascotFolderImport.Read(folders.PackageDirectory(mascot.Id)).States.Count == 6, "release library folder can be imported independently");
            check(CustomizationManager.States.All(s => mascot.For(s).SoundCandidates().Count ==
                (mascot.Id == "mascat" && s is MascotState.Completed or MascotState.Failed or MascotState.NeedsAttention ? 7 : 0)), "only MasCat completion, failure and attention include random sound pools");
        }
        check(new LibraryStore(file).Library.Installed.Count == 2, "restart does not restore removed sample defaults");
        var legacyFile = Path.Combine(dir, "distribution-legacy.json");
        var legacy = TestLibrary.Create(legacyFile);
        check(new LibraryStore(legacyFile).Library.Installed.Count == legacy.Library.Installed.Count, "changing release defaults never deletes existing user library entries");
        check(legacy.Library.Installed.Single(m => m.Id == "original").Name == "Base mascot" &&
              legacy.Library.Selected.Single(m => m.SourceId == "original").Name == "Base mascot",
            "legacy Korean names migrate in both installed and selected lists");
        legacy.Library.Installed.Single(m => m.Id == "original").Name = "Original mascot";
        legacy.Library.Selected.Single(m => m.SourceId == "original").Name = "Original mascot";
        legacy.Save();
        var migrated = new LibraryStore(legacyFile);
        check(migrated.Library.Installed.Single(m => m.Id == "original").Name == "Base mascot" &&
              migrated.Library.Selected.Single(m => m.SourceId == "original").Name == "Base mascot",
            "saved legacy English names migrate on load");
        migrated.Library.Installed.Single(m => m.Id == "original").Name = "My mascot";
        migrated.Save();
        check(new LibraryStore(legacyFile).Library.Installed.Single(m => m.Id == "original").Name == "My mascot",
            "a user-renamed base mascot retains its custom name");
    }
}
