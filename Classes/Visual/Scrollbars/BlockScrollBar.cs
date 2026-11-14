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
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;

namespace Tempora.Classes.Visual.AudioDisplay;

public partial class BlockScrollBar : VScrollBar
{
    [Export]
    AudioVisualsContainer audioVisualsContainer = null!;

    [Export]
    ColorRect playbackMarker = null!;

    private static readonly string[] GrabberStyles = ["grabber", "grabber_highlight", "grabber_pressed"];
    bool isRangeChanging = false;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse
            || mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))
            return;

        // Preserve the native Page=0 wheel distance while Page sizes the visible-range handle.
        double distance = Math.Max((MaxValue - Page - MinValue) / 16 * mouse.Factor, Step);
        double previous = Value;
        Value += mouse.ButtonIndex == MouseButton.WheelDown ? distance : -distance;
        if (Value != previous)
            EmitSignal(SignalName.Scrolling);
        AcceptEvent();
    }

    public override void _Ready()
    {
        foreach (string name in GrabberStyles)
        {
            using var style = (StyleBox)GetThemeStylebox(name).Duplicate();
            AddThemeStyleboxOverride(name, style);
        }

        GlobalEvents.Instance.TimingChanged += OnTimingChanged;
        GlobalEvents.Instance.AudioFileChanged += OnAudioFileChanged;
        GlobalEvents.Instance.AudioVisualsContainerScrolled += OnScrolled;
        ValueChanged += OnValueChanged;
        audioVisualsContainer.VisibleRowCountChanged += UpdateScrollBar;
        audioVisualsContainer.PlaybackPositionChanged += UpdatePlaybackMarker;
        Resized += UpdateGrabberMinimum;
        UpdateScrollBar();
    }

    public override void _ExitTree()
    {
        GlobalEvents.Instance.TimingChanged -= OnTimingChanged;
        GlobalEvents.Instance.AudioFileChanged -= OnAudioFileChanged;
        GlobalEvents.Instance.AudioVisualsContainerScrolled -= OnScrolled;
        audioVisualsContainer.VisibleRowCountChanged -= UpdateScrollBar;
        audioVisualsContainer.PlaybackPositionChanged -= UpdatePlaybackMarker;
    }

    private void OnTimingChanged(object? sender, EventArgs e)
    {
        if (Project.Instance.Timing.IsBatchOperationInProgress)
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
        int endMeasure = Math.Max(firstMeasure + 1, Project.Instance.Timing.GetLastMeasure(Project.Instance.AudioFile?.GetAudioLength() ?? 0) + 1);
        isRangeChanging = true;
        MinValue = firstMeasure;
        MaxValue = endMeasure;
        Page = Math.Min(audioVisualsContainer.VisibleRowCount, endMeasure - firstMeasure);
        isRangeChanging = false;
        UpdateGrabberMinimum();
    }

    private void UpdateGrabberMinimum()
    {
        float minimum = GetGrabberStyleMinimum(Page / (MaxValue - MinValue), GetTrackRect().Size.Y);
        foreach (string name in GrabberStyles)
        {
            StyleBox style = GetThemeStylebox(name);
            style.ContentMarginTop = minimum / 2;
            style.ContentMarginBottom = minimum / 2;
        }
    }

    private static float GetGrabberStyleMinimum(double fraction, float trackHeight)
    {
        // Godot adds the style minimum to the proportional part of the remaining track.
        // Solve that minimum so the final handle is max(24px, fraction * trackHeight).
        return fraction >= 1 ? 0 : (float)Math.Max(0, (24 - fraction * trackHeight) / (1 - fraction));
    }

    private void UpdatePlaybackMarker(float measurePosition)
    {
        playbackMarker.Visible = MusicPlayer.Instance.Playing;
        if (!playbackMarker.Visible)
            return;

        playbackMarker.Color = audioVisualsContainer.AudioBlocks[0].AudioDisplayPanel.Playhead.DefaultColor;
        Rect2 track = GetTrackRect();
        float fraction = (float)Math.Clamp((measurePosition - MinValue) / (MaxValue - MinValue), 0, 1);
        playbackMarker.Position = new Vector2(0, track.Position.Y + fraction * (track.Size.Y - playbackMarker.Size.Y));
    }

    private Rect2 GetTrackRect()
    {
        StyleBox track = GetThemeStylebox("scroll");
        float start = GetThemeIcon("decrement").GetHeight() + track.GetMargin(Side.Top);
        float height = Size.Y - GetThemeIcon("decrement").GetHeight() - GetThemeIcon("increment").GetHeight() - track.GetMinimumSize().Y;
        return new Rect2(0, start, Size.X, height);
    }

    private void OnScrolled(object? sender, EventArgs e)
    {
        Value = audioVisualsContainer.NominalMeasurePositionStartForTopBlock;
    }
}