namespace Tempora.Tests;

using Godot;
using GdUnit4;
using System.IO;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingEditTests
{
    [TestCase]
    public void Returning_after_an_intermediate_save_saves_the_start_again()
    {
        var project = Project.Instance;
        Timing original = project.Timing;
        var originalAudio = project.AudioFile;
        try
        {
            project.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
            project.Timing = CreateTiming();
            MementoHandler.Instance.ResetTimingHistory();
            TimingPoint point = project.Timing.TimingPoints[1];
            project.Edits.Apply(() => project.Timing.TrySetOffset(point, 2.1));
            string saved = FileHandler.LoadText("user://autosave.tmpr");
            int committed = 0;
            project.Edits.Committed += _ => committed++;

            project.Edits.Begin();
            project.Edits.Apply(() => project.Timing.TrySetOffset(point, 2.2));
            AssertThat(FileHandler.LoadText("user://autosave.tmpr")).IsEqual(saved);
            ProjectFileManager.AutoSave();
            AssertThat(FileHandler.LoadText("user://autosave.tmpr")).IsNotEqual(saved);
            project.Edits.Apply(() => project.Timing.TrySetOffset(point, 2.1));
            project.Edits.Commit();

            AssertThat(FileHandler.LoadText("user://autosave.tmpr")).IsEqual(saved);
            AssertThat(committed).IsEqual(0);
            MementoHandler.Instance.Undo();
            AssertThat(project.Timing.TimingPoints[1].Offset).IsEqual(2d);
            AssertThat(FileHandler.LoadText("user://autosave.tmpr"))
                .IsEqual(ProjectFileCodec.Serialize(project.Timing, "autosave.ogg"));
        }
        finally
        {
            project.Timing = original;
            if (originalAudio != null)
                project.AudioFile = originalAudio;
            MementoHandler.Instance.ResetTimingHistory();
        }
    }

    [TestCase]
    public void Failed_autosave_does_not_skip_restoring_the_start()
    {
        var project = Project.Instance;
        Timing original = project.Timing;
        var originalAudio = project.AudioFile;
        string audioPath = ProjectSettings.GlobalizePath("user://autosave.ogg");
        try
        {
            project.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
            project.Timing = CreateTiming();
            ProjectFileManager.AutoSave();
            string saved = FileHandler.LoadText("user://autosave.tmpr");
            project.Edits.Begin();
            project.Edits.Apply(() => project.Timing.TrySetOffset(project.Timing.TimingPoints[1], 2.1));
            File.Delete(audioPath);
            Directory.CreateDirectory(audioPath);
            AssertThrown(ProjectFileManager.AutoSave).IsInstanceOf<IOException>();
            Directory.Delete(audioPath);

            project.Edits.Cancel();
            AssertThat(File.Exists(audioPath)).IsTrue();
            AssertThat(FileHandler.LoadText("user://autosave.tmpr")).IsEqual(saved);
        }
        finally
        {
            if (Directory.Exists(audioPath))
                Directory.Delete(audioPath);
            project.Timing = original;
            if (originalAudio != null)
                project.AudioFile = originalAudio;
            MementoHandler.Instance.ResetTimingHistory();
        }
    }

    [TestCase]
    public void Rejected_group_moves_leave_every_point_unchanged()
    {
        var timing = CreateTiming();
        timing.TimingPoints[1].Offset = 3;
        timing.TimingPoints[1].MeasurePosition = 1.5;
        var edits = new TimingEditSession(timing);
        IMemento before = timing.GetMemento();
        int committed = 0;
        edits.Committed += _ => committed++;
        edits.Apply(() => timing.BatchChangeOffset(0, 1, 1.5));
        AssertThat(timing.Matches(before)).IsTrue();
        edits.Apply(() => timing.BatchChangeMeasurePosition(0, 1, point => point!.MeasurePosition!.Value + 0.75));
        AssertThat(timing.Matches(before)).IsTrue();
        AssertThat(committed).IsEqual(0);
    }

    [TestCase]
    public void Cancelling_a_drag_restores_all_of_its_changes()
    {
        var timing = CreateTiming();
        var edits = new TimingEditSession(timing);
        IMemento before = timing.GetMemento();
        int committed = 0;
        edits.Committed += _ => committed++;
        edits.Begin();
        edits.Apply(() => timing.TrySetOffset(timing.TimingPoints[1], 2.1));
        edits.Apply(() => timing.TrySetOffset(timing.TimingPoints[1], 2.2));
        edits.Cancel();
        AssertThat(timing.Matches(before)).IsTrue();
        AssertThat(committed).IsEqual(0);
    }

    private static Timing CreateTiming()
    {
        var timing = new Timing(new Settings()) { IsInstantiating = true };
        timing.AddTimingPoint(0, 0, 0.5);
        timing.AddTimingPoint(1, 2, 0.5);
        timing.AddTimingPoint(2, 4, 0.5);
        timing.IsInstantiating = false;
        return timing;
    }
}
