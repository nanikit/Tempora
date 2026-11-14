namespace Tempora.Tests;

using System;
using System.IO;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies the real save-then-reopen flow against the real environment (Godot runtime, real file I/O).
/// The previous xUnit tests only checked the serialized string and could not verify the file round-trip
/// (stored audio + reloaded .tmpr). The filesystem boundary is isolated under a user:// temp path.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ProjectFileRoundTripTest
{
    private const string SaveDir = "user://test_roundtrip";

    [BeforeTest]
    public void SetupIntegration()
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
    }

    [AfterTest]
    public void TearDownIntegration()
    {
        using DirAccess? dir = DirAccess.Open(SaveDir);
        if (dir == null)
            return;
        foreach (string file in dir.GetFiles())
            dir.Remove(file);
    }

    [TestCase]
    public void Save_then_load_preserves_timing_point()
    {
        TimingPoint loaded = SaveAndReloadSingleTimingPoint(measurePosition: 0d, offset: 1.5d, measuresPerSecond: 0.5d);

        AssertThat(Project.Instance.Timing.TimingPoints.Count).IsEqual(1);
        AssertThat(loaded.Offset).IsEqual(1.5d);
        AssertThat(loaded.MeasurePosition!.Value).IsEqual(0d);
    }

    [TestCase]
    public void Reopening_resolves_audio_and_signature_changes()
    {
        SaveAndReloadSingleTimingPoint(0, 1, 0.5);
        Timing timing = Project.Instance.Timing;
        timing.IsInstantiating = true;
        timing.UpdateTimeSignature([3, 4], 0);
        timing.UpdateTimeSignature([5, 8], 2);
        timing.AddTimingPoint(4, 9, 0.625);
        timing.IsInstantiating = false;
        string path = ProjectSettings.GlobalizePath($"{SaveDir}/signatures.tmpr");

        ProjectFileManager.SaveProjectAs(path);
        ProjectFileManager.Instance.LoadProjectFromFilePath(path);

        AssertThat(Project.Instance.AudioFile.FilePath).IsEqual(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "signatures.ogg"));
        AssertThat(Project.Instance.Timing.GetTimeSignature(0)).IsEqual(new[] { 3, 4 });
        AssertThat(Project.Instance.Timing.GetTimeSignature(4)).IsEqual(new[] { 5, 8 });
        AssertThat(Project.Instance.Timing.TimingPoints[^1].MeasuresPerSecond).IsEqual(0.625);
    }

    [TestCase]
    public void Save_then_load_preserves_double_precision()
    {
        // Checks that the G17 precision values the old ProjectFileTimingSerializationTests covered survive a real file round-trip.
        double offset = 2.473544123456789;
        double measurePosition = 310.00000000000006;
        double measuresPerSecond = TimingMath.BpmToMeasuresPerSecond(154.99335007388657, [4, 4]);

        TimingPoint loaded = SaveAndReloadSingleTimingPoint(measurePosition, offset, measuresPerSecond);

        AssertThat(loaded.Offset).IsEqual(offset);
        AssertThat(loaded.MeasurePosition!.Value).IsEqual(measurePosition);
        AssertThat(loaded.MeasuresPerSecond).IsEqual(measuresPerSecond);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Opening_document_resets_undo_history(bool standalone)
    {
        SaveAndReloadSingleTimingPoint(0, 1, 0.5);
        string savePath = Project.Instance.ProjectPath!;
        string audioPath = Project.Instance.AudioFile.FilePath;
        AddTwoPointsToUndoHistory();

        if (standalone)
            ProjectFileManager.Instance.LoadAudioFromFilePath(audioPath);
        else
            ProjectFileManager.Instance.LoadProjectFromFilePath(savePath);
        int expectedPoints = standalone ? 0 : 1;
        AssertThat(Project.Instance.Timing.TimingPoints.Count).IsEqual(expectedPoints);
        AssertThat(FileHandler.LoadText("user://autosave.tmpr"))
            .IsEqual(ProjectFileCodec.Serialize(Project.Instance.Timing, "autosave.ogg"));

        MementoHandler.Instance.Undo();

        AssertThat(Project.Instance.Timing.TimingPoints.Count).IsEqual(expectedPoints);
    }

    private static void AddTwoPointsToUndoHistory()
    {
        Project.Instance.Edits.Apply(() => Project.Instance.Timing.AddTimingPoint(2, 3, 0.5));
        Project.Instance.Edits.Apply(() => Project.Instance.Timing.AddTimingPoint(3, 4, 0.5));
    }

    [TestCase("missing-audio")]
    [TestCase("invalid-timing")]
    [TestCase("standalone-audio")]
    [TestCase("missing-project")]
    [TestCase("corrupt-audio")]
    [TestCase("low-rate-project")]
    [TestCase("low-rate-audio")]
    public void Failed_open_preserves_edits_and_undo(string failure)
    {
        SaveAndReloadSingleTimingPoint(0, 1, 0.5);
        Project.Instance.Timing.AddTimingPoint(2, 5, 0.5);
        MementoHandler.Instance.AddTimingMemento();
        Timing original = Project.Instance.Timing;
        AudioFile audio = Project.Instance.AudioFile;
        string? path = Project.Instance.ProjectPath;
        Context.Instance.HeldTimingPoint = original.TimingPoints[^1];
        TimingPoint heldPoint = Context.Instance.HeldTimingPoint;
        string brokenPath = ProjectSettings.GlobalizePath($"{SaveDir}/broken.tmpr");
        string audioPath = failure == "missing-audio" ? "missing.ogg" : audio.FilePath;
        if (failure is "low-rate-project" or "low-rate-audio")
        {
            audioPath = ProjectSettings.GlobalizePath("res://Tests/Fixtures/low-rate.ogg");
            AssertThat(new AudioFile(audioPath).SampleRate).IsEqual(4000);
        }
        if (failure == "corrupt-audio")
        {
            audioPath = brokenPath + ".ogg";
            FileHandler.SaveText(audioPath, "This is not an Ogg stream.");
        }
        string points = failure == "invalid-timing" ? "1;0;4;4\n2;0;4;4\n" : "0;0;4;4;0.5\n";
        FileHandler.SaveText(brokenPath, $"[AudioPath]\n{audioPath}\n[TimingPoints]\n{points}");
        int notifications = 0;
        void OnChanged(object? sender, EventArgs args) => notifications++;
        GlobalEvents.Instance.TimingChanged += OnChanged;
        try
        {
            AssertThrown(() =>
            {
                if (failure == "standalone-audio")
                    ProjectFileManager.Instance.LoadAudioFromFilePath(brokenPath + ".ogg");
                else if (failure == "low-rate-audio")
                    ProjectFileManager.Instance.LoadAudioFromFilePath(audioPath);
                else
                    ProjectFileManager.Instance.LoadProjectFromFilePath(failure == "missing-project" ? brokenPath + ".missing" : brokenPath);
            }).IsInstanceOf<Exception>();

            AssertThat(ReferenceEquals(Project.Instance.Timing, original)).IsTrue();
            AssertThat(ReferenceEquals(Project.Instance.AudioFile, audio)).IsTrue();
            AssertThat(Project.Instance.ProjectPath).IsEqual(path!);
            AssertThat(ReferenceEquals(Context.Instance.HeldTimingPoint, heldPoint)).IsTrue();
            AssertThat(notifications).IsEqual(0);
            AssertThat(original.TimingPoints.Count).IsEqual(2);
            MementoHandler.Instance.Undo();
            AssertThat(Project.Instance.Timing.TimingPoints.Count).IsEqual(1);
        }
        finally
        {
            GlobalEvents.Instance.TimingChanged -= OnChanged;
            Context.Instance.HeldTimingPoint = null;
        }
    }

    [TestCase(".ogg")]
    [TestCase(".tmpr")]
    public void Failed_save_preserves_current_project_path(string blockedExtension)
    {
        SaveAndReloadSingleTimingPoint(0, 1, 0.5);
        string originalPath = Project.Instance.ProjectPath!;
        string path = ProjectSettings.GlobalizePath($"{SaveDir}/blocked.tmpr");
        string blockedPath = Path.ChangeExtension(path, blockedExtension);
        Directory.CreateDirectory(blockedPath);
        try
        {
            AssertThrown(() => ProjectFileManager.SaveProjectAs(path)).IsInstanceOf<IOException>();
            AssertThat(Project.Instance.ProjectPath).IsEqual(originalPath);
        }
        finally
        {
            Directory.Delete(blockedPath);
        }
    }

    private TimingPoint SaveAndReloadSingleTimingPoint(double measurePosition, double offset, double measuresPerSecond)
    {
        // AudioFile decodes via NAudio directly, so it needs an OS path rather than a Godot res:// virtual path.
        string audioPath = ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg");
        Project.Instance.AudioFile = new AudioFile(audioPath);

        Project.Instance.Timing = new Timing(Project.Instance.Settings) { IsInstantiating = true };
        Project.Instance.Timing.AddTimingPoint(measurePosition, offset, measuresPerSecond);
        Project.Instance.Timing.IsInstantiating = false;

        // In production, saving goes to an OS path chosen by the file dialog (user:// is for AutoSave only).
        // Passing a user:// subpath as-is would be broken by Path.GetDirectoryName (backslashes), so convert to an OS path.
        string savePath = ProjectSettings.GlobalizePath($"{SaveDir}/project.tmpr");
        ProjectFileManager.SaveProjectAs(savePath);
        ProjectFileManager.Instance.LoadProjectFromFilePath(savePath);

        return Project.Instance.Timing.TimingPoints[0];
    }
}