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
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Godot;
using NAudio.MediaFoundation;
using Tempora.Classes.Utility;

namespace Tempora.Classes.TimingClasses;

public partial class Timing
{
    public bool ShouldHandleTimingPointChanges = true;

    private void SubscribeToEvents(TimingPoint timingPoint)
    {
        if (timingPoint == null)
            throw new NullReferenceException($"{nameof(timingPoint)} was null");
        timingPoint.AttemptDelete += OnTimingPointDeleteAttempt;
        timingPoint.ChangeFinalized += OnTimingPointChanged;
        timingPoint.PropertyChanged += OnTimingPointPropertyChanged;
    }

    private void ReSubscribe()
    {
        foreach (TimingPoint timingPoint in timingPoints)
        {
            SubscribeToEvents(timingPoint);
        }
    }

    private void OnTimingPointChanged(object? sender, EventArgs e)
    {
        if (sender is not TimingPoint timingPoint)
            throw new Exception("Sender wasn't a TimingPoint.");

        if (!timingPoint.IsInstantiating && !IsBatchOperationInProgress)
            GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.TimingChanged));
    }
    private void OnTimingPointDeleteAttempt(object? sender, EventArgs e)
    {
        if (sender is not TimingPoint timingPoint)
            throw new Exception("Sender wasn't a TimingPoint.");
        DeleteTimingPoint(timingPoint);
    }

    private void OnTimingPointMeasurePositionChangeRejected(object? sender, EventArgs e)
    {
        if (IsBatchOperationInProgress)
            ShouldCancelBatchOperation = true;
    }

    /// <summary>
    /// Calculates the <see cref="TimingPoint.MeasuresPerSecond"/> of a <see cref="TimingPoint"/> based on adjacent points. Returns null if the value is free to be anything. 
    /// </summary>
    /// <param name="timingPoint"></param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    /// <exception cref="Exception"></exception>
    private double? CalculateMPSBasedOnAdjacentPoints(TimingPoint timingPoint)
    {
        double? correctValue = null;
        TimingPoint? previousTimingPoint = GetPreviousTimingPoint(timingPoint);
        TimingPoint? nextTimingPoint = GetNextTimingPoint(timingPoint);

        if (timingPoint.MeasurePosition == null)
            return null;
        if (AreMeasurePositionsEqual(nextTimingPoint, timingPoint) || AreMeasurePositionsEqual(previousTimingPoint, timingPoint))
            throw new Exception("Neighboring Timing Point has same Music Position.");

        if (nextTimingPoint?.MeasurePosition != null)
        {
            correctValue = TimingMath.CalculateMeasuresPerSecond(
                timingPoint.MeasurePosition.Value,
                nextTimingPoint.MeasurePosition.Value,
                timingPoint.Offset,
                nextTimingPoint.Offset);
        }
        else if (previousTimingPoint?.MeasurePosition != null)
        {
            double timeSignatureCorrection = ((double)previousTimingPoint.TimeSignature[0] / previousTimingPoint.TimeSignature[1]) / ((double)timingPoint.TimeSignature[0] / timingPoint.TimeSignature[1]);

            correctValue = TimingMath.CalculateMeasuresPerSecond(
                previousTimingPoint.MeasurePosition.Value,
                timingPoint.MeasurePosition.Value,
                previousTimingPoint.Offset,
                timingPoint.Offset)
                * timeSignatureCorrection;
        }

        return correctValue;
    }

    /// <summary>
    /// Updates adjacent <see cref="TimingPoint.MeasuresPerSecond"/>. These updates will invoke their own <see cref="TimingPoint.PropertyChanged"/>, which updates <see cref="TimingPoint.Bpm"/>.
    /// </summary>
    /// <param name="timingPoint"></param>
    private void UpdateAdjacentTempo(TimingPoint timingPoint)
    {
        TimingPoint? previousTimingPoint = GetPreviousTimingPoint(timingPoint);
        TimingPoint? nextTimingPoint = GetNextTimingPoint(timingPoint);
        if (!timingPoint.IsInstantiating && previousTimingPoint != null)
        {
            previousTimingPoint.MeasuresPerSecond = CalculateMPSBasedOnAdjacentPoints(previousTimingPoint) ?? previousTimingPoint.MeasuresPerSecond;
            previousTimingPoint.ClearManualBpm();
        }
        if (!timingPoint.IsInstantiating && nextTimingPoint == TimingPoints[^1] && !nextTimingPoint.HasManualBpm)
            nextTimingPoint.MeasuresPerSecond = CalculateMPSBasedOnAdjacentPoints(nextTimingPoint) ?? nextTimingPoint.MeasuresPerSecond;
    }

    private void UpdateAllDependentProperties(TimingPoint timingPoint, TimingPoint.PropertyType propertyType, object? oldValue, object? value)
    {
        switch (propertyType)
        {
            case TimingPoint.PropertyType.Offset:
                UpdateTempoIncludingAdjacent(timingPoint);
                break;
            case TimingPoint.PropertyType.MeasurePosition:
                UpdateTempoIncludingAdjacent(timingPoint);
                break;
            case TimingPoint.PropertyType.MeasuresPerSecond:
                break;
            case TimingPoint.PropertyType.TimeSignature:
                UpdateTempoIncludingAdjacent(timingPoint, (int[]?)oldValue);
                break;
            case TimingPoint.PropertyType.Bpm:
                timingPoint.MeasuresPerSecond = timingPoint.BpmToMps(timingPoint.Bpm);
                break;

        }
    }

    /// <summary>
    /// Ensures that all tempo-related value for this <see cref="TimingPoint"/> and adjacent ones are properly updated.
    /// </summary>
    /// <param name="timingPoint"></param>
    private void UpdateTempoIncludingAdjacent(TimingPoint timingPoint, int[]? previousTimeSignature = null)
    {
        if (!(timingPoint == TimingPoints[^1] && timingPoint.HasManualBpm))
        {
            UpdateMPS(timingPoint, previousTimeSignature);
        }
        UpdateAdjacentTempo(timingPoint);
    }

    private void UpdateMPS(TimingPoint timingPoint, int[]? previousTimeSignature = null)
    {
        double? calculatedMps = CalculateMPSBasedOnAdjacentPoints(timingPoint);
        if (calculatedMps != null)
        {
            timingPoint.MeasuresPerSecond = calculatedMps.Value;
            return;
        }

        if (!Settings.Instance.PreserveBPMWhenChangingTimeSignature)
            return;

        double bpmToPreserve = timingPoint.ManualBpm
            ?? (previousTimeSignature == null
                ? timingPoint.ComputedBpm
                : TimingPoint.MpsToBpm(timingPoint.MeasuresPerSecond, previousTimeSignature));

        timingPoint.MeasuresPerSecond = timingPoint.BpmToMps(bpmToPreserve);
    }

    /// <summary>
    /// When the <see cref="Timing"/> decides that changes to a timing point have been finalized, invoke an event that other classes can respond to.
    /// </summary>
    /// <param name="timingPoint"></param>
    /// <exception cref="ArgumentNullException"></exception>
    private void FinalizeTimingPointChange(TimingPoint timingPoint)
    {
        if (timingPoint == null)
            throw new ArgumentNullException(nameof(timingPoint));
        timingPoint.FinalizeChange();
    }

    private void OnTimingPointPropertyChanged(object? sender, EventArgs e)
    {
        if (!ShouldHandleTimingPointChanges)
            return;
        if (sender is null)
            return;
        if (e is null)
            return;
        if (e is not TimingPoint.PropertyChangeArgument propertyChangeArgument)
            throw new Exception("The argument of a TimingPoint property change event must be of type TimingPoint.PropertyChangeArgument.");
        if (sender is not TimingPoint timingPoint)
            throw new Exception("Sender must be a TimingPoint");

        if (timingPoint.IsBeingUpdated)
            return; // All property updates should happen in one event call to prevent recursively invoking change events.

        TimingPoint? previousTimingPoint = GetPreviousTimingPoint(timingPoint);
        TimingPoint? nextTimingPoint = GetNextTimingPoint(timingPoint);
        var propertyType = propertyChangeArgument.PropertyType;
        var oldValue = propertyChangeArgument.OldValue;
        var newValue = propertyChangeArgument.NewValue;

        bool isValid = IsTimingPointPropertyChangeValid(timingPoint, propertyType, newValue);

        if (isValid)
        {
            timingPoint.IsBeingUpdated = true;
            UpdateAllDependentProperties(timingPoint, propertyType, oldValue, newValue);
            timingPoint.IsBeingUpdated = false;
            FinalizeTimingPointChange(timingPoint);
            return;
        }

        switch (propertyType) // Reset to old values if change is denied
        {
            case TimingPoint.PropertyType.Offset:
                double oldOffset = (double)oldValue!;
                timingPoint.Offset = oldOffset;
                break;
            case TimingPoint.PropertyType.MeasuresPerSecond:
                double oldMPS = (double)oldValue!;
                timingPoint.MeasuresPerSecond = oldMPS;
                break;
            case TimingPoint.PropertyType.TimeSignature:
                int[] oldTimeSignature = (int[])oldValue!;
                timingPoint.TimeSignature = oldTimeSignature;
                break;
            case TimingPoint.PropertyType.Bpm:
                timingPoint.RejectManualBpmChange();
                break;
            case TimingPoint.PropertyType.MeasurePosition:
                double? oldMeasurePosition = (double?)oldValue;
                timingPoint.MeasurePosition = oldMeasurePosition;
                TimingPoint? rejectingTimingPoint = null;
                CanTimingPointGoHere(timingPoint, (double)newValue!, out rejectingTimingPoint);
                if (rejectingTimingPoint != null)
                    GlobalEvents.Instance.InvokeEvent(nameof(GlobalEvents.MeasurePositionChangeRejected), new GlobalEvents.ObjectArgument<TimingPoint>(rejectingTimingPoint));
                break;
            default:
                break;
        }
    }

    private bool IsTimingPointPropertyChangeValid(TimingPoint timingPoint, TimingPoint.PropertyType propertyType, object? newValue)
    {
        TimingPoint? previousTimingPoint = GetPreviousTimingPoint(timingPoint);
        TimingPoint? nextTimingPoint = GetNextTimingPoint(timingPoint);
        switch (propertyType)
        {
            case TimingPoint.PropertyType.Offset:
                var newOffset = (double)newValue!;
                bool previousPointBlocksChange = previousTimingPoint != null && previousTimingPoint.Offset >= newOffset;
                bool nextPointBlockschange = nextTimingPoint != null && nextTimingPoint.Offset <= newOffset;
                if (previousPointBlocksChange || nextPointBlockschange)
                    return false;
                return true;

            case TimingPoint.PropertyType.TimeSignature:
                return true;

            case TimingPoint.PropertyType.MeasuresPerSecond:
                if (nextTimingPoint == null)
                    return true;

                double? calculatedMPS = CalculateMPSBasedOnAdjacentPoints(timingPoint);

                return (double)newValue! == calculatedMPS!;

            case TimingPoint.PropertyType.Bpm:
                if (nextTimingPoint == null)
                    return true;

                calculatedMPS = CalculateMPSBasedOnAdjacentPoints(timingPoint);

                if (calculatedMPS == null)
                    return true;

                double newBpm = (double)newValue!;
                double calculatedBpm = timingPoint.MpsToBpm(calculatedMPS.Value);

                return Settings.Instance.RoundBPM
                    ? Math.Abs(calculatedBpm - newBpm) < 0.1
                    : Math.Abs(calculatedBpm - newBpm) < 0.0001;

            case TimingPoint.PropertyType.MeasurePosition:
                return CanTimingPointGoHere(timingPoint, (double?)newValue, out _);

            default:
                return false;
        }
    }
}