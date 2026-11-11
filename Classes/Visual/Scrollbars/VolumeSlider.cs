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
using Godot;
using Tempora.Classes.Utility;

namespace Tempora.Classes.Visual;

public partial class VolumeSlider : HScrollBar, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;

    [Export] public string BusName = null!;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        int busIndex = AudioServer.GetBusIndex(BusName);
        ValueChanged += SaveToSettings;

        float invertedValue = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(busIndex));
        //Value = Math.Abs(1 - invertedValue);

        Value = invertedValue;
    }

    private void SaveToSettings(double value)
    {
        switch (BusName)
        {
            case "Music":
                Settings.MusicVolumeNormalized = value;
                break;
            case "Metronome":
                Settings.MetronomeVolumeNormalized = value;
                break;
            case "Master":
                Settings.MasterVolumeNormalized = value;
                break;
        }
    }
}
