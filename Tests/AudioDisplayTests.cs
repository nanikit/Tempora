namespace Tempora.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using Godot;
using NAudio.Wave;
using NLayer.NAudioSupport;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class AudioDisplayTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Mp3_display_preserves_decoded_duration_and_samples(bool embedded)
    {
        string path = ProjectSettings.GlobalizePath("res://Audio/Loop.mp3");
        var audio = embedded ? new PcmData(File.ReadAllBytes(path), ".mp3") : new PcmData(path);
        var expected = ReadReferenceSamples(path);
        AssertThat(audio.PcmFloats[0].Length).IsEqual(expected.Samples.Length / expected.Channels);
        AssertThat(LargestSampleError(audio, expected.Samples)).IsLess(0.0001d);
    }

    [TestCase("res://Audio/Loop.mp3")]
    [TestCase("res://Audio/click-quick.ogg")]
    public void Spectrogram_starts_at_the_selected_minimum_frequency(string path)
    {
        var audio = new PcmData(ProjectSettings.GlobalizePath(path));
        var spectrogram = SpectrogramHelper.GetSpectrogramGenerator(audio, stepSize: 64, fftSize: 256, minFreq: 2200);
        AssertThat(spectrogram.FreqMin).IsBetween(2200d - spectrogram.HzPerPx, 2200d);
        AssertThat(spectrogram.FreqMax).IsEqual(audio.SampleRate / 2d);
    }

    [TestCase]
    public void Spectrogram_slice_fits_the_row_when_only_width_changes()
    {
        using var source = Image.CreateEmpty(40, 12, false, Image.Format.Rgba8);
        source.Fill(Colors.White);
        using var texture = SpectrogramHelper.GetSpectrogramSlice(source, 0, 40, 12, 100);
        AssertThat(texture.GetSize()).IsEqual(new Vector2(100, 12));
    }

    [TestCase]
    public void Very_short_audio_segments_fit_without_empty_textures()
    {
        var audio = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/clickedy.mp3"));
        var context = new SpectrogramContext(SpectrogramHelper.GetSpectrogramGenerator(audio), () => audio);
        var segment = new SpectrogramSegment(audio, context, 100, 20, [0, 0.1f]);
        try
        {
            segment.InstantiateAndRender(audio, context, 0.25f, 20, [0, 0.001f]);
            AssertThat(segment.Texture.GetSize() * segment.Scale).IsEqual(new Vector2(0.25f, 20));
            segment.InstantiateAndRender(audio, context, 100, 0, [0, 0.1f]);
            AssertThat(segment.Texture).IsNull();
            segment.InstantiateAndRender(audio, context, 100, 20, [0, 0.1f]);
            AssertThat(segment.Texture.GetSize() * segment.Scale).IsEqual(new Vector2(100, 20));
        }
        finally
        {
            segment.Free();
        }
    }

    [TestCase]
    public void Rows_outside_the_song_remain_empty_after_timing_changes()
    {
        AudioFile originalAudio = Project.Instance.AudioFile;
        Timing originalTiming = Timing.Instance;
        var settings = new Settings { DownbeatPositionOffset = 0, MeasureOverlap = 0 };
        var panel = new AudioDisplayPanel { Settings = settings };
        try
        {
            Project.Instance.AudioFile = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/clickedy.mp3"));
            var timing = new Timing(settings) { IsInstantiating = true };
            Timing.Instance = timing;
            timing.AddTimingPoint(-0.5, -1, 0.5);
            timing.AddTimingPoint(0.25, 0.5, 0.5);
            timing.AddTimingPoint(100, 200, 0.5);

            panel.NominalMeasurePosition = -1;
            AssertThat(panel.GetPanelSegments().Count).IsEqual(0);
            panel.NominalMeasurePosition = 0;
            var segments = panel.GetPanelSegments();
            AssertThat(segments.Count).IsEqual(2);
            AssertThat(segments[0]).IsEqual(new[] { 0f, 0.5f });
            AssertThat(segments[1]).IsEqual(new[] { 0.5f, 2f });
            int lastMeasure = (int)(Project.Instance.AudioFile.GetAudioLength() / 2);
            panel.NominalMeasurePosition = lastMeasure;
            segments = panel.GetPanelSegments();
            AssertThat(segments.Count).IsEqual(1);
            AssertThat(segments[0][1]).IsEqual(Project.Instance.AudioFile.GetAudioLength());
            panel.NominalMeasurePosition = lastMeasure + 1;
            AssertThat(panel.GetPanelSegments().Count).IsEqual(0);
            panel.NominalMeasurePosition = 100;
            AssertThat(panel.GetPanelSegments().Count).IsEqual(0);
        }
        finally
        {
            panel.Free();
            Timing.Instance = originalTiming;
            if (originalAudio != null)
                Project.Instance.AudioFile = originalAudio;
        }
    }

    private static (float[] Samples, int Channels) ReadReferenceSamples(string path)
    {
        using var reader = new Mp3FileReaderBase(path, format => new Mp3FrameDecompressor(format));
        ISampleProvider samples = reader.ToSampleProvider();
        var decoded = new List<float>();
        float[] buffer = new float[4096];
        int count;
        while ((count = samples.Read(buffer, 0, buffer.Length)) > 0)
            decoded.AddRange(buffer.AsSpan(0, count).ToArray());
        return (decoded.ToArray(), samples.WaveFormat.Channels);
    }

    private static double LargestSampleError(PcmData audio, float[] expected)
    {
        double largestError = 0;
        for (int index = 0; index < expected.Length; index++)
        {
            float actual = audio.PcmFloats[index % audio.Channels][index / audio.Channels];
            largestError = Math.Max(largestError, Math.Abs(actual - Math.Clamp(expected[index], -1f, 1f)));
        }
        return largestError;
    }
}