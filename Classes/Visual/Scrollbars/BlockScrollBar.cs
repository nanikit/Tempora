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
using System.Collections.Generic;
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
    private ImageTexture? minimapTexture;
    private bool minimapUpdateQueued;
    private float rowContentFraction = 1;
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
        SetMinimapStyles();

        GlobalEvents.Instance.TimingChanged += OnTimingChanged;
        GlobalEvents.Instance.AudioFileChanged += OnAudioFileChanged;
        GlobalEvents.Instance.SpectrogramUpdated += OnSpectrogramUpdated;
        GlobalEvents.Instance.AudioVisualsContainerScrolled += OnScrolled;
        ValueChanged += OnValueChanged;
        audioVisualsContainer.VisibleRowCountChanged += UpdateScrollBar;
        audioVisualsContainer.PlaybackPositionChanged += UpdatePlaybackMarker;
        audioVisualsContainer.SortChildren += OnRowsSorted;
        Project.Instance.Settings.DisplayChanged += OnDisplaySettingsChanged;
        Resized += UpdateGrabberMinimum;
        Resized += QueueMinimapUpdate;
        UpdateScrollBar();
    }

    public override void _ExitTree()
    {
        GlobalEvents.Instance.TimingChanged -= OnTimingChanged;
        GlobalEvents.Instance.AudioFileChanged -= OnAudioFileChanged;
        GlobalEvents.Instance.SpectrogramUpdated -= OnSpectrogramUpdated;
        GlobalEvents.Instance.AudioVisualsContainerScrolled -= OnScrolled;
        audioVisualsContainer.VisibleRowCountChanged -= UpdateScrollBar;
        audioVisualsContainer.PlaybackPositionChanged -= UpdatePlaybackMarker;
        audioVisualsContainer.SortChildren -= OnRowsSorted;
        Project.Instance.Settings.DisplayChanged -= OnDisplaySettingsChanged;
        ((StyleBoxTexture)GetThemeStylebox("scroll")).Texture = null;
        minimapTexture?.Dispose();
        minimapTexture = null;
    }

    private void SetMinimapStyles()
    {
        using var track = new StyleBoxTexture();
        track.SetContentMarginAll(0);
        AddThemeStyleboxOverride("scroll", track);

        var highlight = new Color("ffdf33");
        for (int i = 0; i < GrabberStyles.Length; i++)
        {
            string name = GrabberStyles[i];
            using var style = (StyleBoxFlat)GetThemeStylebox(name).Duplicate();
            style.BgColor = new Color(highlight, 0.2f + i * 0.1f);
            style.BorderColor = highlight;
            style.SetBorderWidthAll(2);
            AddThemeStyleboxOverride(name, style);
        }
    }

    private void OnTimingChanged(object? sender, EventArgs e)
    {
        if (Project.Instance.Timing.IsBatchOperationInProgress)
            return;
        UpdateScrollBar();
    }

    private void OnAudioFileChanged(object? sender, EventArgs e) => UpdateScrollBar();

    private void OnSpectrogramUpdated(object? sender, EventArgs e) => QueueMinimapUpdate();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => UpdateScrollBar();

    private void OnRowsSorted()
    {
        if (audioVisualsContainer.AudioBlocks.Count == 0)
            return;
        float height = audioVisualsContainer.AudioBlocks[0].Size.Y;
        if (height <= 0)
            return;
        float fraction = height / (height + audioVisualsContainer.GetThemeConstant("separation"));
        if (rowContentFraction == fraction)
            return;
        rowContentFraction = fraction;
        QueueMinimapUpdate();
    }

    private void OnValueChanged(double value) => UpdateAudioVisualsContainer(value);

    private void UpdateScrollBar()
    {
        UpdateLimits();
        Value = audioVisualsContainer.NominalMeasurePositionStartForTopBlock;
        UpdateAudioVisualsContainer(Value);
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
        int endMeasure = Math.Max(firstMeasure + 1, audioVisualsContainer.LastRowMeasure + 1);
        isRangeChanging = true;
        MinValue = firstMeasure;
        MaxValue = endMeasure;
        Page = Math.Min(audioVisualsContainer.VisibleRowCount, endMeasure - firstMeasure);
        isRangeChanging = false;
        UpdateGrabberMinimum();
        QueueMinimapUpdate();
    }

    private void QueueMinimapUpdate()
    {
        if (minimapUpdateQueued)
            return;
        minimapUpdateQueued = true;
        Callable.From(UpdateMinimap).CallDeferred();
    }

    private void UpdateMinimap()
    {
        minimapUpdateQueued = false;
        if (!IsInsideTree())
            return;
        Vector2 size = GetTrackRect().Size;
        if (size.X <= 0 || size.Y <= 0 || Project.Instance.SpectrogramContext?.CachedSpectrogramImage == null)
            return;

        using Image image = CreateMinimapImage(new Vector2I((int)Math.Ceiling(size.X), (int)Math.Ceiling(size.Y)));
        var texture = ImageTexture.CreateFromImage(image);
        ((StyleBoxTexture)GetThemeStylebox("scroll")).Texture = texture;
        minimapTexture?.Dispose();
        minimapTexture = texture;
    }

    private Image CreateMinimapImage(Vector2I size)
    {
        Project project = Project.Instance;
        var image = Image.CreateEmpty(size.X, size.Y, false, project.SpectrogramContext.CachedSpectrogramImage!.GetFormat());
        TimingAnchor[] timing = project.Timing.GetAnchors();
        double rowHeight = size.Y / (MaxValue - MinValue);
        for (int measure = (int)MinValue; measure < MaxValue; measure++)
        {
            int top = (int)Math.Round((measure - MinValue) * rowHeight);
            int bottom = (int)Math.Round((measure - MinValue + rowContentFraction) * rowHeight);
            if (bottom <= top)
                continue;
            var layout = new AudioPanelLayout(measure, project.Settings.MeasureOverlap, project.Settings.DownbeatPositionOffset);
            DrawMinimapRow(image, new Rect2I(0, top, size.X, bottom - top), layout, timing);
        }
        return image;
    }

    private static void DrawMinimapRow(Image image, Rect2I row, AudioPanelLayout layout, IReadOnlyList<TimingAnchor> timing)
    {
        Project project = Project.Instance;
        AudioFile audio = project.AudioFile;
        foreach (float[] segment in TimingMath.GetPanelSegments(layout.StartMeasure, layout.EndMeasure, audio.GetAudioLength(), timing))
        {
            float startMeasure = (float)TimingMath.OffsetToMeasurePosition(segment[0], timing);
            float endMeasure = (float)TimingMath.OffsetToMeasurePosition(segment[1], timing);
            int left = (int)Math.Round(layout.MeasurePositionToXPosition(startMeasure, row.Size.X));
            int right = (int)Math.Round(layout.MeasurePositionToXPosition(endMeasure, row.Size.X));
            if (right <= left)
                continue;

            using Image slice = project.SpectrogramContext.GetSpectrogramImageSlice(
                audio.SampleTimeToSampleIndex(segment[0]), audio.SampleTimeToSampleIndex(segment[1]), row.Size.Y, right - left);
            image.BlitRect(slice, new Rect2I(Vector2I.Zero, slice.GetSize()), new Vector2I(left, row.Position.Y));
        }
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
