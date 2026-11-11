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
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;

namespace Tempora.Classes.Visual.AudioDisplay;

public partial class BlockScrollBar : VScrollBar
{
    [Export]
    AudioVisualsContainer audioVisualsContainer = null!;

    bool isRangeChanging = false;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        GlobalEvents.Instance.TimingChanged += OnTimingChanged;
        GlobalEvents.Instance.AudioFileChanged += OnAudioFileChanged;
        GlobalEvents.Instance.AudioVisualsContainerScrolled += OnScrolled;
        ValueChanged += OnValueChanged;
        audioVisualsContainer.VisibleRowCountChanged += UpdateScrollBar;
        UpdateScrollBar();
    }

    public override void _ExitTree()
    {
        audioVisualsContainer.VisibleRowCountChanged -= UpdateScrollBar;
    }

    private void OnTimingChanged(object? sender, EventArgs e)
    {
        if (Timing.Instance.IsBatchOperationInProgress)
            return;
        UpdateScrollBar();
    }

    private void OnAudioFileChanged(object? sender, EventArgs e) => UpdateScrollBar();

    private void OnValueChanged(double value) => UpdateAudioVisualsContainer(value);

    private void UpdateScrollBar()
    {
        UpdateLimits();
        Value = audioVisualsContainer.NominalMeasurePositionStartForTopBlock;
    }

    private void UpdateAudioVisualsContainer(double value)
    {
        if (isRangeChanging)
            return; // If not in place, inadvertent scrolling occurs due to MinValue or MaxValue changing Value.

        audioVisualsContainer.NominalMeasurePositionStartForTopBlock = (int)value;
    }

    public void UpdateLimits()
    {
        int firstMeasure = audioVisualsContainer.FirstTopMeasure;
        int lastMeasure = audioVisualsContainer.LastTopMeasure;
        isRangeChanging = true;
        MinValue = firstMeasure;
        MaxValue = lastMeasure;
        isRangeChanging = false;
    }

    private void OnScrolled(object? sender, EventArgs e)
    {
        Value = audioVisualsContainer.NominalMeasurePositionStartForTopBlock;
    }
}