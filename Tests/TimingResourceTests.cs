namespace Tempora.Tests;

using GdUnit4;
using Godot;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingResourceTests
{
    [TestCase]
    public void Editing_copying_and_restoring_timing_leaves_no_orphan_nodes()
    {
        Timing originalTiming = Timing.Instance;
        int orphanCount = Node.GetOrphanNodeIds().Count;
        Timing timing = new Timing { IsInstantiating = true };
        Timing.Instance = timing;
        try
        {
            timing.AddTimingPoint(0d, 0d, 0.5d);
            timing.AddTimingPoint(2d, 4d, 0.5d);
            timing.UpdateTimeSignature([3, 4], 2);
            IMemento saved = timing.GetMemento();

            timing.TimingPoints[0].Offset = -1d;
            timing.RestoreMemento(saved);
            Timing copy = Timing.CopyTiming(timing);
            copy.TimingPoints[0].Offset = -2d;

            AssertThat(timing.TimingPoints[0].Offset).IsEqual(0d);
            AssertThat(timing.GetTimeSignature(2)).IsEqual(new[] { 3, 4 });
            AssertThat(Node.GetOrphanNodeIds().Count).IsEqual(orphanCount);
        }
        finally
        {
            Timing.Instance = originalTiming;
            MementoHandler.Instance.ResetTimingHistory();
        }
    }
}