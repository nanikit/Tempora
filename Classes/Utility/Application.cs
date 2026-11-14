using System;
using System.ComponentModel;
using Godot;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Visual;

namespace Tempora.Classes.Utility;

// Owns application settings without exposing a global settings accessor.
public partial class Application : Node
{
    private Settings settings = new();
    private SettingsStore? store;
    private string? settingsLoadError;
    private readonly TextSizing textSizing = new();
    private WindowsTextScale? windowsTextScale;
    private double? previewTextScale;
    private bool savingVisualSettings;
    public double SystemTextScale { get; private set; } = 1;
    public VisualSettings? RejectedVisualSettings => store?.RejectedVisualSettings;

    public override void _EnterTree()
    {
        LoadSettings();
        Timing.Instance = new Timing(settings);
        ApplyLanguage();
        ApplyVolume("Music", settings.MusicVolumeNormalized);
        ApplyVolume("Metronome", settings.MetronomeVolumeNormalized);
        ApplyVolume("Master", settings.MasterVolumeNormalized);
        settings.PropertyChanged += OnSettingChanged;
        InitializeTextScale();
        ApplyTextScale();
        textSizing.Register(GetTree().Root);
        // NodeAdded runs before _Ready, including for autoloads and dynamically created rows.
        GetTree().NodeAdded += ConfigureNode;
        GetTree().NodeRemoved += textSizing.Unregister;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= ConfigureNode;
        GetTree().NodeRemoved -= textSizing.Unregister;
        settings.PropertyChanged -= OnSettingChanged;
        if (OperatingSystem.IsWindows())
            windowsTextScale?.Dispose();
        textSizing.Dispose();
    }

    public override void _Process(double delta)
    {
        if (OperatingSystem.IsWindows() && windowsTextScale?.TakeChange() == true)
            ApplySystemTextScale(windowsTextScale.ScaleFactor);
    }

    public void ApplySystemTextScale(double scale)
    {
        SystemTextScale = scale;
        ApplyTextScale();
    }

    public void PreviewTextScale(double? scale)
    {
        previewTextScale = scale;
        ApplyTextScale();
    }

    public bool SaveVisualSettings(VisualSettings values)
    {
        var audio = Project.Instance.AudioFile;
        if (!values.IsValid(audio.SampleRate, audio.PcmFloats[0].Length))
            return false;
        VisualSettings original = VisualSettings.From(settings);
        savingVisualSettings = true;
        try
        {
            values.ApplyTo(settings);
            if (SaveSettings())
                return true;
            original.ApplyTo(settings);
            return false;
        }
        finally
        {
            savingVisualSettings = false;
        }
    }

    private void ApplyTextScale() => textSizing.ApplyScale(SystemTextScale, previewTextScale ?? settings.TextScaleFactor);

    private void LoadSettings()
    {
        var candidate = new SettingsStore();
        try
        {
            settings = candidate.Load();
            store = candidate;
        }
        catch (Exception exception) when (exception is System.IO.IOException or FormatException or OverflowException)
        {
            settingsLoadError = exception.Message;
            Godot.GD.PushError(exception.Message);
        }
    }

    private void InitializeTextScale()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            windowsTextScale = new WindowsTextScale();
            ApplySystemTextScale(windowsTextScale.ScaleFactor);
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            windowsTextScale?.Dispose();
            windowsTextScale = null;
            Godot.GD.PushWarning($"Could not read Windows text size; using the default text size. {exception.Message}");
        }
    }

    private void ConfigureNode(Node node)
    {
        if (node is ISettingsConsumer consumer)
            consumer.Settings = settings;
        textSizing.Register(node);
        if (node is Tempora.Classes.Visual.AudioDisplay.Main && settingsLoadError != null)
            node.Ready += () => Project.Instance.ShowNotification(settingsLoadError, 10);
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs change)
    {
        ApplySetting(change.PropertyName);
        if (!savingVisualSettings)
            SaveSettings(change.PropertyName);
    }

    private void ApplySetting(string? name)
    {
        switch (name)
        {
            case nameof(Settings.Language): ApplyLanguage(); break;
            case nameof(Settings.TextScaleFactor): ApplyTextScale(); break;
            case nameof(Settings.MusicVolumeNormalized): ApplyVolume("Music", settings.MusicVolumeNormalized); break;
            case nameof(Settings.MetronomeVolumeNormalized): ApplyVolume("Metronome", settings.MetronomeVolumeNormalized); break;
            case nameof(Settings.MasterVolumeNormalized): ApplyVolume("Master", settings.MasterVolumeNormalized); break;
        }
    }

    private bool SaveSettings(string? changedProperty = null)
    {
        if (store == null)
            return false;
        try
        {
            store.Save(settings, changedProperty);
            return true;
        }
        catch (Exception exception) when (exception is System.IO.IOException or FormatException)
        {
            Godot.GD.PushError(exception.Message);
            Project.Instance.ShowNotification(exception.Message, 10);
            return false;
        }
    }

    private void ApplyLanguage() => TranslationServer.SetLocale(settings.Language == "automatic" ? OS.GetLocale() : settings.Language);

    private static void ApplyVolume(string bus, double volume) => AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(bus), Mathf.LinearToDb((float)volume));
}