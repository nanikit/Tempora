using Godot;
using Tempora.Classes.Utility;

public partial class VisualSettingsWindow : Window, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;

    [Export]
    HSlider textScaleSlider = null!;

    [Export]
    SpinBox textScalePercentage = null!;

    [Export]
    Button textDefaultsButton = null!;

    [Export]
    LineEdit stepSizeLineEdit = null!;

    [Export]
    LineEdit fftSizeLineEdit = null!;

    [Export]
    LineEdit minFreqLineEdit = null!;

    [Export]
    LineEdit intensityLineEdit = null!;

    [Export]
    CheckBox dBCheckbox = null!;

    [Export]
    Button okButton = null!;

    [Export]
    Button defaultsButton = null!;

    [Export]
    Button applyButton = null!;

    public override void _Ready()
    {
        textScaleSlider.Share(textScalePercentage);
        textDefaultsButton.Pressed += () => textScaleSlider.Value = 100;
        okButton.Pressed += OnOkButtonPressed;
        defaultsButton.Pressed += OnResetButtonPressed;
        applyButton.Pressed += OnApplyButtonPressed;

        CloseRequested += CloseWithoutSaving;
        AboutToPopup += OnAboutToPopup;
    }

    private void LoadSpectrogramValues(Settings settings)
    {
        stepSizeLineEdit.Text = settings.SpectrogramStepSize.ToString();
        fftSizeLineEdit.Text = settings.SpectrogramFftSize.ToString();
        minFreqLineEdit.Text = settings.SpectrogramMinFreq.ToString();
        intensityLineEdit.Text = settings.SpectrogramIntensity.ToString();
        dBCheckbox.ButtonPressed = settings.SpectrogramUseDb;
    }

    private void OnAboutToPopup()
    {
        textScaleSlider.Value = Settings.TextScaleFactor * 100;
        LoadSpectrogramValues(Settings);
    }

    private void OnOkButtonPressed()
    {
        ApplySettings();
        Hide();

        Project.Instance.NotificationMessage = "Saved visual options.";
    }

    private void OnResetButtonPressed()
    {
        LoadSpectrogramValues(new Settings());
    }

    private void OnApplyButtonPressed()
    {
        ApplySettings();
    }

    private void ApplySettings()
    {
        var previous = (Settings.SpectrogramStepSize, Settings.SpectrogramFftSize,
            Settings.SpectrogramMinFreq, Settings.SpectrogramIntensity, Settings.SpectrogramUseDb);
        SaveSettings(Settings);
        var current = (Settings.SpectrogramStepSize, Settings.SpectrogramFftSize,
            Settings.SpectrogramMinFreq, Settings.SpectrogramIntensity, Settings.SpectrogramUseDb);
        if (previous != current)
            Project.Instance.UpdateSpectrogramContextFromSettings();
    }

    private void SaveSettings(Settings settings)
    {
        // A hidden SpinBox can retain stale text until its next draw.
        if (textScalePercentage.IsVisibleInTree())
            textScalePercentage.Apply();
        settings.TextScaleFactor = textScalePercentage.Value / 100;

        bool stepSizeParsed = int.TryParse(stepSizeLineEdit.Text, out int stepSize);
        if (stepSizeParsed)
            settings.SpectrogramStepSize = stepSize;

        bool fftSizeParsed = int.TryParse(fftSizeLineEdit.Text, out int fftSize);
        if (fftSizeParsed)
            settings.SpectrogramFftSize = fftSize;

        bool minFreqParsed = int.TryParse(minFreqLineEdit.Text, out int minFreq);
        if (minFreqParsed)
            settings.SpectrogramMinFreq = minFreq;

        bool intensityParsed = int.TryParse(intensityLineEdit.Text, out int intensity);
        if (intensityParsed)
            settings.SpectrogramIntensity = intensity;

        settings.SpectrogramUseDb = dBCheckbox.ButtonPressed;
    }

    private void CloseWithoutSaving()
    {
        Hide();
        Project.Instance.NotificationMessage = "Visual options were not saved.";
    }
}
