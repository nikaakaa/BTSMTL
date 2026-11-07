using System.Collections.Generic;
using System.Linq;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetPreviewReadIntervals : ITimelineStaticReadIntervals
    {
        public TimelineAssetPreviewReadIntervals(TimelineAssetPreviewPreferences preferences,
            IReadOnlyList<GraphHostInputDescriptor> descriptors,
            IReadOnlyList<TimelinePreviewInputRequest> requests)
        {
            try
            {
                Intervals = new TimelineStaticReadIntervals(preferences.Inputs.Select(ReadInput),
                    ReadConditions(), InputKind, requests.Select(request => request.StaticRequest), preferences.RangeEnd);
            }
            catch (TimelineStaticInputUnavailableException exception)
            {
                throw new TimelineAssetPreviewInputUnavailableException(exception.Message);
            }
            if (Intervals.ActivationIndex >= 0)
                Activation = preferences.Inputs[Intervals.ActivationIndex];

            OrchestrationValueKind? InputKind(string inputId) =>
                descriptors.FirstOrDefault(value => value.InputId == inputId)?.ValueKind switch
                {
                    GraphHostValueKind.Bool => OrchestrationValueKind.Boolean,
                    GraphHostValueKind.Int32 => OrchestrationValueKind.Int32,
                    GraphHostValueKind.Float32 => OrchestrationValueKind.Scalar,
                    GraphHostValueKind.Vector2 => OrchestrationValueKind.Vector2,
                    GraphHostValueKind.Vector3 => OrchestrationValueKind.Vector3,
                    GraphHostValueKind.Quaternion => OrchestrationValueKind.Quaternion,
                    GraphHostValueKind.Enum => OrchestrationValueKind.Enum,
                    GraphHostValueKind.Text => OrchestrationValueKind.Identity,
                    GraphHostValueKind.ObjectHandle => OrchestrationValueKind.ObjectHandle,
                    _ => null
                };

            TimelineStaticInputPoint ReadInput(TimelinePreviewInputPoint point)
            {
                bool vector2 = descriptors.Any(value => value.InputId == point.InputId && value.ValueKind == GraphHostValueKind.Vector2);
                return new TimelineStaticInputPoint(point.InputId, point.Time, point.Boolean, point.Integer, point.Scalar,
                    vector2 ? point.Vector2.x : point.Vector3.x, vector2 ? point.Vector2.y : point.Vector3.y, point.Vector3.z);
            }

            IEnumerable<TimelineStaticConditionPoint> ReadConditions()
            {
                foreach (TimelinePreviewConditionPoint point in preferences.ConditionPoints)
                    yield return new TimelineStaticConditionPoint(point.Key, point.Time, point.Satisfied, point.RestoreDefault);
            }
        }

        internal TimelineStaticReadIntervals Intervals { get; }
        public TimelinePreviewInputPoint Activation { get; }
        public OrchestrationTime RootStart => Intervals.RootStart;
        public IReadOnlyList<TimelineStaticInterval> HostInput(string inputId) => Intervals.HostInput(inputId);
        public IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read) => Intervals.RuntimeData(in read);

        public static bool DefaultSatisfied(in OrchestrationStaticRead read) =>
            TimelineStaticReadIntervals.DefaultSatisfied(in read);

        public bool ValueAt(in OrchestrationStaticRead read, OrchestrationTime time, out bool written) =>
            Intervals.ValueAt(in read, time, out written);
    }
}
