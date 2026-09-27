using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexMascot.App;

internal static class PencilThemeTests
{
    internal static void Run(Action<bool, string> check, string dir)
    {
        check(PencilPalette.Paper.IsFrozen && PencilPalette.Surface.IsFrozen && PencilPalette.Ink.IsFrozen, "pencil palette and paper texture are cached frozen resources");
        var typeface = new Typeface(PencilFonts.Handwriting, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        check(typeface.TryGetGlyphTypeface(out var font) && font.FontUri.ToString().Contains(";component/Fonts/", StringComparison.OrdinalIgnoreCase), "handwriting is loaded from the embedded font, not a system fallback");
        check("설치됨선택볼륨재생속도초기화0123456789".All(c => font!.CharacterToGlyphMap.ContainsKey(c)), "embedded handwriting covers Korean UI labels and digits");
        check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Fonts", "OFL.txt")), "font license accompanies the build output");
        foreach (var kind in Enum.GetValues<PencilIconKind>().Where(k => k != PencilIconKind.None))
        {
            check(PencilGeometry.Icon(kind).IsFrozen && !PencilGeometry.Icon(kind).Bounds.IsEmpty, kind + " has scalable frozen pencil geometry");
            foreach (var size in new[] { 12, 16, 24, 48 })
            {
                var icon = new PencilIcon { Kind = kind, Width = size, Height = size };
                icon.Measure(new Size(size, size)); icon.Arrange(new Rect(0, 0, size, size));
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(icon);
                var pixels = new byte[size * size * 4]; bitmap.CopyPixels(pixels, size * 4, 0);
                check(icon.RenderSize == new Size(size, size) && Enumerable.Range(0, size * size).Any(i => pixels[i * 4 + 3] > 32) && pixels[3] == 0, kind + " fits and renders visible ink on a transparent background at " + size + "px");
            }
        }
        var borderA = PencilGeometry.Rectangle(new Size(260, 40));
        var borderB = PencilGeometry.Rectangle(new Size(260, 40));
        check(borderA.IsFrozen && borderA.ToString() == borderB.ToString(), "pencil borders are stable across repeated render/hover passes");
        var store = new LibraryStore(Path.Combine(dir, "pencil-library.json"));
        var dashboard = new LibraryDashboard(); dashboard.Initialize(store, new CustomizationManager());
        var host = new Window { Content = dashboard, Width = 1220, Height = 900, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout();
            check(ReferenceEquals(dashboard.Background, PencilPalette.Paper), "studio uses the shared paper background");
            check(dashboard.FontFamily == PencilFonts.Handwriting, "studio inherits the embedded handwriting font");
            check(dashboard.FindName("InstalledCount") is null && dashboard.FindName("SelectedCount") is null, "installed and selected count badges are removed");
            check(!Descendants<TextBlock>(dashboard).Any(t => t.Text.Equals("Mascot studio", StringComparison.OrdinalIgnoreCase)), "studio heading is removed from the layout");
            foreach (var (field, kind) in new[] { ("VolumeInput", PencilIconKind.Volume), ("SpeedInput", PencilIconKind.Speed), ("DurationInput", PencilIconKind.Duration) })
            {
                var input = (NumericDragInput)dashboard.FindName(field);
                check(input.Icon == kind && AutomationProperties.GetName(input) == input.Label && input.Content is PencilBorder, field + " retains numeric editing/accessibility with its pencil icon");
                check(input.Editor.FontFamily == PencilFonts.Numbers, field + " keeps readable numeric input");
                CheckHover(check, input, (PencilBorder)input.Content, field);
            }
            foreach (var (button, kind) in new[] { ("TestButton", PencilIconKind.Play), ("AppSettingsButton", PencilIconKind.Settings), ("PositionButton", PencilIconKind.Position), ("ResetSettingsButton", PencilIconKind.Reset), ("RegisterButton", PencilIconKind.Add), ("DeleteButton", PencilIconKind.Delete) })
            {
                var control = (Button)dashboard.FindName(button); control.ApplyTemplate();
                var glyph = (PencilIcon)control.Template.FindName("Icon", control);
                check(Pencil.GetIcon(control) == kind && glyph.Kind == kind && !glyph.IsHitTestVisible, button + " binds its icon without swallowing button clicks");
                CheckHover(check, control, (PencilBorder)control.Template.FindName("Box", control), button);
            }
            foreach (var name in new[] { "InstalledList", "SelectedList" })
            {
                var list = (ListBox)dashboard.FindName(name);
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0); item.ApplyTemplate();
                var border = (PencilBorder)item.Template.FindName("Card", item);
                CheckHover(check, item, border, name + " card");
                check(!border.Hatch && ReferenceEquals(border.Background, PencilPalette.Surface), name + " selection never fills the card green");
                var body = Descendants<PencilBorder>(item).Single(b => b.Name == "CardBody");
                var grid = (Grid)body.Child;
                var cover = grid.Children.OfType<Image>().Single();
                var caption = grid.Children.OfType<TextBlock>().Single();
                check(body.BorderThickness == new Thickness(1) && grid.RowDefinitions.Count == 0 && Grid.GetRow(cover) == Grid.GetRow(caption) && caption.VerticalAlignment == VerticalAlignment.Bottom, name + " name overlays the image instead of reserving a caption row");
                var coverBounds = cover.TransformToAncestor(grid).TransformBounds(new Rect(cover.RenderSize));
                var captionBounds = caption.TransformToAncestor(grid).TransformBounds(new Rect(caption.RenderSize));
                check(coverBounds.Contains(captionBounds) && Panel.GetZIndex(caption) > Panel.GetZIndex(cover), name + " caption is drawn inside and above the full-size image");
                check(Descendants<PencilBorder>(body).Count() == 1, name + " has no separate image-only frame (only the action button)");
                var action = grid.Children.OfType<Button>().Single(); action.ApplyTemplate();
                check(Panel.GetZIndex(action) > Panel.GetZIndex(caption), name + " add/remove action remains above the image and caption");
                CheckHover(check, action, (PencilBorder)action.Template.FindName("Box", action), name + " corner action");
                CheckCardZoom(check, item, body, cover, caption, action, name);
            }
            var splitter = (GridSplitter)dashboard.FindName("LibrarySplitter"); splitter.ApplyTemplate();
            CheckHover(check, splitter, (PencilBorder)splitter.Template.FindName("Grip", splitter), "library divider");
            var reset = (Button)dashboard.FindName("ResetSettingsButton");
            check(ReferenceEquals(reset.Background, PencilPalette.OrangeSurface) && ReferenceEquals(reset.Foreground, PencilPalette.OrangeInk) && ReferenceEquals(reset.BorderBrush, PencilPalette.OrangeLine), "reset uses the orange surface, ink and outline");
            var sort = (ComboBox)dashboard.FindName("InstalledSort");
            sort.ApplyTemplate();
            var toggle = (ToggleButton)sort.Template.FindName("DropDownToggle", sort); toggle.ApplyTemplate();
            check(((PencilIcon)toggle.Template.FindName("Arrow", toggle)).Kind == PencilIconKind.ChevronDown, "sort dropdown has the hand-drawn arrow");
            CheckHover(check, sort, (PencilBorder)toggle.Template.FindName("Box", toggle), "sort dropdown");
            var provider = (IExpandCollapseProvider)new ComboBoxAutomationPeer(sort).GetPattern(PatternInterface.ExpandCollapse);
            provider.Expand(); Pump();
            var popup = (Popup)sort.Template.FindName("PART_Popup", sort);
            check(popup.IsOpen && toggle.IsChecked == true && popup.Child is PencilBorder, "accessible expand opens the pencil popup and updates its toggle");
            var row = (ComboBoxItem)sort.ItemContainerGenerator.ContainerFromIndex(sort.SelectedIndex); row.ApplyTemplate();
            var rowBorder = (PencilBorder)row.Template.FindName("Row", row);
            check(!rowBorder.Hatch && rowBorder.Background == row.Background && rowBorder.BorderBrush != Brushes.Transparent, "selected dropdown option has only an outline, never a colored fill");
            CheckHover(check, row, rowBorder, "dropdown option");
            check(Descendants<TextBlock>(popup.Child).Any(t => t.Text == "설치 날짜"), "dropdown renders DisplayMemberPath option labels");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture((FrameworkElement)popup.Child, Path.ChangeExtension(screenshot, ".options.png"));
            provider.Collapse(); Pump();
            check(!popup.IsOpen && toggle.IsChecked == false, "collapse synchronizes the popup and arrow toggle");
            toggle.IsChecked = true; Pump();
            check(sort.IsDropDownOpen && popup.IsOpen, "arrow toggle opens the actual ComboBox dropdown");
            sort.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sort), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent }); Pump();
            check(!sort.IsDropDownOpen && !popup.IsOpen, "Escape dismisses the custom dropdown");
            sort.SelectedValue = "name";
            check(new LibraryStore(Path.Combine(dir, "pencil-library.json")).Library.InstalledSort == "name", "themed dropdown selection still persists sorting");
            var scope = (Button)dashboard.FindName("StateSettingsButton");
            scope.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Pencil.GetIcon(scope) == PencilIconKind.Back && ((NumericDragInput)dashboard.FindName("DurationInput")).Visibility == Visibility.Visible, "state view keeps image duration and a return icon");
            host.UpdateLayout();
            foreach (var name in new[] { "PlayCheck", "LoopCheck" })
            {
                var checkbox = (CheckBox)dashboard.FindName(name); checkbox.ApplyTemplate();
                CheckHover(check, checkbox, (PencilBorder)checkbox.Template.FindName("Box", checkbox), name);
            }
            scope.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Pencil.GetIcon(scope) == PencilIconKind.Settings && ((NumericDragInput)dashboard.FindName("DurationInput")).Visibility == Visibility.Collapsed, "whole view restores settings icon and hides image duration");
            if (!string.IsNullOrWhiteSpace(screenshot)) LibraryFeatureTests.Capture(dashboard, Path.ChangeExtension(screenshot, ".pencil.png"));
        }
        finally { dashboard.Shutdown(); host.Close(); }
        EditableDropdown(check);
        SharedControls(check);
        ScrollBars(check);
    }

    private static void CheckCardZoom(Action<bool, string> check, ListBoxItem item, PencilBorder body, Image cover, TextBlock caption, Button action, string name)
    {
        var enabled = item.IsEnabled; var wasOver = item.IsMouseOver;
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(body).TransformBounds(new Rect(element.RenderSize));
        bool ScaleIs(double value) => cover.RenderTransform is ScaleTransform scale && Math.Abs(scale.ScaleX - value) < .001 && Math.Abs(scale.ScaleY - value) < .001;
        try
        {
            item.IsEnabled = true; Hover(item, false); SettleZoom();
            var imageSize = cover.RenderSize; var cardSize = item.RenderSize; var bodySize = body.RenderSize;
            var captionBounds = Bounds(caption); var buttonBounds = Bounds(action);
            check(ScaleIs(1), name + " image starts at normal scale");
            Hover(item, true); SettleZoom();
            check(ScaleIs(1.15) && cover.RenderTransformOrigin == new Point(.5, .5), name + " image alone zooms 15 percent around its center on hover");
            check(item.RenderSize == cardSize && body.RenderSize == bodySize && cover.RenderSize == imageSize && Bounds(caption) == captionBounds && Bounds(action) == buttonBounds, name + " zoom leaves card layout, caption and corner action unchanged");
            check(((Grid)body.Child).ClipToBounds && Panel.GetZIndex(action) > Panel.GetZIndex(cover), name + " enlarged image stays clipped inside the card and below controls");
            var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot))
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(item), null, new Rect(item.RenderSize));
                var bitmap = new RenderTargetBitmap((int)item.ActualWidth * 2, (int)item.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.ChangeExtension(screenshot, ".zoom-" + name + ".png")); png.Save(output);
            }
            Hover(item, false); SettleZoom();
            check(ScaleIs(1), name + " image returns to normal scale on mouse leave");
            for (var i = 0; i < 4; i++) { Hover(item, true); Hover(item, false); }
            SettleZoom();
            check(ScaleIs(1), name + " rapid pointer movement does not accumulate zoom");
            item.IsEnabled = false; Hover(item, true); SettleZoom();
            check(ScaleIs(1), name + " disabled card does not zoom");
        }
        finally { item.IsEnabled = enabled; Hover(item, wasOver); SettleZoom(); }
    }

    private static void SettleZoom()
    {
        var context = SynchronizationContext.Current;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => frame.Continue = false;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); SynchronizationContext.SetSynchronizationContext(context); }
    }

    private static void ScrollBars(Action<bool, string> check)
    {
        var viewer = new ScrollViewer { Width = 260, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 900, Height = 1200, Background = PencilPalette.Inset, Child = new TextBlock { Text = "스크롤 테스트" } } };
        var host = new Window { Content = viewer, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, ShowActivated = false };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/CodexMascot.App;component/PencilTheme.xaml", UriKind.Relative) });
        try
        {
            host.Show(); host.UpdateLayout(); Pump();
            foreach (var bar in Descendants<ScrollBar>(viewer))
            {
                bar.ApplyTemplate();
                var vertical = bar.Orientation == Orientation.Vertical;
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                check(track.Orientation == bar.Orientation && track.IsDirectionReversed == vertical, "pencil scrollbar keeps the correct track direction for " + bar.Orientation);
                foreach (var control in new Control[] { track.Thumb, (RepeatButton)bar.Template.FindName("LineBackward", bar), (RepeatButton)bar.Template.FindName("LineForward", bar) })
                {
                    control.ApplyTemplate();
                    CheckHover(check, control, (PencilBorder)control.Template.FindName("Box", control), bar.Orientation + " scroll " + control.GetType().Name);
                    if (control is RepeatButton)
                    {
                        var arrow = Descendants<PencilIcon>(control).Single();
                        var arrowBounds = arrow.TransformToAncestor(control).TransformBounds(new Rect(arrow.RenderSize));
                        check(new Rect(control.RenderSize).Contains(arrowBounds), bar.Orientation + " arrow fits its button: " + arrowBounds + " in " + control.RenderSize);
                    }
                }
                var screenshot = Environment.GetEnvironmentVariable("MASCOT_LIBRARY_SCREENSHOT");
                if (!string.IsNullOrWhiteSpace(screenshot))
                {
                    var bitmap = new RenderTargetBitmap((int)bar.ActualWidth * 3, (int)bar.ActualHeight * 3, 288, 288, PixelFormats.Pbgra32);
                    // A scrollbar is offset inside its viewer; render its local
                    // bounds via a brush so that offset cannot clip the capture.
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(bar) { Stretch = Stretch.Fill }, null, new Rect(bar.RenderSize));
                    bitmap.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.ChangeExtension(screenshot, ".scroll-" + bar.Orientation + ".png")); png.Save(output);
                }
                double Offset() => vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
                var forward = (RepeatButton)bar.Template.FindName("LineForward", bar);
                ((RoutedCommand)forward.Command).Execute(null, forward); Pump();
                var afterArrow = Offset();
                check(afterArrow > 0, bar.Orientation + " scrollbar arrow scrolls the owning viewer");
                ((RoutedCommand)track.IncreaseRepeatButton.Command).Execute(null, track.IncreaseRepeatButton); Pump();
                var afterPage = Offset();
                check(afterPage > afterArrow, bar.Orientation + " scrollbar empty track performs page scrolling");
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(vertical ? 0 : 12, vertical ? 12 : 0));
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(vertical ? 0 : 12, vertical ? 12 : 0, false)); Pump();
                check(Offset() > afterPage, bar.Orientation + " pencil thumb still drags content");
                check(Math.Abs(bar.Value - Offset()) < .01 && Math.Abs(track.Value - bar.Value) < .01, bar.Orientation + " thumb remains synchronized to the scroll offset");
                var backward = (RepeatButton)bar.Template.FindName("LineBackward", bar);
                var beforeBack = Offset(); ((RoutedCommand)backward.Command).Execute(null, backward); Pump();
                check(Offset() < beforeBack, bar.Orientation + " opposite arrow scrolls back");
            }
            var beforeWheel = viewer.VerticalOffset;
            viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent }); Pump();
            check(viewer.VerticalOffset > beforeWheel, "wheel scrolling is unchanged by the pencil scrollbar template");
            var beforeKeyboard = viewer.VerticalOffset;
            viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer), 0, Key.PageDown) { RoutedEvent = Keyboard.KeyDownEvent }); Pump();
            check(viewer.VerticalOffset > beforeKeyboard, "keyboard paging remains available");
            viewer.Content = new Border { Width = 20, Height = 20 }; viewer.UpdateLayout(); Pump();
            check(viewer.ScrollableHeight == 0 && viewer.ScrollableWidth == 0 && Descendants<ScrollBar>(viewer).All(b => !b.IsEnabled), "non-overflowing content leaves themed scrollbars disabled");
        }
        finally { host.Close(); }
    }

    private static void EditableDropdown(Action<bool, string> check)
    {
        var combo = new ComboBox { IsEditable = true, Width = 280, MaxDropDownHeight = 150 };
        combo.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/CodexMascot.App;component/PencilTheme.xaml", UriKind.Relative) });
        for (var i = 0; i < 30; i++) combo.Items.Add(new ComboBoxItem { Content = "프로젝트 " + i });
        combo.SelectedIndex = 0;
        var host = new Window { Content = combo, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); host.UpdateLayout();
            var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
            check(editor.IsVisible && editor.Text == "프로젝트 0", "editable dropdown retains native text/selection binding");
            editor.Text = "C:\\workspace\\새 프로젝트";
            check(combo.Text == editor.Text, "editable dropdown accepts a custom project path");
            combo.SelectedIndex = 1;
            check(editor.Text == "프로젝트 1", "selecting a ComboBoxItem updates editable text");
            combo.IsDropDownOpen = true; Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            var body = (FrameworkElement)popup.Child;
            var scroll = Descendants<ScrollViewer>(body).Single();
            check(body.ActualHeight <= 150 && scroll.ScrollableHeight > 0, "long dropdowns respect the height limit and remain scrollable");
            combo.IsDropDownOpen = false; combo.IsReadOnly = true;
            check(editor.IsReadOnly, "editable dropdown preserves read-only behavior");
        }
        finally { host.Close(); }
    }

    private static void SharedControls(Action<bool, string> check)
    {
        var panel = new StackPanel();
        var text = new TextBox { Text = "프로젝트 폴더", AcceptsReturn = true, Height = 60 };
        var checkbox = new CheckBox { Content = "알림", IsThreeState = true };
        var toggle = new ToggleButton { Content = "Hooks" };
        var tabs = new TabControl();
        var first = new TabItem { Header = "감시", Content = "감시 내용" };
        var second = new TabItem { Header = "실행", Content = "실행 내용" };
        tabs.Items.Add(first); tabs.Items.Add(second);
        var list = new ListBox { ItemsSource = new[] { "승인 요청", "질문" } };
        var view = new GridView(); view.Columns.Add(new GridViewColumn { Header = "작업", DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
        var jobs = new ListView { View = view, ItemsSource = new[] { new { Name = "작업 1" }, new { Name = "작업 2" } } };
        foreach (var control in new Control[] { text, checkbox, toggle, tabs, list, jobs }) panel.Children.Add(control);
        var host = new Window { Content = panel, Width = 500, Height = 500, ShowInTaskbar = false, ShowActivated = false };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/CodexMascot.App;component/PencilTheme.xaml", UriKind.Relative) });
        toggle.Style = (Style)host.Resources["PencilToggle"];
        try
        {
            host.Show(); host.UpdateLayout();
            foreach (var control in new Control[] { text, checkbox, toggle, first, second, (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0), (ListViewItem)jobs.ItemContainerGenerator.ContainerFromIndex(0) })
            {
                control.ApplyTemplate();
                CheckHover(check, control, (PencilBorder)control.Template.FindName("Box", control), control.GetType().Name);
            }
            text.SelectAll(); text.SelectedText = "C:\\프로젝트\n두 번째 줄"; text.SelectAll();
            check(text.Text.Contains("두 번째 줄") && text.SelectionLength == text.Text.Length, "custom TextBox template retains text selection and multiline editing");
            checkbox.IsChecked = true;
            check(((FrameworkElement)checkbox.Template.FindName("CheckMark", checkbox)).Visibility == Visibility.Visible, "checked checkbox retains its tick without a fill change");
            checkbox.IsChecked = null;
            check(((FrameworkElement)checkbox.Template.FindName("MixedMark", checkbox)).Visibility == Visibility.Visible && ((FrameworkElement)checkbox.Template.FindName("CheckMark", checkbox)).Visibility == Visibility.Collapsed, "mixed checkbox retains its distinct dash");
            toggle.IsChecked = true;
            var toggleBorder = (PencilBorder)toggle.Template.FindName("Box", toggle);
            check(toggleBorder.Background == toggle.Background && !toggleBorder.Hatch, "checked hook toggle does not introduce a green background");
            tabs.SelectedIndex = 1;
            check(second.IsSelected && (string)tabs.SelectedContent == "실행 내용", "outlined tabs still switch content");
            jobs.SelectedIndex = 1; list.SelectedIndex = 1; host.UpdateLayout();
            check(Descendants<TextBlock>(jobs).Any(t => t.Text == "작업 2") && jobs.SelectedIndex == 1 && list.SelectedIndex == 1, "outline-only lists retain GridView columns and selection");
        }
        finally { host.Close(); }
    }

    // Exercise WPF's actual template triggers without moving the user's OS pointer.
    private static readonly DependencyPropertyKey MouseOverKey = (DependencyPropertyKey)typeof(UIElement)
        .GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static readonly MethodInfo WriteFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly object MouseOverCache = Enum.Parse(WriteFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
    private static void Hover(Control control, bool value)
    {
        // UIElement's CLR getter uses a cached flag while template triggers read
        // the dependency property. Keep both synchronized like WPF input does.
        WriteFlag.Invoke(control, new[] { MouseOverCache, (object)value });
        control.SetValue(MouseOverKey, value);
        control.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = value ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
        control.UpdateLayout();
    }
    private static void CheckHover(Action<bool, string> check, Control control, PencilBorder border, string name)
    {
        var enabled = control.IsEnabled; var wasOver = control.IsMouseOver;
        try
        {
            control.IsEnabled = true; Hover(control, false);
            var fill = border.Background; var hatch = border.Hatch; var hatchBrush = border.HatchBrush;
            var ink = control.Foreground; var size = border.RenderSize; var line = border.BorderBrush; var thickness = border.BorderThickness;
            Hover(control, true);
            check(ReferenceEquals(border.BorderBrush, PencilPalette.Emphasis), name + " hover darkens its outline");
            check(border.Background == fill && border.Hatch == hatch && border.HatchBrush == hatchBrush && control.Foreground == ink && border.RenderSize == size && border.BorderThickness == thickness, name + " hover leaves fill, hatching, text and layout unchanged");
            Hover(control, false);
            check(border.BorderBrush == line, name + " restores its previous outline on leave");
            control.IsEnabled = false; Hover(control, true);
            check(border.BorderBrush != PencilPalette.Emphasis && border.Background == fill, name + " disabled state is not hover-emphasized");
        }
        finally { Hover(control, wasOver); control.IsEnabled = enabled; }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var context = SynchronizationContext.Current;
        try
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
}
