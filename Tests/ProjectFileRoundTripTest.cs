namespace Tempora.Tests;

using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
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

        AssertThat(Timing.Instance.TimingPoints.Count).IsEqual(1);
        AssertThat(loaded.Offset).IsEqual(1.5d);
        AssertThat(loaded.MeasurePosition!.Value).IsEqual(0d);
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

    private TimingPoint SaveAndReloadSingleTimingPoint(double measurePosition, double offset, double measuresPerSecond)
    {
        // AudioFile decodes via NAudio directly, so it needs an OS path rather than a Godot res:// virtual path.
        string audioPath = ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg");
        Project.Instance.AudioFile = new AudioFile(audioPath);

        Timing.Instance = new Timing { IsInstantiating = true };
        Timing.Instance.AddTimingPoint(measurePosition, offset, measuresPerSecond);
        Timing.Instance.IsInstantiating = false;

        // In production, saving goes to an OS path chosen by the file dialog (user:// is for AutoSave only).
        // Passing a user:// subpath as-is would be broken by Path.GetDirectoryName (backslashes), so convert to an OS path.
        string savePath = ProjectSettings.GlobalizePath($"{SaveDir}/project.tmpr");
        ProjectFileManager.SaveProjectAs(savePath);
        ProjectFileManager.Instance.LoadProjectFromFilePath(savePath);

        return Timing.Instance.TimingPoints[0];
    }
}