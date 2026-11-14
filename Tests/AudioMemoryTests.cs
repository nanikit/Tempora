namespace Tempora.Tests;

using System;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class AudioMemoryTests
{
    [TestCase("res://Audio/click-quick.ogg")]
    [TestCase("res://Tests/Fixtures/low-rate.ogg")]
    public void Opening_ogg_preserves_every_channel_and_playback(string resource)
    {
        string path = ProjectSettings.GlobalizePath(resource);
        AudioDataHelper.DecodeOgg(path, out var expected, out int channels, out int rate, out _);
        var audio = new AudioFile(path);
        using var stream = audio.Stream;
        AssertThat(audio.Channels).IsEqual(channels);
        AssertThat(audio.SampleRate).IsEqual(rate);
        AssertThat(audio.PlaybackOriginSample).IsEqual(0);
        AssertThat(audio.FileBuffer).IsEqual(File.ReadAllBytes(path));
        AssertThat(stream is AudioStreamOggVorbis).IsTrue();
        AssertThat(ReferenceEquals(audio.PcmLeft, audio.PcmFloats[0])).IsTrue();
        for (int channel = 0; channel < channels; channel++)
            AssertThat(audio.PcmFloats[channel]).IsEqual(expected.Where((_, index) => index % channels == channel).ToArray());
        AssertThat(audio.GetPcmAsDoubles(2)).IsEqual(audio.PcmLeft.Select(value => (double)value * 2).ToArray());
    }

    [TestCase("res://Audio/Loop.mp3", false)]
    [TestCase("res://Audio/Loop.mp3", true)]
    [TestCase("res://Audio/click-quick.mp3", false)]
    public void Opening_mp3_preserves_quantization_and_origin(string resource, bool embedded)
    {
        string path = ProjectSettings.GlobalizePath(resource);
        byte[] file = File.ReadAllBytes(path);
        AudioDataHelper.DecodeMp3(file, out var expected, out int channels, out int rate, out _, out int origin);
        var audio = embedded ? new AudioFile(new AudioStreamMP3 { Data = file }) : new AudioFile(path);
        using var stream = audio.Stream;
        AssertThat(audio.Channels).IsEqual(channels);
        AssertThat(audio.SampleRate).IsEqual(rate);
        AssertThat(audio.PlaybackOriginSample).IsEqual(origin);
        AssertThat(audio.SampleTimeToPlaybackTime(0)).IsEqual(-origin / (float)rate);
        AssertThat(audio.FileBuffer).IsEqual(file);
        AssertThat(((AudioStreamMP3)stream).Data).IsEqual(file);
        for (int channel = 0; channel < channels; channel++)
        {
            var samples = new float[expected.Length / sizeof(short) / channels];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = (float)BitConverter.ToInt16(expected, (index * channels + channel) * sizeof(short)) / short.MaxValue;
            AssertThat(audio.PcmFloats[channel]).IsEqual(samples);
        }
    }

    [TestCase]
    public void Metronome_preserves_stereo_extremes_and_silence()
    {
        byte[] samples = [0, 128, 255, 127, 0, 0, 255, 255, 1, 0, 0, 64];
        AssertThat(AudioDataHelper.ConvertToStereoVector2Floats(samples)).IsEqual(new[]
        {
            new Vector2((float)short.MinValue / short.MaxValue, 1),
            new Vector2(0, -1f / short.MaxValue),
            new Vector2(1f / short.MaxValue, 16384f / short.MaxValue)
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Decoding_pcm_preserves_all_16_bit_values(int channels)
    {
        byte[] bytes = new byte[65536 * sizeof(short)];
        for (int index = 0; index < 65536; index++)
        {
            bytes[index * 2] = (byte)index;
            bytes[index * 2 + 1] = (byte)(index >> 8);
        }
        var samples = AudioDataHelper.ConvertToFloats(bytes, channels);
        for (int channel = 0; channel < channels; channel++)
        {
            var expected = new float[65536 / channels];
            for (int index = 0; index < expected.Length; index++)
                expected[index] = (float)unchecked((short)(index * channels + channel)) / short.MaxValue;
            AssertThat(samples[channel]).IsEqual(expected);
        }
    }

    [TestCase]
    public void Decoding_audio_does_not_double_sample_memory()
    {
        byte[] bytes = new byte[5_008_148 * 2 * sizeof(short)];
        _ = AudioDataHelper.ConvertToFloats(new byte[4], 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var samples = AudioDataHelper.ConvertToFloats(bytes, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long resultBytes = (long)bytes.Length * 2;
        Console.WriteLine($"PCM conversion: allocated={allocated}, final float payload={resultBytes}");
        AssertThat(allocated).IsLess(resultBytes + 128);
        GC.KeepAlive(samples);
    }

    [TestCase]
    public void Loading_ogg_allocates_only_float_channel_storage()
    {
        string path = ProjectSettings.GlobalizePath("res://Audio/autosave.ogg");
        _ = new PcmData(path);
        long before = GC.GetAllocatedBytesForCurrentThread();
        AudioDataHelper.DecodeOgg(path, out var mixed, out int channels, out _, out _);
        long decoderBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var audio = new PcmData(path);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long channelBytes = (long)mixed.Length * sizeof(float);
        Console.WriteLine($"OGG: samples/channel={mixed.Length / channels}, channels={channels}, rate={audio.SampleRate}, decode={decoderBytes}, load={allocated}, final float payload={channelBytes}");
        AssertThat(allocated - decoderBytes).IsLess(channelBytes + 4096);
        GC.KeepAlive(audio);
    }

    [TestCase(11)]
    [TestCase(19)]
    [TestCase(20)]
    [TestCase(21)]
    [TestCase(29)]
    public void Waveform_keeps_existing_final_group_boundaries(int count)
    {
        var audio = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
        using var stream = audio.Stream;
        float[] samples = Enumerable.Range(0, count).Select(value => (float)value).ToArray();
        samples[9] = -100;
        samples[^1] = 1000;
        audio.PcmLeft = samples;
        int groups = (count + 9) / 10;
        AssertThat(audio.AudioDataPer10Min.Length).IsEqual(groups);
        for (int group = 0; group < groups; group++)
        {
            int start = group == groups - 1 ? group * 10 - 1 : group * 10;
            int end = group == groups - 1 ? count - 1 : start + 10;
            AssertThat(audio.AudioDataPer10Min[group]).IsEqual(samples[start..end].Min());
            AssertThat(audio.AudioDataPer10Max[group]).IsEqual(samples[start..end].Max());
        }
    }

    [TestCase]
    public void Waveform_keeps_nonfinite_and_signed_zero_extrema()
    {
        var audio = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
        using var stream = audio.Stream;
        float[] samples = [float.NaN, 2, -3, float.PositiveInfinity, float.NegativeInfinity, 0, 1, 0, 0, 0,
            -0f, 0, -0f, 0, 0, 0, 0, 0, 0, float.NaN, float.NaN];
        audio.PcmLeft = samples;
        AssertThat(float.IsNaN(audio.AudioDataPer10Min[0])).IsTrue();
        AssertThat(audio.AudioDataPer10Max[0]).IsEqual(float.PositiveInfinity);
        AssertThat(float.IsNaN(audio.AudioDataPer10Min[1])).IsTrue();
        AssertThat(BitConverter.SingleToInt32Bits(audio.AudioDataPer10Max[1])).IsEqual(BitConverter.SingleToInt32Bits(samples[10..20].Max()));
        AssertThat(float.IsNaN(audio.AudioDataPer10Min[2])).IsTrue();
        AssertThat(float.IsNaN(audio.AudioDataPer10Max[2])).IsTrue();
    }

    [TestCase]
    public void Preparing_waveforms_does_not_duplicate_the_song()
    {
        var audio = new AudioFile(ProjectSettings.GlobalizePath("res://Audio/click-quick.ogg"));
        using var stream = audio.Stream;
        float[] samples = new float[5_008_148];
        long before = GC.GetAllocatedBytesForCurrentThread();
        audio.PcmLeft = samples;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long resultBytes = (long)audio.AudioDataPer10Min.Length * sizeof(float) * 2;
        Console.WriteLine($"Waveform: samples={samples.Length}, allocated={allocated}, result payload={resultBytes}");
        AssertThat(allocated).IsLess(resultBytes + 128);
    }
}