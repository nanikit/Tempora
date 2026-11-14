// Copyright 2024 https://github.com/kongehund
// 
// This file is licensed under the Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International (CC BY-NC-ND 4.0).
// You are free to:
// - Share, copy and redistribute the material in any medium or format
//
// Under the following terms:
// - Attribution - You must give appropriate credit, provide a link to the license, and indicate if changes were made.
// - NonCommercial - You may not use the material for commercial purposes.
// - NoDerivatives - If you remix, transform, or build upon the material, you may not distribute the modified material.
//
// Full license text is available at: https://creativecommons.org/licenses/by-nc-nd/4.0/legalcode

using System;
using System.IO;
using System.Linq;
using Tempora.Classes.DataHelpers;
// This script is partially based on https://github.com/leezer3/OpenBVE/blob/84064b7ef4e51def0b26e07226c114c004bcd4d3/source/OpenBveApi/Sound.cs whose license is public domain.

namespace Tempora.Classes.Audio;


/// <summary>
/// Stores audio PCM data as <see cref="PcmFloats"/>.
/// <para>"PCM": Pulse-code modulation. 
/// Any format to store audio where the audio is split up into evenly-spaced samples with a given <see cref="SampleRate"/> i.e. 41000 samples/second.
/// The value for each sample is the amplitude of the sample, normally between -1 and 1.</para>
/// </summary>
public class PcmData
{
    #region fields
    protected int sampleRate;
    protected float[][] pcmFloats = [];
    protected int channels;
    protected int startSilenceSamples = 0;
    #endregion
    #region constructors
    /// <param name="sampleRate">The number of samples per second.</param>
    /// <param name="pcmFloats">The PCM sound data per channel. </param>
    /// <exception cref="System.ArgumentNullException">Raised when the samples array or any of its subarrays is a null reference.</exception>
    /// <exception cref="System.ArgumentException">Raised when the channels have unequal lengths.</exception>
    public PcmData(int sampleRate, float[][] pcmFloats, int playbackOriginSample = 0)
    {
        if (pcmFloats == null)
        {
            throw new ArgumentNullException(nameof(pcmFloats));
        }
        for (int i = 0; i < pcmFloats.Length; i++)
        {
            if (pcmFloats[i] == null)
            {
                throw new ArgumentNullException(nameof(pcmFloats), "The samples of a channel are a null reference.");
            }
        }
        for (int i = 1; i < pcmFloats.Length; i++)
        {
            if (pcmFloats[i].Length != pcmFloats[0].Length)
            {
                throw new ArgumentException("The channels have unequal lengths.", nameof(pcmFloats));
            }
        }
        InitializeValues(sampleRate, pcmFloats, playbackOriginSample);
    }

    public PcmData(string audioFilePath)
    {
        string extension = Path.GetExtension(audioFilePath).ToLower();

        switch (extension)
        {
            case ".ogg":
                InitializeFromOgg(audioFilePath);
                break;
            case ".mp3":
                InitializeFromMp3(audioFilePath);
                break;
            //case ".wav":
            //    return WavToSoundData(audioFilePath);
            default:
                throw new NotSupportedException($"Audio format '{extension}' is not supported.");
        }
    }

    public PcmData(byte[] fileBuffer, string extension)
    {
        switch (extension)
        {
            case ".ogg":
                throw new NotSupportedException("AudioDataHandler does not yet support retrieving SoundData by parsing an OGG buffer.");
            case ".mp3":
                InitializeFromMp3(fileBuffer);
                break;
            default:
                throw new NotSupportedException($"Audio format '{extension}' is not supported.");
        }
    }

    private void InitializeFromOgg(string path)
    {
        // Based on https://github.com/leezer3/OpenBVE/blob/84064b7ef4e51def0b26e07226c114c004bcd4d3/source/Plugins/Sound.Vorbis/Plugin.Parser.cs#L12

        AudioDataHelper.DecodeOgg(path, out float[] floatsMixed, out int channels, out int sampleRate, out _);
        float[][] floats = AudioDataHelper.SeparateByChannels(floatsMixed, channels);
        InitializeValues(sampleRate, floats, 0);
    }

    private void InitializeFromMp3(string path)
    {
        AudioDataHelper.DecodeMp3(path, out byte[] shortsMixed_byte, out int channels,
            out int sampleRate, out _, out int startSilenceSamples);
        var floats = AudioDataHelper.ConvertToFloats(shortsMixed_byte, channels);
        InitializeValues(sampleRate, floats, startSilenceSamples);
    }

    private void InitializeFromMp3(byte[] mp3File)
    {
        AudioDataHelper.DecodeMp3(mp3File, out byte[] shortsMixed_byte, out int channels,
            out int sampleRate, out _, out int startSilenceSamples);
        var floats = AudioDataHelper.ConvertToFloats(shortsMixed_byte, channels);
        InitializeValues(sampleRate, floats, startSilenceSamples);
    }

    private void InitializeValues(int sampleRate, float[][] pcmFloats, int startSilenceSamples)
    {
        this.sampleRate = sampleRate;
        this.channels = pcmFloats.Length;
        this.pcmFloats = pcmFloats;
        this.startSilenceSamples = startSilenceSamples;
    }

    #endregion
    #region Properties
    /// <summary>Gets the number of samples per second.</summary>
    public int SampleRate => this.sampleRate;
    /// <summary>The PCM sound data per channel. Samples are floats between -1 and 1.</summary>
    public float[][] PcmFloats => pcmFloats;
    /// <summary>
    /// Number of channels
    /// </summary>
    public int Channels => channels;

    /// <summary>
    /// Sample index to be considered as origin due to added silence in start of file. Mainly relevant for mp3 files.
    /// <para>Some playback devices start their playback here, including Godot and osu.</para>
    /// </summary>
    public int PlaybackOriginSample => startSilenceSamples;
    #endregion

    /// <summary>
    /// Returns PCM data as a double array for spectrogram processing.
    /// </summary>
    public double[] GetPcmAsDoubles(double multiplier = 16_000)
    {
        return pcmFloats[0]
            .Select(x => (double)x * multiplier)
            .ToArray();
    }

}