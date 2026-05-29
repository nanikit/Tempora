namespace Tempora.Tests;

using System.Globalization;
using GdUnit4;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

// Pure format/parse functions, so no Godot runtime is needed.
[TestSuite]
public class ProjectFileNumberFormatTests
{
    [TestCase]
    public void FormatFloatForProjectFile_uses_invariant_round_trip_precision()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

        try
        {
            float value = 310.00009f;

            string formatted = ProjectFileManager.FormatFloatForProjectFile(value);

            AssertThat(formatted).Contains(".");
            AssertThat(formatted).NotContains(",");
            AssertThat(float.Parse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture)).IsEqual(value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestCase]
    public void FormatDoubleForProjectFile_uses_invariant_round_trip_precision()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

        try
        {
            double value = 154.99335007388657;

            string formatted = ProjectFileManager.FormatDoubleForProjectFile(value);

            AssertThat(formatted).Contains(".");
            AssertThat(formatted).NotContains(",");
            AssertThat(double.Parse(formatted, NumberStyles.Float, CultureInfo.InvariantCulture)).IsEqual(value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestCase]
    public void TryParseFloatFromProjectFile_accepts_round_trip_float_format()
    {
        AssertThat(ProjectFileManager.TryParseFloatFromProjectFile("1.23456789E-05", out float value)).IsTrue();
        AssertThat(value).IsEqual(1.23456789E-05f);
    }

    [TestCase]
    public void TryParseDoubleFromProjectFile_accepts_round_trip_double_format()
    {
        AssertThat(ProjectFileManager.TryParseDoubleFromProjectFile("154.99335007388657", out double value)).IsTrue();
        AssertThat(value).IsEqual(154.99335007388657);
    }
}
