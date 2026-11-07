using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation.Analysis
{
    public readonly struct TimelineStaticInputPoint
    {
        public TimelineStaticInputPoint(string inputId, double time, bool boolean = false,
            int integer = 0, float scalar = 0, float x = 0, float y = 0, float z = 0)
        {
            InputId = inputId;
            Time = time;
            Boolean = boolean;
            Integer = integer;
            Scalar = scalar;
            X = x;
            Y = y;
            Z = z;
        }

        public string InputId { get; }
        public double Time { get; }
        public bool Boolean { get; }
        public int Integer { get; }
        public float Scalar { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    public readonly struct TimelineStaticConditionPoint
    {
        public TimelineStaticConditionPoint(string key, double time, bool satisfied, bool restoreDefault)
        {
            Key = key;
            Time = time;
            Satisfied = satisfied;
            RestoreDefault = restoreDefault;
        }

        public string Key { get; }
        public double Time { get; }
        public bool Satisfied { get; }
        public bool RestoreDefault { get; }
    }

    public readonly struct TimelineStaticInputRequest
    {
        public TimelineStaticInputRequest(string inputId, int tickRate, ulong bufferTicks)
        {
            InputId = inputId;
            TickRate = tickRate;
            BufferTicks = bufferTicks;
        }

        public string InputId { get; }
        public int TickRate { get; }
        public ulong BufferTicks { get; }
        public double DurationSeconds => (BufferTicks + 1) / (double)TickRate;
    }

    public sealed class TimelineStaticInputUnavailableException : InvalidOperationException
    {
        public TimelineStaticInputUnavailableException(string message) : base(message) { }
    }

    public sealed class TimelineStaticReadIntervals : ITimelineStaticReadIntervals
    {
        static readonly IReadOnlyList<TimelineStaticInterval> s_DefaultSatisfiedIntervals = Array.AsReadOnly(new[]
        {
            new TimelineStaticInterval(OrchestrationTime.Zero, OrchestrationTime.MaxValue)
        });

        readonly ILookup<string, (OrchestrationTime Time, bool Satisfied, bool RestoreDefault)> m_Conditions;
        readonly Dictionary<(string Key, bool DefaultValue), IReadOnlyList<TimelineStaticInterval>> m_RuntimeIntervals = new();
        readonly Dictionary<string, IReadOnlyList<TimelineStaticInterval>> m_Intervals = new(StringComparer.Ordinal);
        // 与原 Native Vector2/Vector3 的零向量比较使用同一平方阈值。
        const float VectorZeroSquared = 1e-5f * 1e-5f;

        public TimelineStaticReadIntervals(IEnumerable<TimelineStaticInputPoint> inputs,
            IEnumerable<TimelineStaticConditionPoint> conditions, Func<string, OrchestrationValueKind?> inputKind,
            IEnumerable<TimelineStaticInputRequest> requests, double rangeEnd)
        {
            var requestInputs = requests.ToDictionary(request => request.InputId, StringComparer.Ordinal);
            var points = inputs.Select((point, index) => (Point: point, Index: index)).ToArray();
            // 最早动作请求由起手消耗。起手和后续动作请求都在逻辑 Tick 读取。
            ActivationIndex = points.Where(value => value.Point.Boolean && requestInputs.ContainsKey(value.Point.InputId))
                .OrderBy(value => value.Point.Time).Select(value => value.Index).DefaultIfEmpty(-1).First();
            if (ActivationIndex >= 0)
            {
                TimelineStaticInputPoint activation = points[ActivationIndex].Point;
                RootStart = OrchestrationTime.FromSeconds(TickCeiling(activation.Time, requestInputs[activation.InputId].TickRate));
            }
            foreach (var input in points.GroupBy(value => value.Point.InputId))
            {
                var ordered = input.OrderBy(value => value.Point.Time).ToArray();
                OrchestrationValueKind? kind = inputKind(input.Key);
                if (!kind.HasValue)
                    throw new TimelineStaticInputUnavailableException($"预览日程中的输入 '{input.Key}' 已不在当前角色的正式输入声明中，请删除该日程项。");
                var intervals = new List<TimelineStaticInterval>();
                if (requestInputs.TryGetValue(input.Key, out TimelineStaticInputRequest request))
                {
                    foreach (var value in ordered)
                        if (value.Point.Boolean && value.Index != ActivationIndex)
                        {
                            double start = TickCeiling(value.Point.Time, request.TickRate);
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(start),
                                OrchestrationTime.FromSeconds(start + request.DurationSeconds)));
                        }
                }
                else
                    for (int i = 0; i < ordered.Length; i++)
                        if (Holds(kind.Value, ordered[i].Point))
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(ordered[i].Point.Time),
                                OrchestrationTime.FromSeconds(i + 1 < ordered.Length ? ordered[i + 1].Point.Time : rangeEnd)));
                m_Intervals.Add(input.Key, intervals);
            }

            m_Conditions = conditions.ToLookup(point => point.Key,
                point => (OrchestrationTime.FromSeconds(point.Time), point.Satisfied, point.RestoreDefault), StringComparer.Ordinal);
            foreach (var group in m_Conditions)
            {
                m_RuntimeIntervals.Add((group.Key, false), ConditionIntervals(group, false));
                m_RuntimeIntervals.Add((group.Key, true), ConditionIntervals(group, true));
            }
        }

        public int ActivationIndex { get; }
        public OrchestrationTime RootStart { get; } = OrchestrationTime.Zero;

        static double TickCeiling(double seconds, int tickRate) => Math.Ceiling(seconds * tickRate - 1e-6) / tickRate;

        static bool Holds(OrchestrationValueKind kind, TimelineStaticInputPoint point) => kind switch
        {
            OrchestrationValueKind.Boolean => point.Boolean,
            OrchestrationValueKind.Int32 or OrchestrationValueKind.Enum => point.Integer != 0,
            OrchestrationValueKind.Scalar => point.Scalar != 0f,
            OrchestrationValueKind.Vector2 => !(point.X * point.X + point.Y * point.Y < VectorZeroSquared),
            OrchestrationValueKind.Vector3 => !(point.X * point.X + point.Y * point.Y + point.Z * point.Z < VectorZeroSquared),
            _ => true
        };

        public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) =>
            m_Intervals.TryGetValue(inputId, out IReadOnlyList<TimelineStaticInterval> intervals)
                ? intervals : Array.Empty<TimelineStaticInterval>();

        public IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read)
        {
            bool defaultValue = DefaultSatisfied(in read);
            return m_RuntimeIntervals.TryGetValue((read.Key, defaultValue), out IReadOnlyList<TimelineStaticInterval> intervals)
                ? intervals : defaultValue ? s_DefaultSatisfiedIntervals : Array.Empty<TimelineStaticInterval>();
        }

        static IReadOnlyList<TimelineStaticInterval> ConditionIntervals(
            IEnumerable<(OrchestrationTime Time, bool Satisfied, bool RestoreDefault)> points, bool defaultValue)
        {
            bool value = defaultValue;
            var result = new List<TimelineStaticInterval>();
            OrchestrationTime start = OrchestrationTime.Zero;
            foreach (var point in points)
            {
                bool next = point.RestoreDefault ? defaultValue : point.Satisfied;
                if (next == value)
                    continue;
                if (value && point.Time > start)
                    result.Add(new TimelineStaticInterval(start, point.Time));
                value = next;
                start = point.Time;
            }
            if (value)
                result.Add(new TimelineStaticInterval(start, OrchestrationTime.MaxValue));
            return result.AsReadOnly();
        }

        public static bool DefaultSatisfied(in OrchestrationStaticRead read) =>
            read.Source != OrchestrationStaticReadSource.ClipState;

        public bool ValueAt(in OrchestrationStaticRead read, OrchestrationTime time, out bool written)
        {
            bool defaultValue = DefaultSatisfied(in read);
            bool value = defaultValue;
            written = false;
            foreach (var point in m_Conditions[read.Key])
                if (point.Time <= time)
                {
                    value = point.RestoreDefault ? defaultValue : point.Satisfied;
                    written = !point.RestoreDefault;
                }
            return value;
        }
    }
}
