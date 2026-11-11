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
            viewport.AddChild(ResourceLoader.Load<PackedScene>("res://Classes/Visual/Main.tscn").Instantiate());
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
        int originalRows = Settings.Instance.NumberOfRows;
        Vector2I originalSize = viewport.Size;
        Timing originalTiming = Timing.Instance;
        Timing timing = AutoFree(new Timing { IsInstantiating = true })!;
        Timing.Instance = timing;

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

            foreach (int rows in new[] { 10, 30, 1 })
            {
                Settings.Instance.NumberOfRows = rows;
                foreach (int height in new[] { 720, 480, 471, 1080 })
                    await VerifyPointerTimeAtHeight(runner, viewport, container, height);
                AssertThat(Settings.Instance.NumberOfRows).IsEqual(rows);
            }
            await VerifyBpmEditing(runner, viewport, container);
        }
        finally
        {
            MusicPlayer.Instance.Pause();
            Context.Instance.HeldTimingPoint = null;
            Context.Instance.TimingPointNearestCursor = null;
            TimingPointSelection.Instance.DeselectAll();
            Timing.Instance = originalTiming;
            Settings.Instance.NumberOfRows = originalRows;
            viewport.Size = originalSize;
        }
    }

    private static async Task VerifyBpmEditing(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).Value = 13;
        await runner.AwaitIdleFrame();
        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        var point = VisibleTimingPoints(panel).Last();
        int count = Timing.Instance.TimingPoints.Count;

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
        await ClickTimeSignatureButton(runner, viewport, container.AudioBlocks[0]);
        await runner.AwaitInputProcessed();
        AssertThat(point.BpmEdit.Visible).IsFalse();
        AssertThat(point.BpmLabel.Visible).IsTrue();
        AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(Colors.White);
        AssertThat(Timing.Instance.TimingPoints.Count).IsEqual(count);
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
        SendInput(viewport, new InputEventMouseMotion { Position = start });
        SendInput(viewport, new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseMotion { Position = end, Relative = end - start, ButtonMask = MouseButtonMask.Left });
        SendInput(viewport, new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left });
        await runner.AwaitInputProcessed();
    }

    private static async Task ClickTimeSignatureButton(ISceneRunner runner, SubViewport viewport, AudioBlock row)
    {
        var stepper = row.GetNode<Stepper>("BlockHBoxContainer/LabelHBoxContainer/TimeSignatureStepper/NumeratorStepper");
        SendInput(viewport, new InputEventMouseMotion { Position = stepper.ValueLabel.GetGlobalRect().GetCenter() });
        await runner.AwaitInputProcessed();
        var button = stepper.GetNode<Button>("VBoxContainer/IncrementButton");
        Vector2 position = button.GetGlobalRect().GetCenter();
        SendInput(viewport, new InputEventMouseMotion { Position = position });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left });
    }

    private static void DoubleClickBpm(SubViewport viewport, VisualTimingPoint point)
    {
        Vector2 position = point.BpmLabel.GetGlobalRect().GetCenter();
        SendInput(viewport, new InputEventMouseMotion { Position = position });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true });
        SendInput(viewport, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left });
    }

    private static async Task VerifyRestoredRows(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        Settings.Instance.NumberOfRows = 10;
        scrollbar.Value = 0;
        viewport.Size = new Vector2I(1280, 720);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        viewport.Size = new Vector2I(1280, 471);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(container.AudioBlocks[8].Visible).IsFalse();
        scrollbar.Value = 5;

        viewport.Size = new Vector2I(1280, 720);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AudioDisplayPanel restored = container.AudioBlocks[8].AudioDisplayPanel;
        IAudioSegmentDisplay waveform = restored.GetNode<Node2D>("AudioSegments").GetChildren().OfType<IAudioSegmentDisplay>().First(segment => segment.Visible);
        AssertThat(waveform.TimeRange[0]).IsEqualApprox((float)Timing.Instance.MeasurePositionToOffset(restored.ActualMeasurePositionStartForPanel), 0.001f);
        AssertThat(VisibleTimingPoints(restored).Last().TimingPoint.MeasurePosition!.Value).IsEqual(13d);
    }

    private static async Task VerifyPointerTimeAtHeight(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container, int height)
    {
        viewport.Size = new Vector2I(1280, height);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(container.NominalMeasurePositionStartForTopBlock).IsLessEqual(container.LastTopMeasure);
        ((BlockScrollBar)runner.FindChild("BlockScrollBar")!).Value = 0;

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

        if (height == 471 && Settings.Instance.NumberOfRows == 30)
        {
            await VerifyRejectedDrag(runner, viewport, panel, cursor);
            AssertThat(point.OffsetLine.DefaultColor).IsEqual(Colors.Red);
            AssertThat(point.BpmLabel.GetThemeColor("font_color")).IsEqual(Colors.Red);
            AssertReadable(panel, point);

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
            AssertThat(TextBounds(panel.GetNode<PreviewLine>("PreviewLine").TimeLabel).End.Y).IsLessEqual(TextBounds(nextPoint.BpmLabel).Position.Y);
        }
        AssertThat(visible[^1].GetGlobalRect().End.Y).IsLessEqual(container.GetGlobalRect().End.Y + 1);
        await VerifySongEndScrolling(runner, viewport, visible[^1], cursor);
    }

    private static async Task VerifyRejectedDrag(ISceneRunner runner, SubViewport viewport, AudioDisplayPanel panel, Vector2 cursor)
    {
        PressKey(viewport, Key.Escape, true);
        PressKey(viewport, Key.Escape, false);
        TimingPoint movingPoint = Timing.Instance.TimingPoints[1];
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

    private static async Task VerifySongEndScrolling(ISceneRunner runner, SubViewport viewport, AudioBlock lastRow, Vector2 cursor)
    {
        var scrollbar = (BlockScrollBar)runner.FindChild("BlockScrollBar")!;
        scrollbar.Value = scrollbar.MaxValue;
        await runner.AwaitIdleFrame();
        AssertThat(lastRow.NominalMeasurePosition).IsEqual(Timing.Instance.GetLastMeasure());

        Wheel(viewport, cursor, MouseButton.WheelUp);
        Pan(viewport, cursor, 1);
        await runner.AwaitInputProcessed();
        AssertThat(lastRow.NominalMeasurePosition).IsEqual(Timing.Instance.GetLastMeasure());
    }

    private static IEnumerable<VisualTimingPoint> VisibleTimingPoints(AudioDisplayPanel panel)
        => panel.GetNode<Node2D>("VisualTimingPointFolder").GetChildren().OfType<VisualTimingPoint>().Where(point => point.Visible);

    private static void AssertReadable(AudioDisplayPanel panel, VisualTimingPoint point)
    {
        Label time = panel.GetNode<PreviewLine>("PreviewLine").TimeLabel;
        AssertThat(time.IsVisibleInTree()).IsTrue();
        AssertThat(point.BpmLabel.IsVisibleInTree()).IsTrue();
        AssertThat(point.BpmLabel.GetGlobalRect().GetCenter().X).IsEqualApprox(point.GlobalPosition.X, 0.01f);
        AssertThat(point.BpmLabel.GetGlobalRect().Position.Y + 0.01f).IsGreaterEqual(panel.GlobalPosition.Y);
        AssertThat(time.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        AssertThat(TextBounds(time).Position.Y + 0.01f).IsGreaterEqual(TextBounds(point.BpmLabel).End.Y);
        AssertThat(TextBounds(time).End.Y).IsLessEqual(panel.GetGlobalRect().End.Y);
        foreach (CanvasItem line in new CanvasItem[] { point.OffsetLine, panel.Playhead, panel.GetNode<PreviewLine>("PreviewLine"), panel.GetNode<Line2D>("SelectedPositionLine") })
        {
            AssertThat(DrawOrder(time)).IsGreater(DrawOrder(line));
            AssertThat(DrawOrder(point.BpmLabel)).IsGreater(DrawOrder(line));
        }
    }

    private static Rect2 TextBounds(Label label)
        => label.GetGlobalRect().Grow(label.GetThemeConstant("outline_size") * label.Scale.Y / 2);

    private static int DrawOrder(CanvasItem item)
        => item.ZIndex + (item.ZAsRelative && item.GetParent() is CanvasItem parent ? DrawOrder(parent) : 0);
}