using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Tempora.Classes.TimingClasses;

namespace Tempora.Classes.DataHelpers;

public sealed record ProjectFileData(
    string AudioPath,
    IReadOnlyList<(int Measure, int Upper, int Lower)> TimeSignaturePoints,
    IReadOnlyList<(double Offset, double MeasurePosition, double? MeasuresPerSecond)> TimingPoints);

public static class ProjectFileCodec
{
    public static ProjectFileData Parse(string text)
    {
        string audioPath = "";
        var signatures = new List<(int, int, int)>();
        var points = new List<(double, double, double?)>();
        string section = "";
        foreach (string line in text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
        {
            if (line is "[AudioPath]" or "[TimeSignaturePoints]" or "[TimingPoints]" or "TimingPoints")
            {
                section = line == "TimingPoints" ? "[TimingPoints]" : line;
                continue;
            }

            switch (section)
            {
                case "[AudioPath]":
                    audioPath = line;
                    break;
                case "[TimeSignaturePoints]":
                    if (ParseTimeSignature(line) is { } signature)
                        signatures.Add(signature);
                    break;
                case "[TimingPoints]":
                    if (ParseTimingPoint(line) is { } point)
                        points.Add(point);
                    break;
            }
        }
        return new(audioPath, signatures, points);
    }

    public static string Serialize(Timing timing, string audioPath)
    {
        var file = new StringBuilder();
        file.Append("[AudioPath]\n").Append(audioPath).Append("\n[TimeSignaturePoints]\n");
        foreach (TimeSignaturePoint point in timing.TimeSignaturePoints)
        {
            file.Append(point.Measure.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(point.TimeSignature[0]).Append(';').Append(point.TimeSignature[1]).Append('\n');
        }
        file.Append("[TimingPoints]\n");
        foreach (TimingPoint point in timing.TimingPoints)
            AppendTimingPoint(file, point, point == timing.TimingPoints[^1]);
        return file.ToString();
    }

    public static string FormatFloatForProjectFile(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    public static string FormatDoubleForProjectFile(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    public static bool TryParseFloatFromProjectFile(string value, out float result)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    public static bool TryParseDoubleFromProjectFile(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static void AppendTimingPoint(StringBuilder file, TimingPoint point, bool isLast)
    {
        if (point.MeasurePosition is not double measurePosition)
            return;
        file.Append(FormatDoubleForProjectFile(point.Offset)).Append(';')
            .Append(FormatDoubleForProjectFile(measurePosition)).Append(';')
            .Append(point.TimeSignature[0]).Append(';').Append(point.TimeSignature[1]);
        if (isLast)
            file.Append(';').Append(FormatDoubleForProjectFile(point.MeasuresPerSecond));
        file.Append('\n');
    }

    private static (int, int, int)? ParseTimeSignature(string line)
    {
        string[] fields = line.Split(';');
        if (fields.Length != 3
            || !int.TryParse(fields[0], out int measure)
            || !int.TryParse(fields[1], out int upper)
            || !int.TryParse(fields[2], out int lower))
            return null;
        return (measure, upper, lower);
    }

    private static (double, double, double?)? ParseTimingPoint(string line)
    {
        string[] fields = line.Split(';');
        if (fields.Length is not 4 and not 5
            || !TryParseDoubleFromProjectFile(fields[0], out double offset)
            || !TryParseDoubleFromProjectFile(fields[1], out double measurePosition)
            || !int.TryParse(fields[2], out _)
            || !int.TryParse(fields[3], out _))
            return null;
        double? measuresPerSecond = null;
        if (fields.Length == 5)
        {
            if (!TryParseDoubleFromProjectFile(fields[4], out double parsed))
                return null;
            measuresPerSecond = parsed;
        }
        return (offset, measurePosition, measuresPerSecond);
    }
}
