using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Graphs;
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
        readonly IEnumerable<TimelineStaticConditionPoint> m_Conditions;
        readonly Dictionary<string, IReadOnlyList<TimelineStaticInterval>> m_Intervals = new(StringComparer.Ordinal);
        // 与原 Native Vector2/Vector3 的零向量比较使用同一平方阈值。
        const float VectorZeroSquared = 1e-5f * 1e-5f;

        public TimelineStaticReadIntervals(IEnumerable<TimelineStaticInputPoint> inputs,
            IEnumerable<TimelineStaticConditionPoint> conditions, IReadOnlyList<GraphHostInputDescriptor> descriptors,
            IEnumerable<TimelineStaticInputRequest> requests, double rangeEnd)
        {
            m_Conditions = conditions;
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
                GraphHostInputDescriptor descriptor = descriptors.FirstOrDefault(value => value.InputId == input.Key);
                if (descriptor == null)
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
                        if (Holds(descriptor.ValueKind, ordered[i].Point))
                            intervals.Add(new TimelineStaticInterval(OrchestrationTime.FromSeconds(ordered[i].Point.Time),
                                OrchestrationTime.FromSeconds(i + 1 < ordered.Length ? ordered[i + 1].Point.Time : rangeEnd)));
                m_Intervals.Add(input.Key, intervals);
            }
        }

        public int ActivationIndex { get; }
        public OrchestrationTime RootStart { get; } = OrchestrationTime.Zero;

        static double TickCeiling(double seconds, int tickRate) => Math.Ceiling(seconds * tickRate - 1e-6) / tickRate;

        static bool Holds(GraphHostValueKind kind, TimelineStaticInputPoint point) => kind switch
        {
            GraphHostValueKind.Bool => point.Boolean,
            GraphHostValueKind.Int32 or GraphHostValueKind.Enum => point.Integer != 0,
            GraphHostValueKind.Float32 => point.Scalar != 0f,
            GraphHostValueKind.Vector2 => !(point.X * point.X + point.Y * point.Y < VectorZeroSquared),
            GraphHostValueKind.Vector3 => !(point.X * point.X + point.Y * point.Y + point.Z * point.Z < VectorZeroSquared),
            _ => true
        };

        public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) =>
            m_Intervals.TryGetValue(inputId, out IReadOnlyList<TimelineStaticInterval> intervals)
                ? intervals : Array.Empty<TimelineStaticInterval>();

        public IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read)
        {
            bool fallback = DefaultSatisfied(in read);
            bool value = fallback;
            var result = new List<TimelineStaticInterval>();
            OrchestrationTime start = OrchestrationTime.Zero;
            foreach (TimelineStaticConditionPoint point in m_Conditions)
            {
                bool next = point.RestoreDefault ? fallback : point.Satisfied;
                if (point.Key != read.Key || next == value)
                    continue;
                OrchestrationTime at = OrchestrationTime.FromSeconds(point.Time);
                if (value && at > start)
                    result.Add(new TimelineStaticInterval(start, at));
                value = next;
                start = at;
            }
            if (value)
                result.Add(new TimelineStaticInterval(start, OrchestrationTime.MaxValue));
            return result;
        }

        public static bool DefaultSatisfied(in OrchestrationStaticRead read) =>
            read.Source != OrchestrationStaticReadSource.ClipState;

        public bool ValueAt(in OrchestrationStaticRead read, OrchestrationTime time, out bool written)
        {
            bool fallback = DefaultSatisfied(in read);
            bool value = fallback;
            written = false;
            foreach (TimelineStaticConditionPoint point in m_Conditions)
                if (point.Key == read.Key && OrchestrationTime.FromSeconds(point.Time) <= time)
                {
                    value = point.RestoreDefault ? fallback : point.Satisfied;
                    written = !point.RestoreDefault;
                }
            return value;
        }
    }
}
