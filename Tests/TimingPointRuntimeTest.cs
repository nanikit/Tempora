namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using static GdUnit4.Assertions;

/// <summary>
/// The TimingPoint constructor calls Godot's Time.GetTicksMsec(), so it cannot be created outside the runtime;
/// the previous xUnit tests had to bypass the constructor with RuntimeHelpers.GetUninitializedObject.
/// This pins that the real constructor path works under the gdUnit4 runtime.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TimingPointRuntimeTest
{
    [TestCase]
    public void Constructs_via_real_constructor_under_godot_runtime()
    {
        var timingPoint = AutoFree(new TimingPoint(time: 2.473544123456789, measurePosition: 310d, timeSignature: [4, 4]))!;

        AssertThat(timingPoint.Offset).IsEqual(2.473544123456789);
        AssertThat(timingPoint.MeasurePosition!.Value).IsEqual(310d);
    }
}