namespace Tempora.Tests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingShortcutTests
{
    [TestCase]
    public async Task Ctrl_and_Shift_edit_timing_with_wheel_drag_and_trackpad_while_Alt_enlarges_changes_and_navigation()
    {
        // A sized viewport supplies cursor positions even when Godot runs headlessly.
        var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
        viewport.AddChild(ResourceLoader.Load<PackedScene>("res://Classes/Visual/Main.tscn").Instantiate());
        using ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        var container = (AudioVisualsContainer)runner.FindChild("AudioVisualsContainer")!;
        await runner.AwaitIdleFrame();

        bool roundBpm = Settings.Instance.RoundBPM;
        int rows = Settings.Instance.NumberOfRows;
        AudioFile originalAudio = Project.Instance.AudioFile;
        Timing originalTiming = Timing.Instance;
        Timing timing = AutoFree(new Timing { IsInstantiating = true })!;
        Timing.Instance = timing;
        Settings.Instance.RoundBPM = false;
        timing.AddTimingPoint(0d, 0d, 0.5d);
        timing.AddTimingPoint(0.35d, 0.7d, 0.5d);
        TimingPoint point = timing.TimingPoints[1];
        MementoHandler.Instance.ResetTimingHistory();
        container.UpdateBlocksScroll();
        await runner.AwaitIdleFrame();
        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        panel.UpdateTimingPointsIndices();
        panel.UpdateVisuals();
        Vector2 cursor = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0.35f), panel.Size.Y / 2);
        SendInput(viewport, new InputEventMouseMotion { Position = cursor });
        await runner.AwaitInputProcessed();

        try
        {
            PressKey(viewport, Key.Ctrl, true);
            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(121d, 1e-6);
            AssertThat(point.Offset).IsEqual(0.7d);

            PressKey(viewport, Key.Alt, true);
            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(126d, 1e-6);

            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, true);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(125.9d, 1e-6);
            PressKey(viewport, Key.Ctrl, false);
            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(0.702d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqual(0d);
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);

            PressKey(viewport, Key.Alt, true);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(0.692d, 1e-8);
            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, false);

            // Holding the existing point follows the same Ctrl/Shift roles as the wheel.
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left, Pressed = true });
            PressKey(viewport, Key.Ctrl, true);
            await runner.AwaitInputProcessed();
            AssertThat(Context.Instance.HeldTimingPoint).IsEqual(point);
            double bpmBeforeDrag = point.Bpm;
            cursor += new Vector2(20, 0);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor, Relative = new Vector2(20, 0) });
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsGreater(bpmBeforeDrag);
            AssertThat(point.Offset).IsEqualApprox(0.692d, 1e-8);
            PressKey(viewport, Key.Ctrl, false);
            PressKey(viewport, Key.Shift, true);
            bpmBeforeDrag = point.Bpm;
            cursor += new Vector2(20, 0);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor, Relative = new Vector2(20, 0) });
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsLess(0.692d);
            AssertThat(point.Bpm).IsEqualApprox(bpmBeforeDrag, 1e-6);
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left });
            PressKey(viewport, Key.Shift, false);
            await runner.AwaitInputProcessed();

            // Trackpad scrolling uses the same roles and wheel direction.
            PressKey(viewport, Key.Ctrl, true);
            bpmBeforeDrag = point.Bpm;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox((int)bpmBeforeDrag + 1d, 1e-6);
            PressKey(viewport, Key.Alt, true);
            Pan(viewport, cursor, 1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox((int)bpmBeforeDrag - 4d, 1e-6);
            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, true);
            double bpmBeforePan = point.Bpm;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(bpmBeforePan + 0.1d, 1e-6);

            // Selecting both points retains batch offset adjustment.
            PressKey(viewport, Key.A, true);
            PressKey(viewport, Key.A, false);
            PressKey(viewport, Key.Ctrl, false);
            double offsetBeforePan = point.Offset;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(offsetBeforePan + 0.002d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqualApprox(0.002d, 1e-8);
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);
            PressKey(viewport, Key.Alt, true);
            Pan(viewport, cursor, 1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(offsetBeforePan - 0.008d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqualApprox(-0.008d, 1e-8);
            PressKey(viewport, Key.Shift, false);

            // A long bundled song keeps both scroll sizes away from the timeline ends.
            Project.Instance.AudioFile = new AudioFile(ResourceLoader.Load<AudioStreamMP3>("res://Audio/Loop.mp3"));
            Settings.Instance.NumberOfRows = 1;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
            cursor = panel.GlobalPosition + new Vector2(panel.Size.X / 2, panel.Size.Y / 2);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor });
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(5);
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);
            PressKey(viewport, Key.Alt, false);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(1);
        }
        finally
        {
            PressKey(viewport, Key.Ctrl, false);
            PressKey(viewport, Key.Shift, false);
            PressKey(viewport, Key.Alt, false);
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left });
            Context.Instance.HeldTimingPoint = null;
            TimingPointSelection.Instance.DeselectAll();
            Timing.Instance = originalTiming;
            Settings.Instance.RoundBPM = roundBpm;
            Settings.Instance.NumberOfRows = rows;
            Project.Instance.AudioFile = originalAudio;
        }
    }

    private static void PressKey(SubViewport viewport, Key key, bool pressed)
        => SendInput(viewport, new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private static void Wheel(SubViewport viewport, Vector2 cursor, MouseButton direction)
        => SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = direction, Pressed = true });

    private static void Pan(SubViewport viewport, Vector2 cursor, float amount)
        => SendInput(viewport, new InputEventPanGesture { Position = cursor, Delta = new Vector2(0, amount) });

    private static void SendInput(SubViewport viewport, InputEvent inputEvent)
    {
        // Global key state and viewport dispatch are separate native input boundaries.
        Input.ParseInputEvent(inputEvent);
        Input.FlushBufferedEvents();
        viewport.PushInput(inputEvent);
    }
}