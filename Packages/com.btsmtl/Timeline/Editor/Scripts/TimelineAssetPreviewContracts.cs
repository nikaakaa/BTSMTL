using System;
using System.Collections.Generic;
using System.Threading;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetPreviewInputUnavailableException : InvalidOperationException
    {
        public TimelineAssetPreviewInputUnavailableException(string message) : base(message) { }
    }

    // 快速档按静态推导与目标时刻显示；真实档以同一输入方案经正式会话重演，显示执行记录。
    public enum TimelinePreviewMode : byte
    {
        Fast,
        Real
    }

    [Serializable]
    public sealed class TimelinePreviewInputPoint
    {
        public string InputId;
        public float Time;
        public bool Active = true;
        public bool Boolean;
        public int Integer;
        public float Scalar;
        public Vector2 Vector2;
        public Vector3 Vector3;
        public Quaternion Rotation = Quaternion.identity;
        public string Text = string.Empty;
    }

    [Serializable]
    public sealed class TimelinePreviewConditionAssumption
    {
        public string Identity;
        public bool Satisfied = true;
    }

    public readonly struct TimelinePreviewInputRequest
    {
        public TimelinePreviewInputRequest(string inputId, int tickRate, ulong bufferTicks)
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

    [Serializable]
    public sealed class TimelineAssetPreviewPreferences
    {
        public string Name = "默认";
        public List<TimelinePreviewInputPoint> Inputs = new();
        public List<TimelinePreviewConditionAssumption> Conditions = new();
        public bool HasTarget;
        public Vector3 TargetPosition;
        public Vector3 TargetEuler;
        public float RangeEnd = 10f;
    }

    public readonly struct TimelinePreviewDifference
    {
        public TimelinePreviewDifference(float time, OrchestrationStaticState state, string message)
        {
            Time = time;
            State = state;
            Message = message;
        }

        public float Time { get; }
        // 差异所在的源状态；为 null 时位于状态机外。
        public OrchestrationStaticState State { get; }
        public string Message { get; }
    }

    public readonly struct TimelineAssetPreviewTraversal
    {
        public TimelineAssetPreviewTraversal(TimelineAsset source, TimelineRuntimePreparedContent content,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, OrchestrationStaticTransition transition,
            OrchestrationStaticState state, bool playbackCompleted)
        {
            Source = source;
            Content = content;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            Transition = transition;
            State = state;
            PlaybackCompleted = playbackCompleted;
        }

        public TimelineAsset Source { get; }
        public TimelineRuntimePreparedContent Content { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        public OrchestrationStaticTransition Transition { get; }
        public OrchestrationStaticState State { get; }
        public bool PlaybackCompleted { get; }
    }

    public readonly struct TimelineAssetPreviewTarget
    {
        public TimelineAssetPreviewTarget(TimelineAsset source, OrchestrationTime time, int cycle,
            ulong revision, TimelineAssetPreviewPreferences preferences, OrchestrationTime previewTime,
            IReadOnlyList<TimelineAssetPreviewTraversal> traversals, long chainRevision)
        {
            Source = source;
            Time = time;
            Cycle = cycle;
            Revision = revision;
            Preferences = preferences;
            PreviewTime = previewTime;
            Traversals = traversals;
            ChainRevision = chainRevision;
        }

        // 同一链修订内，较晚目标的遍历是较早目标的延长，领域可以从上次目标继续推进。
        public long ChainRevision { get; }

        public TimelineAsset Source { get; }
        public OrchestrationTime Time { get; }
        public int Cycle { get; }
        public ulong Revision { get; }
        public TimelineAssetPreviewPreferences Preferences { get; }
        public OrchestrationTime PreviewTime { get; }
        public IReadOnlyList<TimelineAssetPreviewTraversal> Traversals { get; }
    }

    public sealed class TimelineAssetPreviewContent
    {
        public TimelineAssetPreviewContent(ITimelineDomainBindingResolver bindings,
            ITimelineRuntimeDependencyResolver dependencies, IReadOnlyList<ITimelineDomainPreview> domains,
            IReadOnlyList<TimelineCallBinding> inputs)
        {
            Bindings = bindings;
            Dependencies = dependencies;
            Domains = domains;
            Inputs = inputs;
        }

        public ITimelineDomainBindingResolver Bindings { get; }
        public ITimelineRuntimeDependencyResolver Dependencies { get; }
        public IReadOnlyList<ITimelineDomainPreview> Domains { get; }
        public IReadOnlyList<TimelineCallBinding> Inputs { get; }
    }

    public interface ITimelineDomainPreview : IDisposable
    {
        string Name { get; }
        bool SimulatesClipAge { get; }
        void Prepare(TimelineRuntimePreparedContent content);
        void Sample(in TimelineAssetPreviewTarget target, in TimelineRuntimePresentationOperations operations);
        void Stop();
    }

    public interface ITimelineAssetPreviewInstance : IDisposable
    {
        bool IsReady { get; }
        IReadOnlyList<GraphHostInputDescriptor> Inputs { get; }
        string Status { get; }
        TimelineAssetPreviewContent PrepareContent(TimelineAsset source, TimelineContentUnit content);
        void AdvancePreparation();
        void BeginTarget(in TimelineAssetPreviewTarget target);
        void CompleteTarget(in TimelineAssetPreviewTarget target);
        // 空闲时沿当前链往 horizon 提前推进，不改变当前显示；返回 true 表示还有剩余。
        bool Prefetch(in TimelineAssetPreviewTarget horizon, double seconds);
        void AbortTarget();
        void Stop();
    }

    public interface ITimelineAssetPreviewProvider
    {
        event Action SessionChanged;
        Type ConfigurationType { get; }
        UnityEngine.Object Configuration { get; set; }
        string ScenePath { get; }
        TimelineContractCatalog Contracts { get; }
        TimelineRuntimeNumericTarget NumericTarget { get; }
        ITimelineAssetPreviewInstance CreateInstance(Scene scene, UnityEngine.Object configuration);
        bool TryCloseSession(out string reason);
        void OpenSession(TimelineAsset source, TimelineAssetPreviewPreferences preferences, CancellationToken cancellationToken);
        IReadOnlyList<GraphHostInputDescriptor> DescribeInputs();
        IReadOnlyList<TimelinePreviewInputRequest> DescribeInputRequests();
        IReadOnlyList<TimelinePreviewInputPoint> ImportRecordedInputs(string path);
        // 真实档：工具栏、会话游标与执行记录中 Timeline 的作者来源均由宿主提供；
        // 编辑时间轴上的时间拖动经宿主设置的 ITimelineTimeRangePreview 写回。
        VisualElement CreateRealControls();
        bool CanObserveReal { get; }
        float RealTime { get; }
        void SeekReal(float time);
        bool TryResolveRealTimeline(string timelineAuthoringId, out TimelineAsset source);
    }
}
