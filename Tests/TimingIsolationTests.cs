namespace Tempora.Tests;

using System;
using Godot;
using Tempora.Classes.Audio;
using GdUnit4;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingIsolationTests
{
    [TestCase]
    public void Only_the_open_project_publishes_edits_after_switching()
    {
        Timing original = Project.Instance.Timing;
        var originalAudio = Project.Instance.AudioFile;
        Project.Instance.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
        var open = new Timing(new Settings()) { IsInstantiating = true };
        Project.Instance.Timing = open;
        open.AddTimingPoint(0, 0, 0.5);
        open.AddTimingPoint(2, 4, 0.5);
        open.IsInstantiating = false;
        int notifications = 0;
        void OnChanged(object? sender, EventArgs args) => notifications++;
        GlobalEvents.Instance.TimingChanged += OnChanged;
        try
        {
            Timing copy = Timing.CloneAndParseForBeatSaber(open);
            AssertThat(copy.CanBpmBeChangedManually(copy.TimingPoints[^1])).IsTrue();
            copy.UpdateTimeSignature([3, 4], 0);
            copy.TimingPoints[^1].SetManualBpm(160);
            AssertThat(notifications).IsEqual(0);
            AssertThat(open.TimingPoints[0].TimeSignature).IsEqual(new[] { 4, 4 });
            AssertThat(open.TimingPoints[^1].Bpm).IsEqual(120d);

            Project.Instance.Timing = copy;
            open.TimingPoints[^1].SetManualBpm(180);
            AssertThat(notifications).IsEqual(0);
            copy.TimingPoints[^1].SetManualBpm(170);
            AssertThat(notifications).IsGreater(0);
        }
        finally
        {
            GlobalEvents.Instance.TimingChanged -= OnChanged;
            Project.Instance.Timing = original;
            if (originalAudio != null)
                Project.Instance.AudioFile = originalAudio;
        }
    }

    [TestCase("selection", true)]
    [TestCase("signature", true)]
    [TestCase("tempo", true)]
    [TestCase("single", false)]
    [TestCase("single", true)]
    public void Undo_restores_the_start_of_an_in_progress_edit(string edit, bool addingPoint)
    {
        Timing original = Project.Instance.Timing;
        var originalAudio = Project.Instance.AudioFile;
        bool originallyAddingPoint = Context.Instance.HeldPointIsJustBeingAdded;
        var open = new Timing(new Settings()) { IsInstantiating = true };
        Project.Instance.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
        Project.Instance.Timing = open;
        try
        {
            open.AddTimingPoint(0, 0, 0.5);
            open.AddTimingPoint(2, 4, 0.5);
            open.IsInstantiating = false;
            MementoHandler.Instance.ResetTimingHistory();
            string beforeAdding = ProjectFileManager.CreateProjectFileString("audio.ogg");
            if (addingPoint)
                Project.Instance.Edits.Begin();
            Project.Instance.Edits.Apply(() => open.AddTimingPoint(4, 8, 0.5));
            if (addingPoint)
                Context.Instance.HeldTimingPoint = open.TimingPoints[^1];
            string beforeEdit = ProjectFileManager.CreateProjectFileString("audio.ogg");
            Context.Instance.HeldPointIsJustBeingAdded = addingPoint;

            switch (edit)
            {
                case "selection":
                    TimingPointSelection.Instance.SelectAllFrom(0);
                    TimingPointSelection.Instance.DeleteSelection();
                    break;
                case "signature":
                    Project.Instance.Edits.Apply(() => open.UpdateTimeSignature([3, 4], 0));
                    break;
                case "tempo":
                    TimingPointSelection.Instance.ScaleTempo(open.TimingPoints[0], 2);
                    break;
                case "single":
                    Project.Instance.Edits.Apply(open.TimingPoints[0].Delete);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edit));
            }

            AssertThat(ProjectFileManager.CreateProjectFileString("audio.ogg")).IsNotEqual(beforeEdit);
            MementoHandler.Instance.Undo();
            string expected = addingPoint ? beforeAdding : beforeEdit;
            AssertThat(ProjectFileManager.CreateProjectFileString("audio.ogg")).IsEqual(expected);
        }
        finally
        {
            Context.Instance.HeldTimingPoint = null;
            Context.Instance.HeldPointIsJustBeingAdded = originallyAddingPoint;
            TimingPointSelection.Instance.DeselectAll();
            Project.Instance.Timing = original;
            MementoHandler.Instance.ResetTimingHistory();
            if (originalAudio != null)
                Project.Instance.AudioFile = originalAudio;
        }
    }
}
