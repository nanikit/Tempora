namespace Tempora.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class BeatSaberExportTests
{
    [TestCase(3)]
    [TestCase(4)]
    public void Export_contains_playable_audio_and_map_files(int format)
    {
        AudioFile originalAudio = Project.Instance.AudioFile;
        Timing originalTiming = Project.Instance.Timing;
        var exporter = new BeatSaberExporter { Settings = new Settings { BeatSaberExportFormat = format } };
        string path = ProjectSettings.GlobalizePath($"user://export-{Guid.NewGuid():N}.zip");
        try
        {
            Project.Instance.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
            Project.Instance.Timing = new Timing(Project.Instance.Settings) { IsInstantiating = true };
            Project.Instance.Timing.AddTimingPoint(0, 0, 0.5);
            Project.Instance.Timing.IsInstantiating = false;

            exporter.SaveFilesToPath(Path.ChangeExtension(path, ".dat"));

            using var archive = ZipFile.OpenRead(path);
            string audioDataFile = format == 4 ? "AudioData.dat" : "BPMInfo.dat";
            string[] expectedFiles = ["Info.dat", audioDataFile, "ExpertPlusStandard.dat", "song.ogg"];
            if (format == 4)
                expectedFiles = [.. expectedFiles, "Lightshow.dat"];
            AssertThat(archive.Entries.Select(entry => entry.FullName).Order().ToArray()).IsEqual(expectedFiles.Order().ToArray());
            AssertThat(ReadEntry(archive, "song.ogg")).IsEqual(Project.Instance.AudioFile.FileBuffer);

            using var info = JsonDocument.Parse(ReadEntry(archive, "Info.dat"));
            string audioFilename = format == 4
                ? info.RootElement.GetProperty("audio").GetProperty("songFilename").GetString()!
                : info.RootElement.GetProperty("_songFilename").GetString()!;
            AssertThat(audioFilename).IsEqual("song.ogg");

            using var audioData = JsonDocument.Parse(ReadEntry(archive, audioDataFile));
            AssertThat(audioData.RootElement.GetProperty(format == 4 ? "songSampleCount" : "_songSampleCount").GetInt32())
                .IsEqual(Project.Instance.AudioFile.PcmLeft.Length - 1);

            using var difficulty = JsonDocument.Parse(ReadEntry(archive, "ExpertPlusStandard.dat"));
            AssertThat(difficulty.RootElement.GetProperty("version").GetString()).IsEqual(format == 4 ? "4.0.0" : "3.3.0");
            if (format == 4)
                AssertThat(ReadEntry(archive, "Lightshow.dat")).IsEqual(ReadEntry(archive, "ExpertPlusStandard.dat"));
        }
        finally
        {
            exporter.Free();
            Project.Instance.Timing = originalTiming;
            if (originalAudio != null)
                Project.Instance.AudioFile = originalAudio;
            File.Delete(path);
        }
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var input = archive.GetEntry(name)!.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
