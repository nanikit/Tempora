using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Tempora.Classes.Utility;

public sealed class Settings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DisplayChanged;

    public bool SnapToGridEnabled = true;
    public bool ShowMoreSettings;
    public const int MaxNumberOfBlocks = 30;
    public static readonly Dictionary<int, int> GridSliderToDivisorDict = new()
    {
        { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 }, { 5, 6 }, { 6, 8 }, { 7, 12 }, { 8, 16 }
    };

    public static int DivisorToSliderValue(int divisor) => GridSliderToDivisorDict.FirstOrDefault(x => x.Value == divisor).Key;

    private string language = "automatic";
    [SettingKey("HelpLanguage")]
    public string Language
    {
        get => language;
        set => Set(ref language, value);
    }

    private string projectFilesDirectory = "";
    public string ProjectFilesDirectory
    {
        get => projectFilesDirectory;
        set => Set(ref projectFilesDirectory, value);
    }

    private string oszFilesDirectory = "";
    public string OszFilesDirectory
    {
        get => oszFilesDirectory;
        set => Set(ref oszFilesDirectory, value);
    }

    private string beatSaberFilesDirectory = "";
    public string BeatSaberFilesDirectory
    {
        get => beatSaberFilesDirectory;
        set => Set(ref beatSaberFilesDirectory, value);
    }

    private string guitarGameFilesDirectory = "";
    public string GuitarGameFilesDirectory
    {
        get => guitarGameFilesDirectory;
        set => Set(ref guitarGameFilesDirectory, value);
    }

    private int gridDivisor = 1;
    public int GridDivisor
    {
        get => gridDivisor;
        set => Set(ref gridDivisor, value, refreshDisplay: true);
    }

    private int numberOfRows = 10;
    public int NumberOfRows
    {
        get => numberOfRows;
        set => Set(ref numberOfRows, value, refreshDisplay: true);
    }

    private float measureOverlap = 0;
    public float MeasureOverlap
    {
        get => measureOverlap;
        set => Set(ref measureOverlap, value, refreshDisplay: true);
    }

    private float downbeatPositionOffset = 0.125f;
    [SettingKey("DownbeatVisualOffset")]
    public float DownbeatPositionOffset
    {
        get => downbeatPositionOffset;
        set => Set(ref downbeatPositionOffset, value, refreshDisplay: true);
    }

    private bool metronomeFollowsGrid = false;
    public bool MetronomeFollowsGrid
    {
        get => metronomeFollowsGrid;
        set => Set(ref metronomeFollowsGrid, value, refreshDisplay: true);
    }

    private bool roundBPM = true;
    public bool RoundBPM
    {
        get => roundBPM;
        set => Set(ref roundBPM, value, refreshDisplay: true);
    }

    private bool preserveBPMWhenChangingTimeSignature = true;
    [SettingKey("MoveSubsequentTimingPointsWhenChangingTimeSignature")]
    public bool PreserveBPMWhenChangingTimeSignature
    {
        get => preserveBPMWhenChangingTimeSignature;
        set => Set(ref preserveBPMWhenChangingTimeSignature, value);
    }

    private bool autoScrollWhenAddingTimingPoints = false;
    [SettingKey("AutoScroll")]
    public bool AutoScrollWhenAddingTimingPoints
    {
        get => autoScrollWhenAddingTimingPoints;
        set => Set(ref autoScrollWhenAddingTimingPoints, value);
    }

    private bool seekPlaybackOnTimingPointChanges = true;
    public bool SeekPlaybackOnTimingPointChanges
    {
        get => seekPlaybackOnTimingPointChanges;
        set => Set(ref seekPlaybackOnTimingPointChanges, value);
    }

    private int exportOffsetMs = -14;
    public int ExportOffsetMs
    {
        get => exportOffsetMs;
        set => Set(ref exportOffsetMs, value);
    }

    private bool measureResetsOnUnsupportedTimeSignatures = true;
    public bool MeasureResetsOnUnsupportedTimeSignatures
    {
        get => measureResetsOnUnsupportedTimeSignatures;
        set => Set(ref measureResetsOnUnsupportedTimeSignatures, value);
    }

    private bool removePointsThatChangeNothing = true;
    public bool RemovePointsThatChangeNothing
    {
        get => removePointsThatChangeNothing;
        set => Set(ref removePointsThatChangeNothing, value);
    }

    private bool addExtraPointsOnDownbeats = true;
    public bool AddExtraPointsOnDownbeats
    {
        get => addExtraPointsOnDownbeats;
        set => Set(ref addExtraPointsOnDownbeats, value);
    }

    private bool addExtraPointsOnQuarterNotes = true;
    public bool AddExtraPointsOnQuarterNotes
    {
        get => addExtraPointsOnQuarterNotes;
        set => Set(ref addExtraPointsOnQuarterNotes, value);
    }

    private bool omitBarlines = true;
    public bool OmitBarlines
    {
        get => omitBarlines;
        set => Set(ref omitBarlines, value);
    }

    private bool preventDoubleBarlines = true;
    [SettingKey("PreventDoubleBarLines")]
    public bool PreventDoubleBarlines
    {
        get => preventDoubleBarlines;
        set => Set(ref preventDoubleBarlines, value);
    }

    private double musicVolumeNormalized = 0.75d;
    public double MusicVolumeNormalized
    {
        get => musicVolumeNormalized;
        set => Set(ref musicVolumeNormalized, value);
    }

    private double metronomeVolumeNormalized = 1d;
    public double MetronomeVolumeNormalized
    {
        get => metronomeVolumeNormalized;
        set => Set(ref metronomeVolumeNormalized, value);
    }

    private double masterVolumeNormalized = 0.25d;
    public double MasterVolumeNormalized
    {
        get => masterVolumeNormalized;
        set => Set(ref masterVolumeNormalized, value);
    }

    private int beatSaberExportFormat = 4;
    public int BeatSaberExportFormat
    {
        get => beatSaberExportFormat;
        set => Set(ref beatSaberExportFormat, value);
    }

    private bool renderAsSpectrogram = true;
    public bool RenderAsSpectrogram
    {
        get => renderAsSpectrogram;
        set => Set(ref renderAsSpectrogram, value, refreshDisplay: true);
    }

    private int spectrogramStepSize = 64;
    public int SpectrogramStepSize
    {
        get => spectrogramStepSize;
        set => Set(ref spectrogramStepSize, Math.Abs(value));
    }

    private int spectrogramFftSize = 256;
    public int SpectrogramFftSize
    {
        get => spectrogramFftSize;
        set => Set(ref spectrogramFftSize, Math.Abs(value));
    }

    private int spectrogramMaxFreq = 2200;
    public int SpectrogramMaxFreq
    {
        get => spectrogramMaxFreq;
        set => Set(ref spectrogramMaxFreq, Math.Abs(value));
    }

    private int spectrogramIntensity = 5;
    public int SpectrogramIntensity
    {
        get => spectrogramIntensity;
        set => Set(ref spectrogramIntensity, Math.Abs(value));
    }

    private bool spectrogramUseDb = true;
    public bool SpectrogramUseDb
    {
        get => spectrogramUseDb;
        set => Set(ref spectrogramUseDb, value);
    }

    private void Set<T>(ref T field, T value, bool refreshDisplay = false, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (refreshDisplay)
            DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class SettingKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

public interface ISettingsConsumer
{
    Settings Settings { get; set; }
}