using GdUnit4;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

namespace Tempora.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ProjectFileSerializationTests
{
    [TestCase]
    public void Saving_uses_supplied_document_and_last_tempo()
    {
        var settings = new Settings();
        var timing = new Timing(settings)
        {
            TimeSignaturePoints = [new TimeSignaturePoint([3, 4], 0)],
            TimingPoints =
            [
                new TimingPoint(settings, 1.25, 0, [3, 4], 0.5),
                new TimingPoint(settings, 2, [3, 4]),
                new TimingPoint(settings, 5.25, 2, [3, 4], 0.625)
            ]
        };

        string text = ProjectFileCodec.Serialize(timing, "../song.ogg");

        AssertThat(text).IsEqual("[AudioPath]\n../song.ogg\n[TimeSignaturePoints]\n0;3;4\n[TimingPoints]\n1.25;0;3;4\n5.25;2;3;4;0.625\n");
    }
}