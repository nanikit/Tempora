namespace Tempora.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

public partial class TimingShortcutTests
{
    private static async Task VerifyAudioPanelRendering(ISceneRunner runner, SubViewport viewport, AudioVisualsContainer container)
    {
        Settings settings = Project.Instance.Settings;
        float overlap = settings.MeasureOverlap;
        float downbeat = settings.DownbeatPositionOffset;
        bool spectrum = settings.RenderAsSpectrogram;
        int divisor = settings.GridDivisor;
        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        Vector2 cursor = panel.GetGlobalRect().GetCenter();
        Wheel(viewport, cursor, MouseButton.WheelUp);
        await runner.AwaitInputProcessed();
        try
        {
            settings.MeasureOverlap = 0.25f;
            settings.DownbeatPositionOffset = 0.125f;
            settings.GridDivisor = 4;
            await runner.AwaitIdleFrame();
            float[] gridPositions = panel.GetNode<Node2D>("GridFolder").GetChildren().OfType<Node2D>().Select(line => line.Position.X).ToArray();
            float selectionStart = gridPositions.Single(x => System.Math.Abs(x - panel.MeasurePositionToXPosition(0.25f)) < 0.001f);
            float selectionEnd = gridPositions.Single(x => System.Math.Abs(x - panel.MeasurePositionToXPosition(0.75f)) < 0.001f);
            Vector2 start = panel.GlobalPosition + new Vector2(selectionStart, panel.Size.Y / 2);
            cursor = panel.GlobalPosition + new Vector2(selectionEnd, panel.Size.Y / 2);
            PressKey(viewport, Key.Escape, true);
            PressKey(viewport, Key.Escape, false);
            SendInput(viewport, new InputEventMouseMotion { Position = start });
            await runner.AwaitInputProcessed();
            PressKey(viewport, Key.Alt, true);
            PressKey(viewport, Key.Shift, true);
            SendInput(viewport, new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
            SendInput(viewport, new InputEventMouseMotion { Position = cursor, Relative = cursor - start, ButtonMask = MouseButtonMask.Left });
            await runner.AwaitInputProcessed();

            foreach (bool renderSpectrum in new[] { false, true })
            {
                settings.RenderAsSpectrogram = renderSpectrum;
                panel.UpdateVisuals();
                await runner.AwaitIdleFrame();
                Node2D[] segments = VisibleAudioSegments(panel);
                AssertThat(segments.Length).IsEqual(2);
                float boundaryX = panel.MeasurePositionToXPosition((float)Project.Instance.Timing.TimingPoints[1].MeasurePosition!.Value);
                float startX = segments[1] is SpectrogramSegment spectrogram
                    ? spectrogram.Position.X - spectrogram.Width / 2
                    : segments[1].Position.X;
                AssertThat(startX).IsEqualApprox(boundaryX, 0.001f);
                var selector = panel.GetNode<ColorRect>("VisualSelector");
                AssertThat(selector.Visible).IsTrue();
                AssertThat(selector.Position.X).IsEqualApprox(selectionStart, 0.001f);
                AssertThat(selector.GetRect().End.X).IsEqualApprox(selectionEnd, 0.001f);

                panel.UpdateVisuals();
                await runner.AwaitIdleFrame();
                AssertThat(VisibleAudioSegments(panel)).IsEqual(segments);

                panel.NominalMeasurePosition = 10000;
                await runner.AwaitIdleFrame();
                AssertThat(panel.GetNode<Node2D>("AudioSegments").GetChildCount()).IsEqual(0);
                AssertThat(segments.All(node => !GodotObject.IsInstanceValid(node))).IsTrue();
                panel.NominalMeasurePosition = container.AudioBlocks[0].NominalMeasurePosition;
                await runner.AwaitIdleFrame();
                AssertThat(VisibleAudioSegments(panel).Length).IsEqual(2);
            }

            await VerifyHiddenRowsReleaseAudio(runner, container);
        }
        finally
        {
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left });
            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, false);
            PressKey(viewport, Key.Escape, true);
            PressKey(viewport, Key.Escape, false);
            settings.MeasureOverlap = overlap;
            settings.DownbeatPositionOffset = downbeat;
            settings.RenderAsSpectrogram = spectrum;
            settings.GridDivisor = divisor;
            Wheel(viewport, panel.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
        }
    }

    private static async Task VerifyHiddenRowsReleaseAudio(ISceneRunner runner, AudioVisualsContainer container)
    {
        var project = Project.Instance;
        int rows = project.Settings.NumberOfRows;
        AudioFile audio = project.AudioFile;
        try
        {
            project.Settings.NumberOfRows = 2;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
            var panel = container.AudioBlocks[1].AudioDisplayPanel;
            var segments = VisibleAudioSegments(panel);
            AssertThat(segments.Length).IsGreater(0);
            var textures = segments.OfType<SpectrogramSegment>().Select(segment => segment.Texture).ToArray();

            project.Settings.NumberOfRows = 1;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
            AssertThat(panel.Visible).IsFalse();
            AssertThat(panel.GetNode<Node2D>("AudioSegments").GetChildCount()).IsEqual(0);
            AssertThat(segments.All(node => !GodotObject.IsInstanceValid(node))).IsTrue();
            AssertThat(textures.All(texture => !GodotObject.IsInstanceValid(texture))).IsTrue();

            project.AudioFile = new AudioFile(ResourceLoader.Load<AudioStreamMP3>("res://Audio/Loop.mp3"));
            AssertThat(panel.GetNode<Node2D>("AudioSegments").GetChildCount()).IsEqual(0);
            project.Settings.NumberOfRows = 2;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
            var restored = VisibleAudioSegments(panel);
            AssertThat(restored.Length).IsGreater(0);
            AssertThat(restored.Cast<IAudioSegmentDisplay>().All(segment => ReferenceEquals(segment.AudioFile, project.AudioFile))).IsTrue();
        }
        finally
        {
            project.AudioFile = audio;
            project.Settings.NumberOfRows = rows;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
        }
    }

    private static Node2D[] VisibleAudioSegments(AudioDisplayPanel panel)
        => panel.GetNode<Node2D>("AudioSegments").GetChildren().OfType<Node2D>().Where(node => node.Visible).ToArray();
}