namespace Tempora.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

public partial class TimingShortcutTests
{
    private static ISceneRunner LoadWithAutosavedTiming(SubViewport viewport)
    {
        string path = ProjectSettings.GlobalizePath($"{ProjectFileManager.AutoSavePath}{ProjectFileManager.ProjectFileExtension}");
        byte[]? original = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        string audioPath = ProjectSettings.GlobalizePath("res://Audio/Loop.mp3");
        try
        {
            System.IO.File.WriteAllText(path, $"[AudioPath]\n{audioPath}\n[TimeSignaturePoints]\n[TimingPoints]\n0;0;4;4;0.5832166666666667\n");
            var main = ResourceLoader.Load<PackedScene>("res://Classes/Visual/Main.tscn").Instantiate<Control>();
            // The test viewport interrupts the Window theme inheritance used by the real scene.
            main.Theme = ((SceneTree)Engine.GetMainLoop()).Root.Theme;
            viewport.AddChild(main);
            return ISceneRunner.Load(viewport, autoFree: true);
        }
        finally
        {
            if (original != null)
                System.IO.File.WriteAllBytes(path, original);
            else
                System.IO.File.Delete(path);
        }
    }

    private static async Task VerifyRestoredBpmPosition(ISceneRunner runner, AudioVisualsContainer container)
    {
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AudioDisplayPanel panel = container.AudioBlocks.Select(row => row.AudioDisplayPanel).First(panel => VisibleTimingPoints(panel).Any());
        var point = VisibleTimingPoints(panel).Single();
        AssertThat(point.BpmLabel.Text).IsEqual("139.972");
        AssertThat(point.BpmLabel.GetGlobalRect().GetCenter().X).IsEqualApprox(point.GlobalPosition.X, 0.01f);
        AssertThat(point.BpmLabel.GetGlobalRect().Position.Y).IsEqualApprox(panel.GlobalPosition.Y, 0.01f);
    }

    private static async Task VerifyPointerTimeLayout(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        int originalRows = Project.Instance.Settings.NumberOfRows;
        Vector2I originalSize = viewport.Size;
        Timing originalTiming = Project.Instance.Timing;
        Timing timing = new Timing(Project.Instance.Settings) { IsInstantiating = true };
        Project.Instance.Timing = timing;

        try
        {
            timing.AddTimingPoint(0d, 0d, 0.5d);
            timing.AddTimingPoint(0.5d, 1d, 0.5d);
            timing.AddTimingPoint(1d, 2d, 0.5d);
            timing.AddTimingPoint(8d, 16d, 0.5d);
            timing.AddTimingPoint(12.98d, 25.96d, 0.5d);
            timing.AddTimingPoint(13d, 26d, 0.5d);
            timing.IsInstantiating = false;
            ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).UpdateLimits();
            await VerifyRestoredRows(runner, viewport, container);

            foreach (int rows in new[] { 10, 30, 3, 1 })
            {
                Project.Instance.Settings.NumberOfRows = rows;
                foreach (int height in new[] { 720, 480, 471, 1080 })
                    await VerifyPointerTimeAtHeight(runner, viewport, container, height);
                AssertThat(Project.Instance.Settings.NumberOfRows).IsEqual(rows);
            }
            await VerifyBpmEditing(runner, viewport, container);
            SendInput(viewport, new InputEventMouseMotion { Position = container.AudioBlocks[0].AudioDisplayPanel.GetGlobalRect().GetCenter() });
            await runner.AwaitInputProcessed();
            Project.Instance.Settings.NumberOfRows = 10;
            viewport.Size = new Vector2I(1280, 720);
            await runner.AwaitIdleFrame();
            await runner.AwaitIdleFrame();
            await VerifyTimeSignatureOverlay(runner, viewport, container.AudioBlocks[1]);
            await VerifyTimelineScrollbar(runner, viewport, container);
        }
        finally
        {
            MusicPlayer.Instance.Pause();
            Context.Instance.HeldTimingPoint = null;
            Context.Instance.TimingPointNearestCursor = null;
            TimingPointSelection.Instance.DeselectAll();
            Project.Instance.Timing = originalTiming;
            Project.Instance.Settings.NumberOfRows = originalRows;
            viewport.Size = originalSize;
        }
    }

    private static async Task VerifyBpmEditing(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).Value = 13;
        await runner.AwaitIdleFrame();
        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        var point = VisibleTimingPoints(panel).Last();
        int count = Project.Instance.Timing.TimingPoints.Count;

        DoubleClickBpm(viewport, point);
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.HasFocus()).IsTrue();
        AssertThat(point.BpmLabel.Visible).IsFalse();
        AssertThat(point.BpmEdit.GetGlobalRect().GetCenter().X).IsEqualApprox(point.GlobalPosition.X, 0.01f);
        AssertThat(point.BpmEdit.GetGlobalRect().Position.Y + 0.01f).IsGreaterEqual(panel.GlobalPosition.Y);
        Vector2 inputPosition = point.BpmEdit.GetGlobalRect().Position + new Vector2(8, point.BpmEdit.Size.Y / 2);
        SendInput(viewport, new InputEventMouseMotion { Position = inputPosition });
        SendInput(viewport, new InputEventMouseButton { Position = inputPosition, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseButton { Position = inputPosition, ButtonIndex = MouseButton.Left });
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.HasFocus()).IsTrue();
        TypeText(viewport, "175");
        await SelectBpmText(runner, viewport, point.BpmEdit);
        AssertThat(point.BpmEdit.GetSelectedText()).IsEqual("175");
        TypeText(viewport, "150");
        PressKey(viewport, Key.Enter, true);
        PressKey(viewport, Key.Enter, false);
        await runner.AwaitInputProcessed();
        AssertThat(point.TimingPoint.Bpm).IsEqual(150d);
        AssertThat(point.BpmEdit.Visible).IsFalse();
        AssertThat(point.BpmLabel.Visible).IsTrue();

        DoubleClickBpm(viewport, point);
        await runner.AwaitInputProcessed();
        TypeText(viewport, "200");
        await ClickTimeSignatureButton(runner, viewport, (Stepper)container.AudioBlocks[0].FindChild("NumeratorStepper")!, "IncrementButton");
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.Visible).IsFalse();
        AssertThat(point.BpmLabel.Visible).IsTrue();
        AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(Colors.White);
        AssertThat(Project.Instance.Timing.TimingPoints.Count).IsEqual(count);
        AssertThat(point.TimingPoint.Bpm).IsEqual(150d);
    }

    private static void TypeText(SubViewport viewport, string text)
    {
        foreach (char character in text)
        {
            SendInput(viewport, new InputEventKey { Keycode = (Key)character, Unicode = character, Pressed = true });
            SendInput(viewport, new InputEventKey { Keycode = (Key)character });
        }
    }

    private static async Task SelectBpmText(ISceneRunner runner, SubViewport viewport, BpmEdit editor)
    {
        Rect2 bounds = editor.GetGlobalRect();
        Vector2 start = bounds.Position + new Vector2(8, bounds.Size.Y / 2);
        Vector2 end = start + new Vector2(bounds.Size.X - 12, 0);
        Drag(viewport, start, end);
        await runner.AwaitInputProcessed();
    }

    private static void Drag(SubViewport viewport, Vector2 start, Vector2 end)
    {
        SendInput(viewport, new InputEventMouseMotion { Position = start });
        SendInput(viewport, new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseMotion { Position = end, Relative = end - start, ButtonMask = MouseButtonMask.Left });
        SendInput(viewport, new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left });
    }

    private static async Task ClickTimeSignatureButton(ISceneRunner runner, SubViewport viewport, Stepper stepper, string buttonName)
    {
        SendInput(viewport, new InputEventMouseMotion { Position = stepper.ValueLabel.GetGlobalRect().GetCenter() });
        await runner.AwaitInputProcessed();
        var button = (Button)stepper.FindChild(buttonName)!;
        Vector2 position = button.GetGlobalRect().GetCenter();
        SendInput(viewport, new InputEventMouseMotion { Position = position });
        await runner.AwaitInputProcessed();
        AssertThat(button.Visible).IsTrue();
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left });
        await runner.AwaitInputProcessed();
    }

    private static void AssertTimeSignatureButtonPlacement(AudioBlock row, Stepper stepper)
    {
        var measure = (Label)row.FindChild("MeasureLabel")!;
        var signature = (Control)row.FindChild("TimeSignatureStepper")!;
        AssertThat(measure.GetGlobalRect().End.Y).IsLessEqual(signature.GlobalPosition.Y);
        var button = (Button)stepper.FindChild("IncrementButton")!;
        var decrease = (Button)stepper.FindChild("DecrementButton")!;
        Rect2 numberBounds = stepper.ValueLabel.GetGlobalRect();
        AssertThat(button.Visible).IsTrue();
        AssertThat(button.GetGlobalRect().GetCenter().X).IsEqualApprox(numberBounds.GetCenter().X, 0.01f);
        AssertThat(decrease.GetGlobalRect().GetCenter().X).IsEqualApprox(numberBounds.GetCenter().X, 0.01f);
        AssertThat(button.GetGlobalRect().End.Y).IsEqualApprox(numberBounds.Position.Y, 0.01f);
        AssertThat(decrease.GlobalPosition.Y).IsEqualApprox(numberBounds.End.Y, 0.01f);
    }

    private static async Task VerifyTimeSignatureOverlay(ISceneRunner runner, SubViewport viewport, AudioBlock row)
    {
        Rect2 audioBounds = row.AudioDisplayPanel.GetGlobalRect();
        foreach (var (name, index) in new[] { ("NumeratorStepper", 0), ("DenominatorStepper", 1) })
        {
            var stepper = (Stepper)row.FindChild(name)!;
            int original = stepper.DisplayedValue;
            int increased = index == 0 ? original + 1 : original * 2;
            foreach (var (buttonName, expected) in new[] { ("IncrementButton", increased), ("DecrementButton", original) })
            {
                await ClickTimeSignatureButton(runner, viewport, stepper, buttonName);
                AssertTimeSignatureButtonPlacement(row, stepper);
                AssertThat(row.AudioDisplayPanel.GetGlobalRect()).IsEqual(audioBounds);
                AssertThat(stepper.DisplayedValue).IsEqual(expected);

                SendInput(viewport, new InputEventMouseMotion { Position = audioBounds.GetCenter() });
                await runner.AwaitInputProcessed();
                AssertThat(Project.Instance.Timing.GetTimeSignature(row.NominalMeasurePosition)[index]).IsEqual(expected);
                AssertThat(stepper.HasMouseInside).IsFalse();
                AssertThat(stepper.GetNode<Button>("ValueLabel/IncrementButton").Visible).IsFalse();
                AssertThat(row.AudioDisplayPanel.GetGlobalRect()).IsEqual(audioBounds);
            }
        }
    }

    private static void DoubleClickBpm(SubViewport viewport, VisualTimingPoint point)
    {
        Vector2 position = point.BpmLabel.GetGlobalRect().GetCenter();
        SendInput(viewport, new InputEventMouseMotion { Position = position });
        DoubleClickAt(viewport, position);
    }

    private static void DoubleClickAt(SubViewport viewport, Vector2 position)
    {
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left });
    }

    private static async Task VerifyRestoredRows(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        Project.Instance.Settings.NumberOfRows = 10;
        scrollbar.Value = 0;
        viewport.Size = new Vector2I(1280, 720);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        int restoredIndex = container.VisibleRowCount - 1;

        viewport.Size = new Vector2I(1280, 471);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(container.AudioBlocks[restoredIndex].Visible).IsFalse();
        scrollbar.Value = 13 - restoredIndex;

        viewport.Size = new Vector2I(1280, 720);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AudioDisplayPanel restored = container.AudioBlocks[restoredIndex].AudioDisplayPanel;
        IAudioSegmentDisplay waveform = restored.GetNode<Node2D>("AudioSegments").GetChildren().OfType<IAudioSegmentDisplay>().First(segment => segment.Visible);
        AssertThat(waveform.TimeRange[0]).IsEqualApprox((float)Project.Instance.Timing.MeasurePositionToOffset(restored.ActualMeasurePositionStartForPanel), 0.001f);
        AssertThat(VisibleTimingPoints(restored).Last().TimingPoint.MeasurePosition!.Value).IsEqual(13d);
    }

    private static async Task VerifyPointerTimeAtHeight(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container, int height)
    {
        viewport.Size = new Vector2I(1280, height);
        ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).Value = 0;
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(container.NominalMeasurePositionStartForTopBlock).IsLessEqual(container.LastTopMeasure);
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        int totalMeasures = container.LastRowMeasure - container.FirstTopMeasure + 1;
        AssertThat(scrollbar.Page).IsEqual((double)System.Math.Min(container.VisibleRowCount, totalMeasures));
        AssertThat(scrollbar.MaxValue - scrollbar.Page).IsEqual((double)container.LastTopMeasure);

        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        panel.UpdateTimingPointsIndices();
        panel.UpdateVisuals();
        var point = VisibleTimingPoints(panel).First();
        Vector2 cursor = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0), panel.Size.Y / 2);
        SendInput(viewport, new InputEventMouseMotion { Position = cursor });
        await runner.AwaitInputProcessed();
        AssertThat(Context.Instance.TimingPointNearestCursor).IsEqual(point.TimingPoint);
        AssertReadable(panel, point);
        AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(new Color("ff9900"));

        if (height == 471 && Project.Instance.Settings.NumberOfRows == 30)
        {
            AudioDisplayPanel below = container.AudioBlocks[1].AudioDisplayPanel;
            var belowPoint = VisibleTimingPoints(below).First();
            float belowBpmY = belowPoint.BpmLabel.GlobalPosition.Y;
            await VerifyRejectedDrag(runner, viewport, panel, cursor);
            AssertThat(point.OffsetLine.DefaultColor).IsEqual(Colors.Red);
            AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(Colors.Red);
            AssertReadable(panel, point);
            AssertThat(belowPoint.BpmLabel.GlobalPosition.Y).IsEqual(belowBpmY);
            AssertBpmBetweenRows(panel, below, belowPoint);

            MusicPlayer.Instance.SeekPlay(0);
            await runner.AwaitMillis(100);
            AssertThat(panel.Playhead.Visible).IsTrue();
            AssertReadable(panel, point);
            MusicPlayer.Instance.Pause();
            await runner.AwaitMillis(400);
            AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(new Color("ff9900"));
        }

        AudioBlock[] visible = container.AudioBlocks.Where(block => block.Visible).ToArray();
        if (visible.Length > 1)
        {
            var nextPoint = VisibleTimingPoints(visible[1].AudioDisplayPanel).First();
            AssertBpmBetweenRows(panel, visible[1].AudioDisplayPanel, nextPoint);
            AssertThat(TextBounds(panel.GetNode<PreviewLine>("PreviewLine").TimeLabel).End.Y).IsLessEqual(TextBounds(nextPoint.BpmLabel).Position.Y);

            float bpmY = nextPoint.BpmLabel.GlobalPosition.Y;
            Label nextTime = visible[1].AudioDisplayPanel.GetNode<PreviewLine>("PreviewLine").TimeLabel;
            float timeY = nextTime.GlobalPosition.Y;
            Vector2 nextCursor = nextPoint.GlobalPosition;
            SendInput(viewport, new InputEventMouseMotion { Position = nextCursor });
            await runner.AwaitInputProcessed();
            AssertThat(nextPoint.BpmLabel.GlobalPosition.Y).IsEqual(bpmY);
            AssertThat(nextTime.GlobalPosition.Y).IsEqual(timeY);
            AssertBpmBetweenRows(panel, visible[1].AudioDisplayPanel, nextPoint);
            AssertReadable(visible[1].AudioDisplayPanel, nextPoint);
            if (height == 1080 && Project.Instance.Settings.NumberOfRows == 3)
                await VerifyRaisedBpmEditing(runner, viewport, container);
        }
        AssertThat(visible[^1].GetGlobalRect().End.Y).IsLessEqual(container.GetGlobalRect().End.Y + 1);
        await VerifySongEndScrolling(runner, viewport, visible[^1], cursor);
    }

    private static async Task VerifyRaisedBpmEditing(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).Value = 12;
        await runner.AwaitIdleFrame();
        var point = VisibleTimingPoints(container.AudioBlocks[1].AudioDisplayPanel).Last();
        SendInput(viewport, new InputEventMouseMotion { Position = point.GlobalPosition });
        await runner.AwaitInputProcessed();
        Vector2 position = point.BpmLabel.GetGlobalRect().GetCenter();
        SendInput(viewport, new InputEventMouseMotion { Position = position });
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(new Color("ff9900"));
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left });
        await runner.AwaitInputProcessed();
        DoubleClickAt(viewport, position);
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.HasFocus()).IsTrue();
        TypeText(viewport, point.TimingPoint.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PressKey(viewport, Key.Enter, true);
        PressKey(viewport, Key.Enter, false);
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.Visible).IsFalse();
    }

    private static async Task VerifyRejectedDrag(ISceneRunner runner, SubViewport viewport, AudioDisplayPanel panel, Vector2 cursor)
    {
        PressKey(viewport, Key.Escape, true);
        PressKey(viewport, Key.Escape, false);
        TimingPoint movingPoint = Project.Instance.Timing.TimingPoints[1];
        Vector2 start = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0.5f), panel.Size.Y / 2);
        Vector2 end = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(-0.0625f), panel.Size.Y / 2);
        SendInput(viewport, new InputEventMouseMotion { Position = start });
        SendInput(viewport, new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
        await runner.AwaitInputProcessed();
        AssertThat(Context.Instance.HeldTimingPoint).IsEqual(movingPoint);

        SendInput(viewport, new InputEventMouseMotion { Position = end, Relative = end - start });
        SendInput(viewport, new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left });
        SendInput(viewport, new InputEventMouseMotion { Position = cursor });
        await runner.AwaitInputProcessed();
        AssertThat(movingPoint.MeasurePosition!.Value).IsEqual(0.5d);
    }

    private static async Task VerifyTimelineScrollbar(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        await VerifyScrollbarWheel(runner, viewport, container, scrollbar);
        await VerifyMinimapRendering(runner, container, scrollbar);
        await VerifySongEdgesInMinimap(runner, container, scrollbar);
        Project.Instance.Settings.NumberOfRows = 1;
        await runner.AwaitIdleFrame();
        scrollbar.Value = scrollbar.MinValue;

        Vector2 start = scrollbar.GlobalPosition + new Vector2(scrollbar.Size.X / 2, 23);
        Vector2 end = start + new Vector2(0, scrollbar.Size.Y);
        Drag(viewport, start, end);
        await runner.AwaitInputProcessed();
        AssertThat(container.AudioBlocks[0].NominalMeasurePosition).IsEqual(container.LastRowMeasure);

        var marker = scrollbar.GetNode<ColorRect>("PlaybackMarker");
        double midpoint = (scrollbar.MinValue + scrollbar.MaxValue) / 2;
        MusicPlayer.Instance.SeekPlay((float)Project.Instance.Timing.MeasurePositionToOffset(midpoint));
        await runner.AwaitMillis(100);
        AssertThat(marker.Visible).IsTrue();
        AssertThat(marker.Color).IsEqual(container.AudioBlocks[0].AudioDisplayPanel.Playhead.DefaultColor);
        AssertThat(marker.Position.Y).IsEqualApprox(scrollbar.Size.Y / 2, 5f);
        AssertThat(marker.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        await Capture(runner, viewport, "minimap-playback.png");

        MusicPlayer.Instance.Pause();
        await runner.AwaitIdleFrame();
        AssertThat(marker.Visible).IsFalse();
        MusicPlayer.Instance.SeekPlay(Project.Instance.AudioFile.GetAudioLength() - 0.05f);
        await runner.AwaitMillis(250);
        AssertThat(marker.Visible).IsFalse();

        AudioFile originalAudio = Project.Instance.AudioFile;
        try
        {
            Project.Instance.AudioFile = new AudioFile(ResourceLoader.Load<AudioStreamMP3>("res://Audio/clickedy.mp3"));
            Project.Instance.Settings.NumberOfRows = 10;
            await runner.AwaitIdleFrame();
            AssertThat(scrollbar.Page).IsEqual(scrollbar.MaxValue - scrollbar.MinValue);
            scrollbar.Value = scrollbar.MaxValue;
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(container.FirstTopMeasure);
        }
        finally
        {
            Project.Instance.AudioFile = originalAudio;
        }
    }

    private static async Task VerifyMinimapRendering(ISceneRunner runner, AudioVisualsContainer container, BlockScrollBar scrollbar)
    {
        await Settle(runner);
        AssertThat(scrollbar.GetThemeStylebox("scroll")).IsInstanceOf<StyleBoxTexture>();
        var track = (StyleBoxTexture)scrollbar.GetThemeStylebox("scroll");
        Texture2D texture = track.Texture;
        using Image image = texture.GetImage();
        AssertThat(image.GetWidth()).IsEqual((int)System.Math.Ceiling(scrollbar.Size.X));
        AssertThat(image.GetData().Distinct().Count()).IsGreater(10);
        foreach (string name in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var handle = (StyleBoxFlat)scrollbar.GetThemeStylebox(name);
            AssertThat(handle.BgColor.A).IsBetween(0.05f, 0.5f);
            AssertThat(handle.BorderWidthLeft).IsGreater(0);
        }

        scrollbar.Value = scrollbar.MinValue;
        await Settle(runner);
        AssertMinimapMatchesRow(container, scrollbar);
        scrollbar.Value = scrollbar.MaxValue;
        await Settle(runner);
        AssertThat(track.Texture).IsSame(texture);
        AssertMinimapMatchesRow(container, scrollbar);

        TimingPoint lastPoint = Project.Instance.Timing.TimingPoints.Last();
        double originalBpm = lastPoint.Bpm;
        Settings settings = Project.Instance.Settings;
        float originalOverlap = settings.MeasureOverlap;
        float originalDownbeat = settings.DownbeatPositionOffset;
        int originalRows = settings.NumberOfRows;
        var margin = scrollbar.GetParent<Control>();
        Vector2 originalMinimum = margin.CustomMinimumSize;
        var context = Project.Instance.SpectrogramContext;
        var source = context.CachedSpectrogramImage;
        var fft = context.SpectrogramGenerator.GetFFTs()[0];
        try
        {
            Project.Instance.Timing.TrySetBpm(lastPoint, originalBpm * 2);
            await Settle(runner);
            using Image edited = track.Texture.GetImage();
            AssertThat(edited.GetData().SequenceEqual(image.GetData())).IsFalse();
            AssertMinimapMatchesRow(container, scrollbar);
            settings.MeasureOverlap = originalOverlap + 0.25f;
            settings.DownbeatPositionOffset = originalDownbeat + 0.125f;
            await Settle(runner);
            AssertMinimapMatchesRow(container, scrollbar);
            settings.NumberOfRows = 3;
            margin.CustomMinimumSize = new Vector2(180, originalMinimum.Y);
            scrollbar.Value = scrollbar.MinValue + 2;
            await Settle(runner);
            AssertMinimapMatchesRow(container, scrollbar);
            await Capture(runner, scrollbar.GetViewport(), "scrollbar-minimap.png");
            AssertThat(Project.Instance.SpectrogramContext.CachedSpectrogramImage).IsSame(source);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0]).IsSame(fft);
        }
        finally
        {
            Project.Instance.Timing.TrySetBpm(lastPoint, originalBpm);
            settings.MeasureOverlap = originalOverlap;
            settings.DownbeatPositionOffset = originalDownbeat;
            settings.NumberOfRows = originalRows;
            margin.CustomMinimumSize = originalMinimum;
            await Settle(runner);
        }
    }

    private static async Task VerifySongEdgesInMinimap(ISceneRunner runner, AudioVisualsContainer container, BlockScrollBar scrollbar)
    {
        Timing original = Project.Instance.Timing;
        Settings settings = Project.Instance.Settings;
        int originalRows = settings.NumberOfRows;
        float originalOverlap = settings.MeasureOverlap;
        float originalDownbeat = settings.DownbeatPositionOffset;
        var timing = new Timing(Project.Instance.Settings) { IsInstantiating = true };
        timing.AddTimingPoint(0d, 1d, 0.5d);
        timing.IsInstantiating = false;
        try
        {
            Project.Instance.ReplaceDocument(timing, Project.Instance.AudioFile, Project.Instance.ProjectPath);
            await Settle(runner);
            scrollbar.Value = scrollbar.MinValue;
            await Settle(runner);
            AssertThat(container.AudioBlocks[0].NominalMeasurePosition).IsEqual(-1);
            AssertMinimapMatchesRow(container, scrollbar);

            settings.NumberOfRows = 1;
            settings.MeasureOverlap = 0;
            settings.DownbeatPositionOffset = 0.125f;
            float audioLength = Project.Instance.AudioFile.GetAudioLength();
            timing = new Timing(settings) { IsInstantiating = true };
            timing.AddTimingPoint(0d, 0d, 10.95d / audioLength);
            timing.IsInstantiating = false;
            Project.Instance.ReplaceDocument(timing, Project.Instance.AudioFile, Project.Instance.ProjectPath);
            scrollbar.Value = scrollbar.MaxValue;
            await Settle(runner);
            AssertThat(container.AudioBlocks[0].NominalMeasurePosition).IsEqual(11);
            AssertMinimapMatchesRow(container, scrollbar);
            var lastSegment = VisibleAudioSegments(container.AudioBlocks[0].AudioDisplayPanel).OfType<IAudioSegmentDisplay>().Last();
            AssertThat(lastSegment.TimeRange[1]).IsEqual(audioLength);

            settings.DownbeatPositionOffset = 0;
            await Settle(runner);
            AssertThat(scrollbar.MaxValue).IsEqual(11d);
            AssertThat(container.AudioBlocks[0].NominalMeasurePosition).IsEqual(10);
            AssertMinimapMatchesRow(container, scrollbar);
        }
        finally
        {
            settings.NumberOfRows = originalRows;
            settings.MeasureOverlap = originalOverlap;
            settings.DownbeatPositionOffset = originalDownbeat;
            Project.Instance.ReplaceDocument(original, Project.Instance.AudioFile, Project.Instance.ProjectPath);
            await Settle(runner);
        }
    }

    private static void AssertMinimapMatchesRow(AudioVisualsContainer container, BlockScrollBar scrollbar)
    {
        AudioBlock row = container.AudioBlocks[0];
        AudioDisplayPanel panel = row.AudioDisplayPanel;
        var context = Project.Instance.SpectrogramContext;
        using Image minimap = ((StyleBoxTexture)scrollbar.GetThemeStylebox("scroll")).Texture.GetImage();
        double rowHeight = minimap.GetHeight() / (scrollbar.MaxValue - scrollbar.MinValue);
        int top = (int)System.Math.Round((row.NominalMeasurePosition - scrollbar.MinValue) * rowHeight);
        double contentFraction = row.Size.Y / (row.Size.Y + container.GetThemeConstant("separation"));
        int bottom = (int)System.Math.Round((row.NominalMeasurePosition - scrollbar.MinValue + contentFraction) * rowHeight);
        using var expectedRow = Image.CreateEmpty(minimap.GetWidth(), bottom - top, false, Image.Format.Rgba8);
        foreach (var segment in VisibleAudioSegments(panel).OfType<IAudioSegmentDisplay>())
        {
            float startMeasure = (float)Project.Instance.Timing.OffsetToMeasurePosition(segment.TimeRange[0]);
            float endMeasure = (float)Project.Instance.Timing.OffsetToMeasurePosition(segment.TimeRange[1]);
            int left = (int)System.Math.Round(panel.MeasurePositionToXPosition(startMeasure) / panel.Size.X * minimap.GetWidth());
            int right = (int)System.Math.Round(panel.MeasurePositionToXPosition(endMeasure) / panel.Size.X * minimap.GetWidth());
            if (right <= left)
                continue;
            using Image pixels = context.GetSpectrogramImageSlice(
                Project.Instance.AudioFile.SampleTimeToSampleIndex(segment.TimeRange[0]),
                Project.Instance.AudioFile.SampleTimeToSampleIndex(segment.TimeRange[1]), bottom - top, right - left);
            expectedRow.BlitRect(pixels, new Rect2I(Vector2I.Zero, pixels.GetSize()), new Vector2I(left, 0));
        }
        using Image actualRow = minimap.GetRegion(new Rect2I(0, top, minimap.GetWidth(), bottom - top));
        AssertThat(actualRow.GetData()).IsEqual(expectedRow.GetData());
    }

    private static async Task VerifyScrollbarWheel(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container, BlockScrollBar scrollbar)
    {
        var previous = new VScrollBar { Size = new Vector2(32, 100) };
        viewport.AddChild(previous);
        try
        {
            foreach (int rows in new[] { 1, 10 })
            {
                Project.Instance.Settings.NumberOfRows = rows;
                await runner.AwaitIdleFrame();
                previous.MinValue = container.FirstTopMeasure;
                previous.MaxValue = container.LastTopMeasure;
                previous.Value = scrollbar.Value = previous.MinValue;

                foreach (var (direction, factor) in new[] { (MouseButton.WheelDown, 1f), (MouseButton.WheelDown, 0.5f), (MouseButton.WheelUp, 2f), (MouseButton.WheelDown, 100f) })
                {
                    Wheel(viewport, previous.GetGlobalRect().GetCenter(), direction, factor);
                    Wheel(viewport, scrollbar.GetGlobalRect().GetCenter(), direction, factor);
                    await runner.AwaitInputProcessed();
                    AssertThat(scrollbar.Value).IsEqualApprox(previous.Value, 1e-8);
                    AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual((int)previous.Value);
                }
            }
        }
        finally
        {
            previous.Free();
        }
    }

    private static async Task VerifySongEndScrolling(ISceneRunner runner, SubViewport viewport, AudioBlock lastRow, Vector2 cursor)
    {
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        scrollbar.Value = scrollbar.MaxValue;
        await runner.AwaitIdleFrame();
        AssertThat(lastRow.NominalMeasurePosition).IsEqual(lastRow.GetParent<AudioVisualsContainer>().LastRowMeasure);

        Wheel(viewport, cursor, MouseButton.WheelUp);
        Pan(viewport, cursor, 1);
        await runner.AwaitInputProcessed();
        AssertThat(lastRow.NominalMeasurePosition).IsEqual(lastRow.GetParent<AudioVisualsContainer>().LastRowMeasure);
    }

    private static IEnumerable<VisualTimingPoint> VisibleTimingPoints(AudioDisplayPanel panel)
        => panel.GetNode<Node2D>("VisualTimingPointFolder").GetChildren().OfType<VisualTimingPoint>().Where(point => point.Visible);

    private static void AssertReadable(AudioDisplayPanel panel, VisualTimingPoint point)
    {
        Label time = panel.GetNode<PreviewLine>("PreviewLine").TimeLabel;
        AssertThat(time.IsVisibleInTree()).IsTrue();
        AssertThat(point.BpmLabel.IsVisibleInTree()).IsTrue();
        AssertThat(point.BpmLabel.GetGlobalRect().GetCenter().X).IsEqualApprox(point.GlobalPosition.X, 0.01f);
        AssertThat(time.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        AssertThat(TextBounds(time).Position.Y + 0.01f).IsGreaterEqual(TextBounds(point.BpmLabel).End.Y);
        AssertThat(TextBounds(time).End.Y).IsLessEqual(panel.GetGlobalRect().End.Y);
        AssertPointerGapIsHalved(point.BpmLabel, time);
        foreach (CanvasItem line in new CanvasItem[] { point.OffsetLine, panel.Playhead, panel.GetNode<PreviewLine>("PreviewLine"), panel.GetNode<Line2D>("SelectedPositionLine") })
        {
            AssertThat(DrawOrder(time)).IsGreater(DrawOrder(line));
            AssertThat(DrawOrder(point.BpmLabel)).IsGreater(DrawOrder(line));
        }
    }

    private static void AssertPointerGapIsHalved(Label bpm, Label time)
    {
        var bpmDigits = NumericTextMetrics.GetDigitBounds(bpm);
        var timeDigits = NumericTextMetrics.GetDigitBounds(time);
        float outlines = (bpm.GetThemeConstant("outline_size") * bpm.Scale.Y + time.GetThemeConstant("outline_size")) / 2f;
        float oldGap = System.Math.Max(26, bpm.Size.Y * bpm.Scale.Y + outlines) + timeDigits.Top - bpmDigits.Bottom;
        float gap = time.GlobalPosition.Y + timeDigits.Top - bpm.GlobalPosition.Y - bpmDigits.Bottom;
        AssertThat(gap).IsEqualApprox(oldGap / 2, 0.01f);
    }

    private static void AssertBpmBetweenRows(AudioDisplayPanel upper, AudioDisplayPanel lower, VisualTimingPoint point)
    {
        float middle = (upper.GetGlobalRect().End.Y + lower.GlobalPosition.Y) / 2;
        AssertThat(TextBounds(point.BpmLabel).Position.Y).IsEqualApprox(middle, 0.01f);
    }

    private static Rect2 TextBounds(Label label)
    {
        var digits = NumericTextMetrics.GetDigitBounds(label);
        Rect2 rect = label.GetGlobalRect();
        rect.Position += new Vector2(0, digits.Top);
        rect.Size = new Vector2(rect.Size.X, digits.Bottom - digits.Top);
        return rect.Grow(label.GetThemeConstant("outline_size") * label.Scale.Y / 2);
    }

    private static int DrawOrder(CanvasItem item)
        => item.ZIndex + (item.ZAsRelative && item.GetParent() is CanvasItem parent ? DrawOrder(parent) : 0);
}
