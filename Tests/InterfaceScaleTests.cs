namespace Tempora.Tests;

using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Utility;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

public partial class TimingShortcutTests
{
    private static async Task VerifyInterfaceScaling(ISceneRunner runner, SubViewport viewport)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var application = root.GetNode<Application>("Application");
        Vector2I originalSize = root.Size;
        Vector2I originalViewportSize = viewport.Size;
        double originalSystemScale = application.SystemTextScale;
        double originalScale = Project.Instance.Settings.TextScaleFactor;
        string originalLanguage = Project.Instance.Settings.Language;
        root.Size = new Vector2I(1280, 720);
        application.ApplySystemTextScale(1);
        Project.Instance.Settings.TextScaleFactor = 1;
        Project.Instance.Settings.Language = "ko";
        try
        {
            await Settle(runner);
            var window = (Window)runner.FindChild("VisualSettingsWindow")!;
            var menu = (PopupMenu)runner.FindChild("Options")!;
            var menuButton = (Button)runner.FindChild("OptionsButton")!;
            var percentage = (SpinBox)window.FindChild("Percentage");
            var apply = window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/SaveButton");
            window.GetNode<TabContainer>("Margin/Scroll/Layout/TabContainer").CurrentTab = 0;
            int fontSize = apply.GetThemeFontSize("font_size");
            Vector2 checkSize = menu.GetThemeIcon("checked").GetSize();
            window.Popup();
            menuButton.EmitSignal(Button.SignalName.Pressed);
            await Settle(runner);
            foreach (int percent in new[] { 200, 100, 150 })
            {
                var elapsed = Stopwatch.StartNew();
                percentage.Value = percent;
                await Settle(runner);
                viewport.Size = (Vector2I)root.GetVisibleRect().Size;
                await Settle(runner);
                AssertToolbarFits(runner);
                GD.Print($"Interface preview {percent}%: six layout frames {elapsed.Elapsed.TotalMilliseconds:F2} ms");
                float expectedScale = percent / 100f;
                AssertThat(root.GetStretchTransform().Scale.X).IsEqualApprox(expectedScale, 0.001f);
                AssertThat(apply.GetThemeFontSize("font_size")).IsEqual(fontSize);
                AssertThat(window.ContentScaleFactor).IsEqual(1f);
                AssertThat(menu.GetThemeIcon("checked").GetSize() * root.GetStretchTransform().Scale)
                    .IsEqual(checkSize * expectedScale);
                AssertThat(menu.Visible).IsTrue();
                AssertThat(window.Visible).IsTrue();
                AssertWindowFits(root, window);
                AssertWindowFits(root, menu);
                AssertThat(menu.Size.Y).IsEqual((int)menu.GetContentsMinimumSize().Y);
                await Capture(runner, root, $"interface-{percent}.png");
                menu.Hide();
                apply.EmitSignal(Button.SignalName.Pressed);
                await VerifyScaledDragging(runner, viewport, root);
                window.Popup();
                menuButton.EmitSignal(Button.SignalName.Pressed);
            }

            // Large system text changes layout without multiplying the interface scale again.
            application.ApplySystemTextScale(2.25);
            await Settle(runner);
            AssertThat(root.GetStretchTransform().Scale.X).IsEqualApprox(1.5f, 0.001f);
            AssertThat(window.GetThemeConstant("title_height")).IsGreaterEqual(
                (int)window.GetThemeFont("title_font").GetHeight(window.GetThemeFontSize("title_font_size")));
            AssertWindowFits(root, window);
            AssertWindowFits(root, menu);
            var scroll = window.GetNode<ScrollContainer>("Margin/Scroll");
            scroll.EnsureControlVisible(apply);
            await Settle(runner);
            AssertThat(scroll.GetGlobalRect().Intersects(apply.GetGlobalRect())).IsTrue();
            await Capture(runner, root, "interface-large-text.png");
            menu.Hide();
            await Capture(runner, root, "interface-settings-large-text.png");
            window.Hide();
            Project.Instance.Settings.TextScaleFactor = 2;
            viewport.Size = (Vector2I)root.GetVisibleRect().Size;
            await VerifyFileDialogAccess(runner, root);
        }
        finally
        {
            application.ApplySystemTextScale(originalSystemScale);
            Project.Instance.Settings.TextScaleFactor = originalScale;
            Project.Instance.Settings.Language = originalLanguage;
            root.Size = originalSize;
            viewport.Size = originalViewportSize;
            await Settle(runner);
        }
    }

    private static async Task VerifyScaledDragging(ISceneRunner runner, SubViewport viewport, Window root)
    {
        Timing originalTiming = Project.Instance.Timing;
        int originalGrid = Project.Instance.Settings.GridDivisor;
        Vector2I originalSize = viewport.Size;
        var timing = new Timing(Project.Instance.Settings) { IsInstantiating = true };
        var container = (AudioVisualsContainer)runner.FindChild("AudioVisualsContainer")!;
        Project.Instance.Timing = timing;
        Project.Instance.Settings.GridDivisor = 4;
        try
        {
            timing.AddTimingPoint(0d, 0d, 0.5d);
            timing.AddTimingPoint(0.5d, 1d, 0.5d);
            timing.AddTimingPoint(2d, 4d, 0.5d);
            timing.IsInstantiating = false;
            MementoHandler.Instance.ResetTimingHistory();

            viewport.Size = root.Size;
            viewport.Size2DOverride = originalSize;
            viewport.Size2DOverrideStretch = true;
            container.UpdateBlocksScroll();
            ((Range)runner.FindChild("BlockScrollBar")!).Value = 0;
            await Settle(runner);
            var panel = container.AudioBlocks[0].AudioDisplayPanel;
            panel.UpdateTimingPointsIndices();
            panel.UpdateVisuals();

            TimingPoint point = timing.TimingPoints[1];
            var transform = viewport.GetFinalTransform();
            Vector2 start = transform * (panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0.5f), panel.Size.Y / 2));
            Vector2 end = transform * (panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0.75f), panel.Size.Y / 2));

            SendInput(viewport, new InputEventMouseMotion { Position = start });
            SendInput(viewport, new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
            await runner.AwaitInputProcessed();
            AssertThat(Context.Instance.HeldTimingPoint).IsEqual(point);

            SendInput(viewport, new InputEventMouseMotion { Position = end, Relative = end - start, ButtonMask = MouseButtonMask.Left });
            await runner.AwaitInputProcessed();
            SendInput(viewport, new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left });
            await runner.AwaitInputProcessed();
            AssertThat(point.MeasurePosition!.Value).IsEqualApprox(0.75d, 0.001d);
            AssertThat(point.Offset).IsEqual(1d);
            AssertThat(Context.Instance.HeldTimingPoint).IsNull();
            MementoHandler.Instance.Undo();
            AssertThat(Project.Instance.Timing.TimingPoints[1].MeasurePosition!.Value).IsEqual(0.5d);
        }
        finally
        {
            Context.Instance.HeldTimingPoint = null;
            TimingPointSelection.Instance.DeselectAll();
            Project.Instance.Timing = originalTiming;
            Project.Instance.Settings.GridDivisor = originalGrid;
            viewport.Size2DOverrideStretch = false;
            viewport.Size2DOverride = Vector2I.Zero;
            viewport.Size = originalSize;
            container.UpdateBlocksScroll();
            await Settle(runner);
        }
    }

    private static async Task VerifyFileDialogAccess(ISceneRunner runner, Window root)
    {
        ProjectFileManager.Instance.LoadFileDialogPopup();
        FileDialog dialog = ProjectFileManager.Instance.LoadFileDialog;
        try
        {
            dialog.CurrentDir = ProjectSettings.GlobalizePath("res://Audio");
            await Settle(runner);
            var list = dialog.GetVBox().FindChildren("*", "ItemList", true, false).Cast<ItemList>()
                .First(list => Enumerable.Range(0, list.ItemCount).Any(index => list.GetItemText(index) == "Loop.mp3"));
            AssertThat(list.Size.Y).IsGreaterEqual(list.GetThemeFont("font").GetHeight(list.GetThemeFontSize("font_size")));
            var scroll = (ScrollContainer)dialog.GetVBox().GetParent();
            scroll.EnsureControlVisible(list);
            await Settle(runner);
            AssertThat(scroll.GetGlobalRect().Intersects(list.GetGlobalRect())).IsTrue();
            int item = Enumerable.Range(0, list.ItemCount).First(index => list.GetItemText(index) == "Loop.mp3");
            list.Select(item);
            list.EnsureCurrentIsVisible();
            list.EmitSignal(ItemList.SignalName.ItemSelected, item);
            AssertThat(dialog.GetLineEdit().Text).IsEqual("Loop.mp3");
            AssertWindowFits(root, dialog);
            await Capture(runner, root, "interface-file-dialog.png");
        }
        finally
        {
            dialog.Hide();
        }
    }

    private static void AssertToolbarFits(ISceneRunner runner)
    {
        foreach (string name in new[] { "GridScrollBar", "PlaybackRateScrollBar" })
        {
            var control = (Control)runner.FindChild(name)!;
            var content = control.GetNode<Control>("Slider/Content");
            AssertThat(control.GetGlobalRect().Encloses(content.GetGlobalRect())).IsTrue();
        }
        foreach (string name in new[] { "VolumeSliderMusic", "VolumeSliderMetronome", "VolumeSliderMaster" })
        {
            var control = (Control)runner.FindChild(name)!;
            AssertThat(control.GetGlobalRect().Encloses(control.GetParent().GetNode<Control>("Label").GetGlobalRect())).IsTrue();
        }
    }

    private static void AssertWindowFits(Window root, Window window)
    {
        var bounds = (Rect2I)root.GetVisibleRect();
        AssertThat(bounds.Encloses(new Rect2I(window.Position, window.Size))).IsTrue();
        if (!window.Borderless)
            AssertThat(window.Position.Y).IsGreaterEqual(window.GetThemeConstant("title_height"));
    }

    private static async Task Settle(ISceneRunner runner)
    {
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
    }

    private static async Task Capture(ISceneRunner runner, Window root, string name)
    {
        if (DisplayServer.GetName() == "headless")
            return;
        await Settle(runner);
        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = root.GetTexture().GetImage();
        AssertThat(image.SavePng("user://" + name)).IsEqual(Error.Ok);
    }
}