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
using System.Linq;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.TimingClasses;

namespace Tempora.Classes.Utility;

/// <summary>
///     Handles saving and loading of project files
/// </summary>
public partial class ProjectFileManager : Node, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;

    #region Properties & Fields
    private static ProjectFileManager instance = null!;

    [Export]
    public FileDialog SaveFileDialog = null!;

    [Export]
    public FileDialog LoadFileDialog = null!;

    private MusicPlayer MusicPlayer => MusicPlayer.Instance;

    public static readonly string ProjectFileExtension = ".tmpr";

    public const string AutoSavePath = "user://autosave";

    public static ProjectFileManager Instance { get => instance; set => instance = value; }
    #endregion

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        Instance = this;

        SaveFileDialog.FileSelected += OnSaveFilePathSelected;
        LoadFileDialog.FileSelected += OnLoadFilePathSelected;
        Project.Instance.TimingEditFinished += OnTimingEditFinished;
    }

    public override void _ExitTree() => Project.Instance.TimingEditFinished -= OnTimingEditFinished;

    public override void _Input(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventKey keyEvent:
                {
                    if (keyEvent.Keycode == Key.S && keyEvent.Pressed && Input.IsKeyPressed(Key.Ctrl))
                        ProjectFileManager.SaveProjectAs(Project.Instance.ProjectPath);
                    break;
                }
        }
    }

    #region Save Dialog

    public enum SaveConfig
    {
        project,
        osz,
        beatSaber,
        guitarGame
    }

    private SaveConfig latestSaveConfig = SaveConfig.project;

    public void SaveOszFileDialogPopup() => SaveFileDialogPopup(SaveConfig.osz);
    public void SaveProjectFileDialogPopup() => SaveFileDialogPopup(SaveConfig.project);
    public void SaveBeatSaberFileDialogPopup() => SaveFileDialogPopup(SaveConfig.beatSaber);
    public void SaveGuitarGameFileDialogPopup() => SaveFileDialogPopup(SaveConfig.guitarGame);

    private void SaveFileDialogPopup(SaveConfig config)
    {
        switch (config)
        {
            case SaveConfig.project:
                SaveFileDialog.CurrentDir = Settings.ProjectFilesDirectory;
                break;
            case SaveConfig.osz:
                SaveFileDialog.CurrentDir = Settings.OszFilesDirectory;
                break;
            case SaveConfig.beatSaber:
                SaveFileDialog.CurrentDir = Settings.BeatSaberFilesDirectory;
                break;
            case SaveConfig.guitarGame:
                SaveFileDialog.CurrentDir = Settings.GuitarGameFilesDirectory;
                break;
        }
        latestSaveConfig = config;
        UpdateSaveDialogTitle();
        SaveFileDialog.Popup();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged && IsNodeReady())
            UpdateSaveDialogTitle();
    }

    private void UpdateSaveDialogTitle()
    {
        SaveFileDialog.Title = latestSaveConfig switch
        {
            SaveConfig.project => Tr("Save Project"),
            SaveConfig.osz => Tr("Export osz"),
            SaveConfig.beatSaber => string.Format(Tr("Export Beat Saber (v{0})"), Settings.BeatSaberExportFormat),
            SaveConfig.guitarGame => Tr("Export Guitar Game (.chart)"),
            _ => throw new ArgumentOutOfRangeException(nameof(latestSaveConfig))
        };
    }
    private void OnSaveFilePathSelected(string selectedPath)
    {
        switch (latestSaveConfig)
        {
            case SaveConfig.project:
                ProjectFileManager.SaveProjectAs(selectedPath);
                string dir = Path.GetDirectoryName(selectedPath) ?? "";
                Settings.ProjectFilesDirectory = dir;
                break;
            case SaveConfig.osz:
                OsuExporter.Instance.SaveOszAs_AndShowInFileExplorer(selectedPath);
                dir = Path.GetDirectoryName(selectedPath) ?? "";
                Settings.OszFilesDirectory = dir;
                break;
            case SaveConfig.beatSaber:
                BeatSaberExporter.Instance.SaveFilesToPath(selectedPath);
                dir = Path.GetDirectoryName(selectedPath) ?? "";
                Settings.BeatSaberFilesDirectory = dir;
                break;
            case SaveConfig.guitarGame:
                GuitarGameExporter.SaveChartToPath_AndShowInFileExplorer(
                    Project.Instance.Timing,
                    selectedPath,
                    Project.Instance.AudioFile.AudacityOrigin);
                dir = Path.GetDirectoryName(selectedPath) ?? "";
                Settings.GuitarGameFilesDirectory = dir;
                break;
        }
    }

    private void OnTimingEditFinished(object? sender, EventArgs e)
    {
        if (Project.Instance.Timing.IsInstantiating)
            return;
        AutoSave();
    }
    #endregion

    #region Load Dialog
    public void LoadFileDialogPopup()
    {
        LoadFileDialog.CurrentDir = Settings.ProjectFilesDirectory;
        LoadFileDialog.Popup();
    }

    private void OnLoadFilePathSelected(string selectedPath)
    {
        string extension = Path.GetExtension(selectedPath).ToLower();

        string projectFileExtension = ProjectFileExtension;
        string mp3Extension = ".mp3";
        string oggExtension = ".ogg";

        string[] allowedExtensions =
        [
            projectFileExtension,
            mp3Extension,
            oggExtension,
        ];

        if (!allowedExtensions.Contains(extension))
            return;

        switch (extension)
        {
            case var value when (value == mp3Extension || value == oggExtension):
                LoadAudioFromFilePath(selectedPath);
                break;
            case var value when value == projectFileExtension:
                ProjectFileManager.Instance.LoadProjectFromFilePath(selectedPath);
                string dir = Path.GetDirectoryName(selectedPath) ?? "";
                Settings.ProjectFilesDirectory = dir;
                break;
        }
    }

    public void LoadAudioFromFilePath(string audioPath)
    {
        if (!AudioFile.IsAudioFileExtensionValid(audioPath, out _))
            return;

        var audio = new AudioFile(audioPath);
        ReplaceDocument(new Timing(Settings), audio, null);
    }

    private static void ResetEditingState()
    {
        Context.Instance.HeldPointIsJustBeingAdded = false;
        Context.Instance.HeldTimingPoint = null;
        Context.Instance.TimingPointNearestCursor = null;
        Context.Instance.SelectedMeasurePosition = 0;
        Context.Instance.IsSelectedMeasurePositionMoving = false;
        TimingPointSelection.Instance.DeselectAll();

        MementoHandler.Instance.ResetTimingHistory();
    }
    #endregion

    #region Save Project
    public static void SaveProjectAs(string? filePath)
    {
        if (filePath == null)
        {
            Instance.SaveProjectFileDialogPopup();
            return;
        }

        SaveProject(filePath);

        Project.Instance.ProjectPath = filePath;
        Project.Instance.ShowNotification("Saved to {0}", arguments: [filePath]);
    }

    private static void SaveProject(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        string correctExtension = ProjectFileManager.ProjectFileExtension;

        filePath = Path.ChangeExtension(filePath, correctExtension);
        string? fileDir = Path.GetDirectoryName(filePath) ?? throw new NullReferenceException(nameof(filePath));
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        string audioFileExtension = Project.Instance.AudioFile.Extension;
        string audioFilePathShort = $"{fileName}{audioFileExtension}";
        string audioFilePathLong = fileDir == "user:"
            ? fileDir + "//" + audioFilePathShort
            : Path.Combine(fileDir, audioFilePathShort);

        FileHandler.SaveBuffer(audioFilePathLong, Project.Instance.AudioFile.FileBuffer);

        string file = ProjectFileCodec.Serialize(Project.Instance.Timing, audioFilePathShort);
        FileHandler.SaveText(filePath, file);
    }

    private Timing? lastSavedTiming;
    private IMemento? lastSavedState;
    private AudioFile? lastSavedAudio;

    public static void AutoSave()
    {
        var project = Project.Instance;
        var store = Instance;
        if (store.lastSavedTiming == project.Timing && store.lastSavedAudio == project.AudioFile
            && store.lastSavedState != null && project.Timing.Matches(store.lastSavedState))
            return;
        store.lastSavedState = null;
        SaveProject(AutoSavePath);
        store.lastSavedTiming = project.Timing;
        store.lastSavedAudio = project.AudioFile;
        store.lastSavedState = project.Timing.GetMemento();
    }

    #endregion

    #region Load Project
    public void NewProject()
    {
        Project.Instance.ProjectPath = null;
        //Project.Instance.NotificationMessage = "You are now editing a new project.";
    }

    public void LoadProjectFromFilePath(string filePath)
    {
        string projectFile = FileHandler.LoadText(filePath);
        if (string.IsNullOrEmpty(projectFile))
            return;

        ProjectFileData data = ProjectFileCodec.Parse(projectFile);
        Timing timing = CreateTiming(data);
        string directory = Path.GetDirectoryName(filePath) ?? throw new ArgumentException("Project path has no directory.", nameof(filePath));
        var audio = new AudioFile(Path.Combine(directory, data.AudioPath));

        ReplaceDocument(timing, audio, filePath);
        Project.Instance.ShowNotification("Loaded {0}", arguments: [filePath]);
    }

    private Timing CreateTiming(ProjectFileData data)
    {
        var timing = new Timing(Settings) { IsInstantiating = true };
        foreach (var point in data.TimeSignaturePoints)
            timing.UpdateTimeSignature([point.Upper, point.Lower], point.Measure);
        foreach (var point in data.TimingPoints)
        {
            if (point.MeasuresPerSecond is double measuresPerSecond)
                timing.AddTimingPoint(point.MeasurePosition, point.Offset, measuresPerSecond);
            else
                timing.AddTimingPoint(point.MeasurePosition, point.Offset);
        }
        timing.IsInstantiating = false;
        return timing;
    }

    private static void ReplaceDocument(Timing timing, AudioFile audio, string? path)
    {
        Project.Instance.ReplaceDocument(timing, audio, path);
        ResetEditingState();
        GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.TimingChanged));
        Project.Instance.NotifyTimingEditFinished();
    }
    #endregion
}