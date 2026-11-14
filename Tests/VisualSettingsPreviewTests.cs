namespace Tempora.Tests;

using System.Threading.Tasks;
using System.Linq;
using GdUnit4;
using Godot;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

public partial class TimingShortcutTests
{
    private static async Task VerifyVisualSettingsPreview(ISceneRunner runner)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var application = root.GetNode<Application>("Application");
        if (application.RejectedVisualSettings.HasValue)
            await VerifyRejectedStartupSettings(runner, application);
        await VerifyScaleDragKeepsWindowInPlace(runner);
        await VerifySpectrumPreviewReusesCalculations(runner);
        var window = (Window)runner.FindChild("VisualSettingsWindow")!;
        var tabs = window.GetNode<TabContainer>("Margin/Scroll/Layout/TabContainer");
        var slider = window.GetNode<HSlider>("Margin/Scroll/Layout/TabContainer/Interface/Scale/Slider");
        var percentage = (SpinBox)window.FindChild("Percentage");
        var save = window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/SaveButton");
        var cancel = window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/CancelButton");
        var reset = window.GetNode<Button>("Margin/Scroll/Layout/TabContainer/Interface/DefaultsButton");
        var frequency = window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/minFreq/LineEdit");
        var fft = window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/fftSize/LineEdit");
        var step = window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/stepSize/LineEdit");
        var intensity = window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/Intensity/LineEdit");
        var decibels = window.GetNode<CheckBox>("Margin/Scroll/Layout/TabContainer/Spectrogram/dB/CheckBox");
        var volume = (HScrollBar)runner.FindChild("VolumeSliderMusic")!;
        VisualSettings original = VisualSettings.From(Project.Instance.Settings);
        double originalVolume = volume.Value;
        double originalSystemScale = application.SystemTextScale;
        try
        {
            tabs.CurrentTab = 0;
            window.Popup();
            var spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
            slider.Value = 150;
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual(1.5f);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);

            volume.Value = originalVolume == 0.4 ? 0.6 : 0.4;
            Settings persisted = new SettingsStore().Load();
            AssertThat(VisualSettings.From(persisted)).IsEqual(original);
            AssertThat(persisted.MusicVolumeNormalized).IsEqual(volume.Value);
            AssertThat(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music"))))
                .IsEqualApprox((float)volume.Value, 0.0001f);
            application.ApplySystemTextScale(1.5);
            AssertThat(root.ContentScaleFactor).IsEqual(1.5f);
            application.ApplySystemTextScale(originalSystemScale);
            cancel.EmitSignal(Button.SignalName.Pressed);
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual((float)original.TextScaleFactor);
            AssertThat(Project.Instance.Settings.MusicVolumeNormalized).IsEqual(volume.Value);

            window.Popup();
            Edit(percentage.GetLineEdit(), "200");
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual(2f);
            save.EmitSignal(Button.SignalName.Pressed);
            AssertThat(window.Visible).IsFalse();
            AssertThat(new SettingsStore().Load().TextScaleFactor).IsEqual(2d);

            window.Popup();
            reset.EmitSignal(Button.SignalName.Pressed);
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual(1f);
            window.EmitSignal(Window.SignalName.CloseRequested);
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual(2f);

            window.Popup();
            slider.Value = 120;
            cancel.EmitSignal(Button.SignalName.Pressed);
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual(2f);

            window.Popup();
            tabs.CurrentTab = 1;
            Edit(step, "1");
            await runner.AwaitMillis(350);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
            AssertThat(window.GetNode<Label>("Margin/Scroll/Layout/PreviewStatus").Visible).IsTrue();
            AssertThat(save.Disabled).IsTrue();
            save.EmitSignal(Button.SignalName.Pressed);
            AssertThat(window.Visible).IsTrue();
            AssertThat(new SettingsStore().Load().SpectrogramStepSize).IsEqual(original.StepSize);

            Edit(fft, "512");
            Edit(step, "128");
            Edit(intensity, "6");
            decibels.ButtonPressed = !original.UseDb;
            Edit(frequency, (original.MinFreq + 500).ToString());
            Edit(frequency, (original.MinFreq + 1000).ToString());
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
            await runner.AwaitMillis(350);
            spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
            AssertThat(spectrum.FftSize).IsEqual(512);
            AssertThat(spectrum.StepSize).IsEqual(128);
            AssertThat(Project.Instance.SpectrogramContext.Intensity).IsEqual(6);
            AssertThat(Project.Instance.SpectrogramContext.DB).IsEqual(!original.UseDb);
            AssertThat(spectrum.FreqMin).IsBetween(original.MinFreq + 1000 - spectrum.HzPerPx, original.MinFreq + 1000d);
            AssertThat(new SettingsStore().Load().SpectrogramMinFreq).IsEqual(original.MinFreq);

            Edit(fft, "300");
            await runner.AwaitMillis(250);
            AssertThat(save.Disabled).IsTrue();
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
            Edit(fft, original.FftSize.ToString());
            Edit(frequency, (original.MinFreq + 2000).ToString());
            cancel.EmitSignal(Button.SignalName.Pressed);
            await runner.AwaitMillis(350);
            spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
            AssertThat(spectrum.FreqMin).IsBetween(original.MinFreq - spectrum.HzPerPx, (double)original.MinFreq);
            AssertThat(spectrum.FftSize).IsEqual(original.FftSize);
            AssertThat(Project.Instance.SpectrogramContext.DB).IsEqual(original.UseDb);

            window.Popup();
            AssertThat(frequency.Text).IsEqual(original.MinFreq.ToString());
            Edit(frequency, (original.MinFreq + 1000).ToString());
            save.EmitSignal(Button.SignalName.Pressed);
            spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
            await runner.AwaitMillis(350);
            AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
            AssertThat(new SettingsStore().Load().SpectrogramMinFreq).IsEqual(original.MinFreq + 1000);
            AssertThat(spectrum.FreqMin).IsBetween(original.MinFreq + 1000 - spectrum.HzPerPx, original.MinFreq + 1000d);

            window.Popup();
            window.GetNode<Button>("Margin/Scroll/Layout/TabContainer/Spectrogram/DefaultsButton").EmitSignal(Button.SignalName.Pressed);
            await runner.AwaitMillis(350);
            spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
            int defaultFrequency = new Settings().SpectrogramMinFreq;
            AssertThat(spectrum.FreqMin).IsBetween(defaultFrequency - spectrum.HzPerPx, (double)defaultFrequency);
            window.EmitSignal(Window.SignalName.CloseRequested);
            AssertThat(new SettingsStore().Load().SpectrogramMinFreq).IsEqual(original.MinFreq + 1000);

            await VerifyFailedSettingsSave(runner, window, slider, save, cancel);
            await VerifySpectrogramLimits(runner, application);
        }
        finally
        {
            window.Hide();
            application.SaveVisualSettings(original);
            Project.Instance.UpdateSpectrogramContextFromSettings();
            volume.Value = originalVolume;
            application.ApplySystemTextScale(originalSystemScale);
            await Settle(runner);
        }
    }

    private static async Task VerifySpectrogramLimits(ISceneRunner runner, Application application)
    {
        var project = Project.Instance;
        VisualSettings saved = VisualSettings.From(new SettingsStore().Load());
        var tooExpensive = saved with { StepSize = 16, FftSize = 16384 };
        AssertThat(tooExpensive.IsInRange).IsTrue();
        AssertThat(application.SaveVisualSettings(tooExpensive)).IsFalse();
        AssertThat(VisualSettings.From(new SettingsStore().Load())).IsEqual(saved);

        try
        {
            var previous = project.SpectrogramContext;
            AssertThat(project.UpdateSpectrogramContextFromSettings(tooExpensive)).IsFalse();
            await Settle(runner);
            AssertThat(project.SpectrogramContext).IsSame(previous);
            var container = (AudioVisualsContainer)runner.FindChild("AudioVisualsContainer")!;
            AssertThat(container.FindChildren("*", recursive: true, owned: false)
                .OfType<SpectrogramSegment>().Any(spectrum => spectrum.IsVisibleInTree())).IsTrue();

            tooExpensive.ApplyTo(project.Settings);
            AssertThat(project.UpdateSpectrogramContextFromSettings()).IsTrue();
            var defaults = VisualSettings.From(new Settings());
            AssertThat(project.SpectrogramContext.SpectrogramGenerator.FftSize).IsEqual(defaults.FftSize);
            AssertThat(project.SpectrogramContext.SpectrogramGenerator.StepSize).IsEqual(defaults.StepSize);

            var window = (Window)runner.FindChild("VisualSettingsWindow")!;
            window.Popup();
            AssertThat(window.GetNode<Label>("Margin/Scroll/Layout/PreviewStatus").Visible).IsTrue();
            AssertThat(window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/SaveButton").Disabled).IsTrue();
            window.GetNode<Button>("Margin/Scroll/Layout/TabContainer/Spectrogram/DefaultsButton").EmitSignal(Button.SignalName.Pressed);
            Edit(window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/minFreq/LineEdit"), (defaults.MinFreq + 1000).ToString());
            await runner.AwaitMillis(350);
            var spectrum = project.SpectrogramContext.SpectrogramGenerator;
            AssertThat(spectrum.FreqMin).IsBetween(defaults.MinFreq + 1000 - spectrum.HzPerPx, defaults.MinFreq + 1000d);
            var volume = (HScrollBar)runner.FindChild("VolumeSliderMusic")!;
            double originalVolume = volume.Value;
            volume.Value = originalVolume == 0.4 ? 0.6 : 0.4;
            AssertThat(new SettingsStore().Load().MusicVolumeNormalized).IsEqual(volume.Value);
            volume.Value = originalVolume;
            window.Hide();
            AssertThat(project.SpectrogramContext.SpectrogramGenerator.FftSize).IsEqual(defaults.FftSize);
            spectrum = project.SpectrogramContext.SpectrogramGenerator;
            AssertThat(spectrum.FreqMin).IsBetween(defaults.MinFreq - spectrum.HzPerPx, (double)defaults.MinFreq);
        }
        finally
        {
            saved.ApplyTo(project.Settings);
            AssertThat(project.UpdateSpectrogramContextFromSettings()).IsTrue();
            await Settle(runner);
        }
    }

    private static async Task VerifyScaleDragKeepsWindowInPlace(ISceneRunner runner)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        var window = (Window)runner.FindChild("VisualSettingsWindow")!;
        var slider = window.GetNode<HSlider>("Margin/Scroll/Layout/TabContainer/Interface/Scale/Slider");
        var percentage = (SpinBox)window.FindChild("Percentage");
        window.GetNode<TabContainer>("Margin/Scroll/Layout/TabContainer").CurrentTab = 0;
        window.Popup();
        slider.Value = 125;
        await Settle(runner);
        window.Position = new Vector2I(80, 80);
        await Settle(runner);
        Vector2 titleOrigin = new(0, -window.GetThemeConstant("title_height"));
        Vector2 screenPosition = window.GetScreenTransform() * titleOrigin;
        Vector2 windowSize = window.Size;
        Vector2 start = slider.GetGlobalRect().GetCenter();
        Vector2 end = start + new Vector2(slider.Size.X / 4, 0);
        var displayedScales = new System.Collections.Generic.List<float>();
        var tree = (SceneTree)Engine.GetMainLoop();
        void RecordScale() => displayedScales.Add(root.ContentScaleFactor);
        tree.ProcessFrame += RecordScale;
        try
        {
            window.PushInput(new InputEventMouseMotion { Position = start }, true);
            window.PushInput(new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            await Settle(runner);
            window.PushInput(new InputEventMouseMotion { Position = end, Relative = end - start, ButtonMask = MouseButtonMask.Left }, true);
            await Settle(runner);
            AssertThat(percentage.Value).IsEqual(slider.Value);
            AssertThat(slider.Value).IsGreater(125d);
            AssertThat(root.ContentScaleFactor).IsEqual(1.25f);
            AssertThat(window.GetScreenTransform() * titleOrigin).IsEqual(screenPosition);
            AssertThat((Vector2)window.Size).IsEqual(windowSize);

            double selected = slider.Value;
            window.PushInput(new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left }, true);
            await Settle(runner);
            AssertThat(root.ContentScaleFactor).IsEqual((float)(selected / 100));
            AssertThat((window.GetScreenTransform() * titleOrigin).DistanceTo(screenPosition)).IsLessEqual(2f);
            AssertThat(percentage.Value).IsEqual(selected);
            AssertThat(displayedScales.All(scale => scale == 1.25f || scale == (float)(selected / 100))).IsTrue();
        }
        finally
        {
            tree.ProcessFrame -= RecordScale;
            window.PushInput(new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left }, true);
            window.Hide();
            await Settle(runner);
        }
    }

    private static async Task VerifyRejectedStartupSettings(ISceneRunner runner, Application application)
    {
        var window = (Window)runner.FindChild("VisualSettingsWindow")!;
        var spectrum = Project.Instance.SpectrogramContext.SpectrogramGenerator;
        AssertThat(spectrum.FftSize).IsEqual(new Settings().SpectrogramFftSize);
        window.Popup();
        AssertThat(window.GetNode<LineEdit>("Margin/Scroll/Layout/TabContainer/Spectrogram/fftSize/LineEdit").Text)
            .IsEqual(application.RejectedVisualSettings!.Value.FftSize.ToString());
        AssertThat(window.GetNode<Label>("Margin/Scroll/Layout/PreviewStatus").Visible).IsTrue();
        var save = window.GetNode<Button>("Margin/Scroll/Layout/BottomButtonsHBox/SaveButton");
        AssertThat(save.Disabled).IsTrue();
        var volume = (HScrollBar)runner.FindChild("VolumeSliderMusic")!;
        volume.Value = volume.Value == 0.4 ? 0.6 : 0.4;
        var store = new SettingsStore();
        AssertThat(store.Load().MusicVolumeNormalized).IsEqual(volume.Value);
        AssertThat(store.RejectedVisualSettings).IsEqual(application.RejectedVisualSettings);
        window.Hide();
        AssertThat(Project.Instance.SpectrogramContext.SpectrogramGenerator).IsSame(spectrum);
        window.Popup();
        window.GetNode<Button>("Margin/Scroll/Layout/TabContainer/Spectrogram/DefaultsButton").EmitSignal(Button.SignalName.Pressed);
        save.EmitSignal(Button.SignalName.Pressed);
        AssertThat(window.Visible).IsFalse();
        AssertThat(application.RejectedVisualSettings).IsNull();
        AssertThat(store.Load().SpectrogramFftSize).IsEqual(new Settings().SpectrogramFftSize);
        AssertThat(store.RejectedVisualSettings).IsNull();
        await Settle(runner);
    }

    private static async Task VerifyFailedSettingsSave(ISceneRunner runner, Window window, HSlider slider, Button save, Button cancel)
    {
        VisualSettings saved = VisualSettings.From(Project.Instance.Settings);
        double volume = Project.Instance.Settings.MusicVolumeNormalized;
        string path = ProjectSettings.GlobalizePath("user://settings.cfg");
        string backup = path + ".backup-" + System.Guid.NewGuid();
        System.IO.File.Move(path, backup);
        try
        {
            System.IO.Directory.CreateDirectory(path);
            window.GetNode<TabContainer>("Margin/Scroll/Layout/TabContainer").CurrentTab = 0;
            window.Popup();
            slider.Value = 150;
            await Settle(runner);
            GD.Print("Expected write failure: settings.cfg temporarily replaced by a directory.");
            save.EmitSignal(Button.SignalName.Pressed);
            AssertThat(window.Visible).IsTrue();
            AssertThat(VisualSettings.From(Project.Instance.Settings)).IsEqual(saved);
            cancel.EmitSignal(Button.SignalName.Pressed);
            AssertThat(((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor).IsEqual((float)saved.TextScaleFactor);
            AssertThat(Project.Instance.Settings.MusicVolumeNormalized).IsEqual(volume);
        }
        finally
        {
            System.IO.Directory.Delete(path);
            System.IO.File.Move(backup, path);
        }

        window.Popup();
        slider.Value = 150;
        save.EmitSignal(Button.SignalName.Pressed);
        AssertThat(window.Visible).IsFalse();
        AssertThat(new SettingsStore().Load().TextScaleFactor).IsEqual(1.5d);
    }

    private static void Edit(LineEdit input, string text)
    {
        input.Text = text;
        input.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }
}