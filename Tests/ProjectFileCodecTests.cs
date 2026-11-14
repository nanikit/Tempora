using GdUnit4;
using Tempora.Classes.DataHelpers;
using static GdUnit4.Assertions;

namespace Tempora.Tests;

[TestSuite]
public class ProjectFileCodecTests
{
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void Reading_project_skips_malformed_rows(string newline)
    {
        string file = string.Join(newline,
            "[AudioPath]", "song.ogg", "[TimeSignaturePoints]", "0;3;4", "bad;4;4", "1;4", "",
            "[TimingPoints]", "0;0;3;4", "bad;1;3;4", "2;1;bad;4", "2;1;3;4;bad",
            "2;1;3;4;0.6432198765432109", "");

        ProjectFileData data = ProjectFileCodec.Parse(file);

        AssertThat(data.AudioPath).IsEqual("song.ogg");
        AssertThat(data.TimeSignaturePoints.Count).IsEqual(1);
        AssertThat(data.TimeSignaturePoints[0]).IsEqual((0, 3, 4));
        AssertThat(data.TimingPoints.Count).IsEqual(2);
        AssertThat(data.TimingPoints[0].MeasuresPerSecond.HasValue).IsFalse();
        AssertThat(data.TimingPoints[1].MeasuresPerSecond!.Value).IsEqual(0.6432198765432109);
    }

    [TestCase]
    public void Legacy_header_and_exponents_remain_readable()
    {
        ProjectFileData data = ProjectFileCodec.Parse(
            "[AudioPath]\n../song.mp3\nTimingPoints\n1.23456789E-05;310.00000000000006;4;4;0.5\n");

        AssertThat(data.AudioPath).IsEqual("../song.mp3");
        AssertThat(data.TimingPoints.Count).IsEqual(1);
        AssertThat(data.TimingPoints[0].Offset).IsEqual(1.23456789E-05);
        AssertThat(data.TimingPoints[0].MeasurePosition).IsEqual(310.00000000000006);
    }
}