using Godot;
using Tempora.Classes.Utility;

public partial class VisualSettingsWindow : Window, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;

    [Export] HSlider textScaleSlider = null!;
    [Export] SpinBox textScalePercentage = null!;
    [Export] Button textDefaultsButton = null!;
    [Export] LineEdit stepSizeLineEdit = null!;
    [Export] LineEdit fftSizeLineEdit = null!;
    [Export] LineEdit minFreqLineEdit = null!;
    [Export] LineEdit intensityLineEdit = null!;
    [Export] CheckBox dBCheckbox = null!;
    [Export] Button saveButton = null!;
    [Export] Button defaultsButton = null!;
    [Export] Button cancelButton = null!;
    [Export] Label previewStatus = null!;

    private readonly Timer spectrogramDelay = new() { WaitTime = 0.2, OneShot = true };
    private Application application = null!;
    private VisualSettings appliedSpectrogram;
    private bool previewing;
    private bool scaleQueued;
    private bool draggingScale;

    public override void _Ready()
    {
        application = GetTree().Root.GetNode<Application>("Application");
        AddChild(spectrogramDelay);
        spectrogramDelay.Timeout += PreviewSpectrogram;

        textScaleSlider.Share(textScalePercentage);
        textScalePercentage.UpdateOnTextChanged = true;
        stepSizeLineEdit.TooltipText = "16–65536";
        fftSizeLineEdit.TooltipText = "32–16384 (powers of 2)";
        minFreqLineEdit.TooltipText = "0–96000 Hz; below half the sample rate";
        intensityLineEdit.TooltipText = "≥ 0";
        textScaleSlider.ValueChanged += _ => QueueScalePreview();
        textScaleSlider.DragStarted += () => draggingScale = true;
        textScaleSlider.DragEnded += _ =>
        {
            draggingScale = false;
            QueueScalePreview();
        };
        textDefaultsButton.Pressed += () => textScaleSlider.Value = 100;
        foreach (LineEdit input in new[] { stepSizeLineEdit, fftSizeLineEdit, minFreqLineEdit, intensityLineEdit })
            input.TextChanged += _ => QueueSpectrogramPreview();
        dBCheckbox.Toggled += _ => QueueSpectrogramPreview();
        defaultsButton.Pressed += ResetSpectrogram;

        saveButton.Pressed += Save;
        cancelButton.Pressed += Hide;
        CloseRequested += Hide;
        AboutToPopup += BeginPreview;
        VisibilityChanged += OnVisibilityChanged;
        GlobalEvents.Instance.AudioFileChanged += OnAudioFileChanged;
    }

    public override void _ExitTree()
    {
        GlobalEvents.Instance.AudioFileChanged -= OnAudioFileChanged;
        EndPreview();
    }

    private void BeginPreview()
    {
        if (previewing)
            return;
        appliedSpectrogram = VisualSettings.From(Settings);
        textScaleSlider.Value = Settings.TextScaleFactor * 100;
        LoadSpectrogramValues(application.RejectedVisualSettings ?? appliedSpectrogram);
        saveButton.Disabled = false;
        saveButton.TooltipText = "";
        previewStatus.Visible = false;
        previewing = true;
        QueueSpectrogramPreview();
    }

    private void ResetSpectrogram()
    {
        LoadSpectrogramValues(VisualSettings.From(new Settings()));
        QueueSpectrogramPreview();
    }

    private void OnVisibilityChanged()
    {
        if (!Visible)
            EndPreview();
    }

    private void LoadSpectrogramValues(VisualSettings values)
    {
        stepSizeLineEdit.Text = values.StepSize.ToString();
        fftSizeLineEdit.Text = values.FftSize.ToString();
        minFreqLineEdit.Text = values.MinFreq.ToString();
        intensityLineEdit.Text = values.Intensity.ToString();
        dBCheckbox.SetPressedNoSignal(values.UseDb);
    }

    private void QueueScalePreview()
    {
        if (!previewing || draggingScale || scaleQueued)
            return;
        scaleQueued = true;
        Callable.From(PreviewScale).CallDeferred();
    }

    private void PreviewScale()
    {
        scaleQueued = false;
        if (previewing && !draggingScale)
            application.PreviewTextScale(textScalePercentage.Value / 100);
    }

    private void QueueSpectrogramPreview()
    {
        if (!previewing)
            return;
        bool valid = TryReadValues(out VisualSettings values);
        saveButton.Disabled = !valid;
        previewStatus.Text = !valid ? "Check the spectrogram values. For long audio, increase step size or reduce FFT size."
            : application.RejectedVisualSettings != null && values == VisualSettings.From(Settings)
                ? "Saved display values were invalid. Review and save your settings." : "";
        previewStatus.Visible = previewStatus.Text.Length > 0;
        spectrogramDelay.Start();
    }

    private void PreviewSpectrogram()
    {
        if (previewing && TryReadValues(out VisualSettings values))
            ApplySpectrogram(values);
    }

    private void ApplySpectrogram(VisualSettings values)
    {
        if (values.Spectrogram == appliedSpectrogram.Spectrogram && Project.Instance.SpectrogramContext != null)
            return;
        if (Project.Instance.UpdateSpectrogramContextFromSettings(values))
            appliedSpectrogram = values;
    }

    private void OnAudioFileChanged(object? sender, System.EventArgs args)
    {
        appliedSpectrogram = VisualSettings.From(Settings);
        QueueSpectrogramPreview();
    }

    private bool TryReadValues(out VisualSettings values)
    {
        var audio = Project.Instance.AudioFile;
        return VisualSettings.TryParse(textScalePercentage.Value / 100,
            (stepSizeLineEdit.Text, fftSizeLineEdit.Text, minFreqLineEdit.Text, intensityLineEdit.Text),
            dBCheckbox.ButtonPressed, audio.SampleRate, audio.PcmFloats[0].Length, out values);
    }

    private void Save()
    {
        if (textScalePercentage.IsVisibleInTree())
            textScalePercentage.Apply();
        if (!TryReadValues(out VisualSettings values))
            return;
        spectrogramDelay.Stop();
        ApplySpectrogram(values);
        if (!application.SaveVisualSettings(values))
            return;
        Hide();
        Project.Instance.NotificationMessage = "Saved visual options.";
    }

    private void EndPreview()
    {
        if (!previewing)
            return;
        previewing = false;
        draggingScale = false;
        spectrogramDelay.Stop();
        application.PreviewTextScale(null);
        if (appliedSpectrogram.Spectrogram != VisualSettings.From(Settings).Spectrogram)
            Project.Instance.UpdateSpectrogramContextFromSettings();
    }
}