using System;
using System.Globalization;
using InduLink.Abstractions;

namespace InduLink.Simulation
{
    public enum SimulationBehavior { Fixed, Increment, Toggle }

    /// <summary>Immutable point configuration. Numeric text always uses invariant culture.</summary>
    public sealed class SimulationPoint
    {
        public SimulationPoint(string address, DataType dataType, string initialValue,
            SimulationBehavior behavior = SimulationBehavior.Fixed, double step = 1)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Point address is required.", nameof(address));
            if (!Enum.IsDefined(typeof(SimulationBehavior), behavior)) throw new ArgumentOutOfRangeException(nameof(behavior));
            if (!double.IsFinite(step)) throw new ArgumentOutOfRangeException(nameof(step));
            Address = address.Trim();
            DataType = dataType;
            InitialValue = initialValue ?? "";
            Behavior = behavior;
            Step = step;
            ParseValue(InitialValue);
            if (behavior == SimulationBehavior.Toggle && dataType != DataType.Bool)
                throw new ArgumentException("Toggle behavior requires a Bool point.");
            if (behavior == SimulationBehavior.Increment && (dataType == DataType.Bool || dataType == DataType.String))
                throw new ArgumentException("Increment behavior requires a numeric point.");
            if (behavior == SimulationBehavior.Increment && dataType != DataType.Float && dataType != DataType.Double && step != Math.Truncate(step))
                throw new ArgumentException("Integer points require an integer increment step.");
        }

        public string Address { get; }
        public DataType DataType { get; }
        public string InitialValue { get; }
        public SimulationBehavior Behavior { get; }
        public double Step { get; }

        public object ParseValue(string text)
        {
            var culture = CultureInfo.InvariantCulture;
            switch (DataType)
            {
                case DataType.Bool: return bool.Parse(text);
                case DataType.Int16: return short.Parse(text, NumberStyles.Integer, culture);
                case DataType.UInt16: return ushort.Parse(text, NumberStyles.Integer, culture);
                case DataType.Int32: return int.Parse(text, NumberStyles.Integer, culture);
                case DataType.UInt32: return uint.Parse(text, NumberStyles.Integer, culture);
                case DataType.Float:
                    var single = float.Parse(text, NumberStyles.Float, culture);
                    if (!float.IsFinite(single)) throw new FormatException("Float values must be finite.");
                    return single;
                case DataType.Double:
                    var number = double.Parse(text, NumberStyles.Float, culture);
                    if (!double.IsFinite(number)) throw new FormatException("Double values must be finite.");
                    return number;
                case DataType.String: return text ?? "";
                default: throw new ArgumentException("Unsupported simulator type: " + DataType);
            }
        }

        internal object NextValue(object current)
        {
            if (Behavior == SimulationBehavior.Fixed) return current;
            if (Behavior == SimulationBehavior.Toggle) return !(bool)current;
            if (DataType == DataType.Float || DataType == DataType.Double)
                return ParseValue((Convert.ToDouble(current, CultureInfo.InvariantCulture) + Step).ToString("R", CultureInfo.InvariantCulture));
            return ParseValue((Convert.ToDecimal(current, CultureInfo.InvariantCulture) + Convert.ToDecimal(Step)).ToString(CultureInfo.InvariantCulture));
        }

        public static string FormatValue(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public sealed class SimulationPointValue
    {
        public SimulationPointValue(SimulationPoint point, object value)
        {
            Address = point.Address;
            DataType = point.DataType;
            Value = value;
        }
        public string Address { get; }
        public DataType DataType { get; }
        public object Value { get; }
        public string Text => SimulationPoint.FormatValue(Value);
    }
}
