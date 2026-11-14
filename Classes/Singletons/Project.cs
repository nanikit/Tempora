// Copyright 2024 https://github.com/kongehund
// 
// This file is licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International (CC BY-NC-ND 4.0).
// You are free to:
// - Share, copy and redistribute the material in any medium or format
//
// Under the following terms:
// - Attribution - You must give appropriate credit, provide a link to the license, and indicate if changes were made.
// - NonCommercial - You may not use the material for commercial purposes.
// - NoDerivatives - If you remix, transform, or build upon the material, you may not distribute the modified material.
//
// Full license text is available at: https://creativecommons.org/licenses/by-nc-nd/4.0/legalcode

using System;
using System.IO;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.Visual.AudioDisplay;
using Tempora.Classes.TimingClasses;

namespace Tempora.Classes.Utility;

/// <summary>
/// Contains data about the current project.
/// </summary>
public partial class Project : Node, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;

    private Timing timing = null!;
    public TimingEditSession Edits { get; private set; } = null!;
    public event EventHandler? TimingEditFinished;
    public Timing Timing
    {
        get => timing;
        set
        {
            if (timing == value)
                return;
            UnsubscribeTiming();
            timing = value;
            timing.Changed += OnTimingChanged;
            timing.PointCountChanged += OnPointCountChanged;
            timing.MeasurePositionRejected += OnMeasurePositionRejected;
            Edits = new TimingEditSession(timing);
            Edits.Committed += OnEditCommitted;
            Edits.Finished += NotifyTimingEditFinished;
        }
    }

    private void UnsubscribeTiming()
    {
        if (timing == null)
            return;
        timing.Changed -= OnTimingChanged;
        timing.PointCountChanged -= OnPointCountChanged;
        timing.MeasurePositionRejected -= OnMeasurePositionRejected;
        Edits.Committed -= OnEditCommitted;
        Edits.Finished -= NotifyTimingEditFinished;
    }

    private void OnTimingChanged(object? sender, EventArgs args) => GlobalEvents.Instance.NotifyTimingChanged();
    private void OnPointCountChanged(object? sender, EventArgs args) => GlobalEvents.Instance.NotifyTimingPointCountChanged();
    private void OnMeasurePositionRejected(TimingPoint point) => GlobalEvents.Instance.NotifyMeasurePositionRejected(point);
    private void OnEditCommitted(object? historyGroup) => MementoHandler.Instance.RecordEdit(historyGroup);
    public void NotifyTimingEditFinished() => TimingEditFinished?.Invoke(this, EventArgs.Empty);

    public override void _ExitTree() => UnsubscribeTiming();

    private static Project instance = null!;

    private AudioFile audioFile = null!;



    private string? projectPath = null;
    public string? ProjectPath
    {
        get => projectPath;
        set
        {
            if (value == projectPath)
                return;
            projectPath = value;
            string? projectName = Path.GetFileName(projectPath);
            string titleAddition = projectName != null ? $" - {projectName}" : "";
            GetWindow().Title = "Tempora" + titleAddition;
        }
    }

    public event EventHandler NotificationMessageChanged = null!;
    private string notificationMessage = "";
    private object[] notificationArguments = [];

    public string NotificationMessage
    {
        get => string.Format(Tr(notificationMessage), notificationArguments);
        set => ShowNotification(value);
    }

    public double NotificationDuration { get; private set; } = 2.5d;

    public void ShowNotification(string message, double duration = 2.5d, object[]? arguments = null)
    {
        notificationMessage = message;
        notificationArguments = arguments ?? [];
        NotificationDuration = duration;
        NotificationMessageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static Project Instance { get => instance; set => instance = value; }

    public AudioFile AudioFile
    {
        get => audioFile;
        set
        {
            if (audioFile == value)
                return;
            audioFile = value;
            UpdateSpectrogramContextFromSettings();
            GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.Instance.AudioFileChanged), this, EventArgs.Empty);
        }
    }

    public SpectrogramContext SpectrogramContext { get; set; } = null!;

    public bool UpdateSpectrogramContextFromSettings(VisualSettings? preview = null)
    {
        VisualSettings values = preview ?? VisualSettings.From(Settings);
        if (!values.IsValid(AudioFile.SampleRate, AudioFile.PcmFloats[0].Length))
        {
            if (preview.HasValue)
                return false;
            values = VisualSettings.From(new Settings());
            if (!values.IsValid(AudioFile.SampleRate, AudioFile.PcmFloats[0].Length))
                throw new InvalidOperationException("The audio cannot be represented as a spectrogram with default settings.");
        }
        SpectrogramContext = new(SpectrogramHelper.GetSpectrogramGenerator(AudioFile, values.StepSize, values.FftSize, values.MinFreq, 16000));
        SpectrogramContext.DB = values.UseDb;
        SpectrogramContext.Intensity = values.Intensity;
        SpectrogramContext.UpdateSpectrogram();
        return true;
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        Instance = this;
        Timing = new Timing(Settings);
    }
}