namespace Tempora.Tests;

using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

public partial class TimingShortcutTests
{
    private static async Task VerifySpectrumPreviewReusesCalculations(ISceneRunner runner)
    {
        var window = (Window)runner.FindChild("VisualSettingsWindow")!;
        var intensity = window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/Intensity/LineEdit");
        var decibels = window.GetNode<CheckBox>("Margin/Scroll/Layout/TabContainer/Spectrogram/dB/CheckBox");
        var project = Project.Instance;
        var originalAudio = project.AudioFile;
        var original = VisualSettings.From(project.Settings);
        var context = project.SpectrogramContext;
        var spectrum = context.SpectrogramGenerator;
        var fft = spectrum.GetFFTs()[0];
        byte[] pixels = context.CachedSpectrogramImage!.GetData();
        var image = context.CachedSpectrogramImage;
        try
        {
            window.Popup();
            window.GetNode<TabContainer>("Margin/Scroll/Layout/TabContainer").CurrentTab = 1;
            Edit(intensity, "0");
            await runner.AwaitMillis(350);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
            AssertThat(spectrum.GetFFTs()[0]).IsSame(fft);
            AssertThat(GodotObject.IsInstanceValid(image)).IsFalse();
            AssertThat(Project.Instance.SpectrogramContext.CachedSpectrogramImage!.GetData().SequenceEqual(pixels)).IsFalse();
            image = project.SpectrogramContext.CachedSpectrogramImage;
            project.UpdateSpectrogramContextFromSettings(original with { Intensity = 0 });
            AssertThat(project.SpectrogramContext.CachedSpectrogramImage).IsSame(image);

            Edit(intensity, "6");
            await runner.AwaitMillis(350);
            byte[] brightPixels = Project.Instance.SpectrogramContext.CachedSpectrogramImage!.GetData();
            decibels.ButtonPressed = !decibels.ButtonPressed;
            await runner.AwaitMillis(350);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0]).IsSame(fft);
            AssertThat(Project.Instance.SpectrogramContext.CachedSpectrogramImage!.GetData().SequenceEqual(brightPixels)).IsFalse();

            window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/CancelButton").EmitSignal(Button.SignalName.Pressed);
            await runner.AwaitMillis(350);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0]).IsSame(fft);
            AssertThat(Project.Instance.SpectrogramContext.CachedSpectrogramImage!.GetData()).IsEqual(pixels);

            VerifySpectrumDetailChanges(original);
            project.UpdateSpectrogramContextFromSettings();
            fft = project.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0];
            image = project.SpectrogramContext.CachedSpectrogramImage;
            project.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/clickedy.mp3"));
            AssertThat(project.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0]).IsNotSame(fft);
            AssertThat(GodotObject.IsInstanceValid(image)).IsFalse();
        }
        finally
        {
            window.Hide();
            project.AudioFile = originalAudio;
            Project.Instance.UpdateSpectrogramContextFromSettings();
            await Settle(runner);
        }
    }

    private static void VerifySpectrumDetailChanges(VisualSettings original)
    {
        var project = Project.Instance;
        foreach (var values in new[]
        {
            original with { StepSize = original.StepSize * 2 },
            original with { FftSize = original.FftSize * 2 },
            original with { MinFreq = original.MinFreq + 1 },
        })
        {
            project.UpdateSpectrogramContextFromSettings(original);
            var fft = project.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0];
            project.UpdateSpectrogramContextFromSettings(values);
            AssertThat(project.SpectrogramContext.SpectrogramGenerator.GetFFTs()[0]).IsNotSame(fft);
        }
    }
}