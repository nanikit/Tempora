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

namespace Tempora.Classes.TimingClasses;

/// <summary>
/// A data class which asserts that a specific point in time (<see cref="Offset"/>)
/// should be attached to a musical timeline at (<see cref="MeasurePosition"/>).
/// The Bpm (<see cref="Bpm"/>) is calculated via the subsequent <see cref="TimingPoint"/> 
/// in <see cref="Timing.TimingPoints"/> if the subsequent point exists.
/// </summary>
public partial class TimingPoint : Node, IComparable<TimingPoint>, ICloneable
{
    #region Properties and Fields

    public bool IsInstantiating = true;

    public bool IsBeingUpdated = false;

    public ulong SystemTimeWhenCreatedMsec;

    public bool WasBPMManuallySet = false;

    #region Time Signature
    private int[] timeSignature = [4, 4];

    /// <summary>
    /// Time Signature. Affects how <see cref="MeasuresPerSecond"/> and <see cref="TimeSignature"/> correlate
    /// via the formulas <see cref="BpmToMps(float)"/> and <see cref="MpsToBpm(float)"/>.
    /// Changes are always accepted.
    /// </summary>
    public int[] TimeSignature
    {
        get => timeSignature;
        set
        {
            if (timeSignature == value)
                return;
            int[] oldValue = timeSignature;
            timeSignature = value;

            PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.TimeSignature, oldValue, value));
        }
    }
    #endregion

    #region Offset
    private double offset;

    /// <summary>
    /// The timestamp in the audio which this <see cref="TimingPoint"/> is attached to. 
    /// </summary>
    public double Offset
    {
        get => offset;
        set
        {
            if (offset == value)
                return;
            if (IsInstantiating)
            {
                offset = value;
                return;
            }

            double oldValue = offset;
            offset = value;

            PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.Offset, oldValue, value));
        }
    }
    #endregion

    #region MeasurePosition
    private double? measurePosition;
    /// <summary>
    /// The <see cref="TimingPoint"/>'s position on the musical timeline. 
    /// The value is defined in terms of measures (integer part) from the musical timeline origin.
    /// Individual beats in a measure are the fractional part of the value.
    /// As an example, if a measure has a 4/4 <see cref="TimeSignature"/>, 
    /// the value 0.75 means "Measure 0, Quarter note 4"
    /// </summary>
    public double? MeasurePosition
    {
        get => measurePosition;
        set
        {
            if (measurePosition == value)
                return;

            double? oldValue = measurePosition;
            measurePosition = value;

            PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.MeasurePosition, oldValue, value));
        }
    }
    #endregion

    #region MeasuresPerSecond
    private double measuresPerSecond = 0.5d;
    /// <summary>
    /// Musical measures per second. 
    /// Directly correlated with <see cref="Bpm"/> and <see cref="TimeSignature"/>
    /// via the formulas <see cref="BpmToMps(float)"/> and <see cref="MpsToBpm(float)"/>.
    /// Cannot be changed directly, as it is a calculated property via <see cref="MeasuresPerSecond_Set(Timing)"/>
    /// </summary>
    public double MeasuresPerSecond
    {
        get => measuresPerSecond;
        set
        {
            if (measuresPerSecond == value)
                return;

            double oldValue = measuresPerSecond;
            measuresPerSecond = value;

            PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.MeasuresPerSecond, oldValue, value));
        }
    }
    #endregion

    #region Bpm
    private double? manualBpm;
    /// <summary>
    /// Beats per minute. Directly correlated with <see cref="MeasuresPerSecond"/> and <see cref="TimeSignature"/>
    /// via the formulas <see cref="BpmToMps(float)"/> and <see cref="MpsToBpm(float)"/>.
    /// Changes are only accepted if <see cref="Timing"/> validates the change.
    /// </summary>
    public double Bpm
    {
        get => manualBpm ?? ComputedBpm;
        set
        {
            if (manualBpm == value)
                return;

            double oldValue = Bpm;
            manualBpm = Settings.Instance.RoundBPM ? Math.Round(value * 10, MidpointRounding.ToEven) / 10d : value;

            PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.Bpm, oldValue, manualBpm));
        }
    }

    public double ComputedBpm => MpsToBpm(MeasuresPerSecond);

    public double? ManualBpm => manualBpm;

    public void SetManualBpm(double bpm)
    {
        Bpm = bpm;
        WasBPMManuallySet = true;
    }

    public void ClearManualBpm()
    {
        if (manualBpm == null)
            return;

        double oldValue = Bpm;
        manualBpm = null;

        PropertyChanged?.Invoke(this, new PropertyChangeArgument(PropertyType.Bpm, oldValue, ComputedBpm));
    }
    #endregion

    #endregion
    #region Constructors
    public TimingPoint(double time, int[] timeSignature)
    {
        this.offset = time;
        this.timeSignature = timeSignature;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }

    public TimingPoint(double measurePosition)
    {
        this.measurePosition = measurePosition;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }

    public TimingPoint(double time, double measurePosition, double measuresPerSecond)
    {
        this.offset = time;
        this.measurePosition = measurePosition;
        this.measuresPerSecond = measuresPerSecond;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }

    public TimingPoint(double time, double measurePosition, int[] timeSignature)
    {
        this.offset = time;
        this.measurePosition = measurePosition;
        this.timeSignature = timeSignature;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }

    public TimingPoint(double time, double measurePosition, int[] timeSignature, double measuresPerSecond)
    {
        this.offset = time;
        this.measurePosition = measurePosition;
        this.timeSignature = timeSignature;
        this.measuresPerSecond = measuresPerSecond;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }

    /// <summary>
    /// Constructor used only for cloning
    /// </summary>
    private TimingPoint(double time, double? measurePosition, int[] timeSignature, double measuresPerSecond, double? manualBpm, bool isInstantiating)
    {
        this.offset = time;
        this.measurePosition = measurePosition;
        this.timeSignature = timeSignature;
        this.measuresPerSecond = measuresPerSecond;
        this.manualBpm = manualBpm;
        this.IsInstantiating = isInstantiating;
        SystemTimeWhenCreatedMsec = Time.GetTicksMsec();
    }
    #endregion
    #region Interface Methods
    public int CompareTo(TimingPoint? other) => Offset.CompareTo(other?.Offset);

    public object Clone()
    {
        var timingPoint = new TimingPoint(Offset, MeasurePosition, TimeSignature, MeasuresPerSecond, ManualBpm, IsInstantiating);

        return timingPoint;
    }
    #endregion
    #region Change and Deletion Events
    public event EventHandler ChangeFinalized = null!;

    public void FinalizeChange()
    {
        if (!IsInstantiating)
        {
            ChangeFinalized?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler AttemptDelete = null!;
    /// <summary>
    ///     Relies on parent <see cref="Timing" /> to delete from project.
    /// </summary>
    public void Delete() => AttemptDelete?.Invoke(this, EventArgs.Empty);

    public event EventHandler PropertyChanged = null!;

    public enum PropertyType
    {
        TimeSignature,
        Offset,
        MeasurePosition,
        MeasuresPerSecond,
        Bpm,
    }

    public class PropertyChangeArgument(PropertyType propertyType, object? oldValue, object? newValue) : EventArgs
    {
        private PropertyType propertyType = propertyType;
        private object? oldValue = oldValue;
        private object? newValue = newValue;
        public object? OldValue
        {
            get => oldValue;
        }
        public object? NewValue
        {
            get => newValue;
        }
        public PropertyType PropertyType
        {
            get => propertyType;
        }
    }
    #endregion
    #region Calculators
    public double BpmToMps(double bpm) => BpmToMps(bpm, TimeSignature);

    public double MpsToBpm(double mps) => MpsToBpm(mps, TimeSignature);

    public static float BpmToMps(float bpm, int[] timeSignature) => (float)BpmToMps((double)bpm, timeSignature);

    public static float MpsToBpm(float mps, int[] timeSignature) => (float)MpsToBpm((double)mps, timeSignature);

    public static double BpmToMps(double bpm, int[] timeSignature) => TimingMath.BpmToMeasuresPerSecond(bpm, timeSignature);

    public static double MpsToBpm(double mps, int[] timeSignature) => TimingMath.MeasuresPerSecondToBpm(mps, timeSignature);

    public double BeatLengthSec => 1 / (Bpm / 60);
    #endregion
}