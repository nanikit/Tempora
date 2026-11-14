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
using System.Linq;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;

namespace Tempora.Classes.Visual.AudioDisplay;

public partial class AudioVisualsContainer : VBoxContainer, ISettingsConsumer
{
    public Settings Settings { get; set; } = null!;


    public int VisibleRowCount { get; private set; } = 1;
    public event Action? VisibleRowCountChanged;
    public event Action<float>? PlaybackPositionChanged;

    /// Accumulated pan gesture delta for converting continuous gestures to discrete scroll steps
    private float panGestureAccumulator;

    /// <summary>
    ///     Forward childrens' signals to Main
    /// </summary>
    public event EventHandler SeekPlaybackTime = null!;

    [Export]
    private PackedScene packedAudioBlock = null!;

    private MusicPlayer MusicPlayer => MusicPlayer.Instance;

    //public float FirstBlockStartTime = 0;

    private int nominalMeasurePositionStartForTopBlock;

    public List<AudioBlock> AudioBlocks = [];

    public int FirstTopMeasure => Project.Instance.Timing.TimeSignaturePoints.Count > 0
            ? Math.Min((int)Project.Instance.Timing.OffsetToMeasurePosition(0), Project.Instance.Timing.TimeSignaturePoints[0].Measure)
            : (int)Project.Instance.Timing.OffsetToMeasurePosition(0);
    public int LastTopMeasure => Math.Max(FirstTopMeasure, Project.Instance.Timing.GetLastMeasure(Project.Instance.AudioFile?.GetAudioLength() ?? 0) - (VisibleRowCount - 1));

    public int NominalMeasurePositionStartForTopBlock
    {
        get => nominalMeasurePositionStartForTopBlock;
        set
        {
            if (value == nominalMeasurePositionStartForTopBlock)
                return;
            nominalMeasurePositionStartForTopBlock = value;
            UpdateBlocksScroll();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.WheelDown && mouseEvent.Pressed && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Shift))
            {
                int distanceToLast = LastTopMeasure - NominalMeasurePositionStartForTopBlock;
                NominalMeasurePositionStartForTopBlock += Math.Min(Input.IsKeyPressed(Key.Alt) ? 5 : 1, distanceToLast);
                GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.AudioVisualsContainerScrolled));
            }
            else if (mouseEvent.ButtonIndex == MouseButton.WheelUp && mouseEvent.Pressed && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Shift))
            {
                int distanceToFirst = NominalMeasurePositionStartForTopBlock - FirstTopMeasure;
                NominalMeasurePositionStartForTopBlock -= Math.Min(Input.IsKeyPressed(Key.Alt) ? 5 : 1, distanceToFirst);
                GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.AudioVisualsContainerScrolled));
            }
        }
        else if (@event is InputEventPanGesture panGesture && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Shift))
        {
            panGestureAccumulator += panGesture.Delta.Y;
            int steps = (int)panGestureAccumulator;
            if (steps == 0)
                return;
            panGestureAccumulator -= steps;

            int scrollAmount = Input.IsKeyPressed(Key.Alt) ? steps * 5 : steps;
            if (scrollAmount > 0)
            {
                int distanceToLast = LastTopMeasure - NominalMeasurePositionStartForTopBlock;
                NominalMeasurePositionStartForTopBlock += Math.Min(scrollAmount, distanceToLast);
            }
            else
            {
                int distanceToFirst = NominalMeasurePositionStartForTopBlock - FirstTopMeasure;
                NominalMeasurePositionStartForTopBlock -= Math.Min(-scrollAmount, distanceToFirst);
            }
            GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.AudioVisualsContainerScrolled));
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                TimingPointSelection.Instance.DeselectAll();
            }
        }
    }

    public override void _Ready()
    {
        Resized += UpdateNumberOfVisibleBlocks;
        MouseExited += OnMouseExited;
        MusicPlayer.Paused += OnMusicPaused;
        MusicPlayer.Finished += OnMusicFinished;
        GlobalEvents.Instance.AudioFileChanged += OnAudioFileChanged;
        GlobalEvents.Instance.TimingPointAdded += OnTimingPointAdded;
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        if (MusicPlayer.Playing)
        {
            UpdatePlayHeads();
        }
        PositionTimingText();
    }

    private void PositionTimingText()
    {
        AudioDisplayPanel? above = null;
        foreach (AudioBlock block in AudioBlocks)
        {
            if (!block.Visible)
                continue;
            block.AudioDisplayPanel.PositionTimingText(above);
            above = block.AudioDisplayPanel;
        }
    }

    private void UpdatePlayHeads()
    {
        double playbackTime = MusicPlayer.GetPlaybackTime();
        float sampleTime = Project.Instance.AudioFile.PlaybackTimeToSampleTime((float)playbackTime);
        float measurePosition = (float)Project.Instance.Timing.OffsetToMeasurePosition(sampleTime);
        foreach (AudioBlock audioBlock in GetChildren().OfType<AudioBlock>())
        {
            AudioDisplayPanel audioDisplayPanel = audioBlock.AudioDisplayPanel;
            float x = audioDisplayPanel.MeasurePositionToXPosition(measurePosition);
            audioDisplayPanel.Playhead.Position = new Vector2(x, 0.0f);
            audioDisplayPanel.Playhead.Visible = x >= 0 && x <= audioDisplayPanel.Size.X && MusicPlayer.Playing;
        }
        PlaybackPositionChanged?.Invoke(measurePosition);
    }

    private void OnMouseExited()
    {
        Context.Instance.TimingPointNearestCursor = null;
    }

    private void OnMusicPaused() => UpdatePlayHeads();

    private void OnMusicFinished() => UpdatePlayHeads();

    private void OnAudioFileChanged(object? sender, EventArgs e)
    {
        panGestureAccumulator = 0;
        NominalMeasurePositionStartForTopBlock = FirstTopMeasure;
        UpdatePlayHeads();
        GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.AudioVisualsContainerScrolled));
    }

    public void CreateBlocks()
    {
        foreach (Node? child in GetChildren())
            child.QueueFree();
        AudioBlocks.Clear();

        int measurePositionStart = NominalMeasurePositionStartForTopBlock;

        // Instantiate block scenes and add as children
        for (int i = 0; i < Settings.MaxNumberOfBlocks; i++)
        {
            if (packedAudioBlock.Instantiate() is not AudioBlock audioBlock)
            {
                throw new NullReferenceException(nameof(audioBlock));
            }
            audioBlock.NominalMeasurePosition = measurePositionStart;
            measurePositionStart++;
            AddChild(audioBlock);
            AudioBlocks.Add(audioBlock);
        }

        foreach (AudioBlock audioBlock in AudioBlocks)
        {
            AudioDisplayPanel audioDisplayPanel = audioBlock.AudioDisplayPanel;
            audioDisplayPanel.SizeFlagsVertical = SizeFlags.ExpandFill;

            audioDisplayPanel.Playhead.Visible = false;

            audioDisplayPanel.SeekPlaybackTime += OnSeekPlaybackTime;
            audioDisplayPanel.AttemptToAddTimingPoint += OnAttemptToAddTimingPoint;
            audioDisplayPanel.TextLayoutChanged += UpdateNumberOfVisibleBlocks;

            audioDisplayPanel.IsInstantiating = false;
        }

        UpdateNumberOfVisibleBlocks();
    }

    public void UpdateNumberOfVisibleBlocks()
    {
        if (AudioBlocks.Count == 0)
            return;

        int separation = GetThemeConstant("separation");
        float minimumHeight = AudioBlocks.Max(block => block.AudioDisplayPanel.MinimumRowHeight);
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, minimumHeight);
        int fittingRows = Math.Max(1, (int)((Size.Y + separation) / (minimumHeight + separation)));
        int visibleRows = Math.Clamp(Settings.NumberOfRows, 1, Math.Min(fittingRows, AudioBlocks.Count));
        bool countChanged = VisibleRowCount != visibleRows;
        VisibleRowCount = visibleRows;

        for (int i = 0; i < AudioBlocks.Count; i++)
            AudioBlocks[i].Visible = i < VisibleRowCount;

        if (countChanged)
        {
            NominalMeasurePositionStartForTopBlock = Math.Clamp(NominalMeasurePositionStartForTopBlock, FirstTopMeasure, LastTopMeasure);
            VisibleRowCountChanged?.Invoke();
        }
    }

    public void UpdateBlocksScroll()
    {
        int measurePositionStart = NominalMeasurePositionStartForTopBlock;

        Godot.Collections.Array<Node> children = GetChildren();

        foreach (Node? child in children)
        {
            if (child is not AudioBlock)
            {
                continue;
            }
            var audioBlock = (AudioBlock)child;
            audioBlock.NominalMeasurePosition = measurePositionStart;
            measurePositionStart++;
        }
    }

    private void OnSeekPlaybackTime(object? sender, EventArgs e)
    {
        if (e is not GlobalEvents.ObjectArgument<float> floatArgument)
            throw new Exception($"{nameof(e)} was not of type {nameof(GlobalEvents.ObjectArgument<float>)}");
        float playbackTime = floatArgument.Value;
        SeekPlaybackTime?.Invoke(this, new GlobalEvents.ObjectArgument<float>(playbackTime));
    }

    private void OnAttemptToAddTimingPoint(object? sender, EventArgs e)
    {
        if (e is not GlobalEvents.ObjectArgument<float> floatArgument)
            throw new Exception($"{nameof(e)} was not of type {nameof(GlobalEvents.ObjectArgument<float>)}");
        float time = floatArgument.Value;

        Project.Instance.Edits.Begin();
        Project.Instance.Timing.AddTimingPoint(time, out TimingPoint? timingPoint);
        if (timingPoint == null)
        {
            Project.Instance.Edits.Commit();
            return;
        }
        if (timingPoint.MeasurePosition == null)
            throw new NullReferenceException($"{nameof(timingPoint.MeasurePosition)} was null");

        float measurePosition = (float)timingPoint.MeasurePosition;
        Project.Instance.Timing.SnapTimingPoint(timingPoint, measurePosition);
        Context.Instance.HeldTimingPoint = timingPoint;
        Context.Instance.HeldPointIsJustBeingAdded = true;

        TimingPointSelection.Instance.SelectTimingPoint(timingPoint);
    }

    private void OnTimingPointAdded(object? sender, EventArgs e)
    {
        if (e is not GlobalEvents.ObjectArgument<TimingPoint> timingPointArgument)
            throw new Exception($"{nameof(e)} was not of type {nameof(GlobalEvents.ObjectArgument<TimingPoint>)}");

        if (!Settings.AutoScrollWhenAddingTimingPoints)
            return;

        var timingPoint = timingPointArgument.Value;
        if (timingPoint.MeasurePosition == null)
            return;
        float measurePosition = (float)timingPoint.MeasurePosition!;

        SetTopBlockToPosition(measurePosition);
    }

    /// <summary>
    /// Changes the Block scroll such that the top block contains the music position. 
    /// If more than one block contains the music position due to timeline offset settings, select the last one that does.
    /// </summary>
    /// <param name="measurePosition"></param>
    private void SetTopBlockToPosition(float measurePosition)
    {
        // Get the last ActualMeasurePositionStart which is smaller than measurePosition. This way, we account for Timeline overlap.
        int bestNominalMeasureStart = (int)(measurePosition - 1);
        for (int measure = bestNominalMeasureStart; measure <= bestNominalMeasureStart + 2; measure++)
        {
            float actualStartForThisMeasure = AudioDisplayPanel.ActualMeasurePositionStart(measure, Settings);
            if (actualStartForThisMeasure > measurePosition)
            {
                NominalMeasurePositionStartForTopBlock = bestNominalMeasureStart;
                break;
            }
            bestNominalMeasureStart = measure;
        }
    }
}