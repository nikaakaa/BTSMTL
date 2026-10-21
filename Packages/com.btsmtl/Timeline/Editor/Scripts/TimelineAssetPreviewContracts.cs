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

    // 输入 Timeline 读出的一次值变化（预览全局时刻），只作派生数据。
    public sealed class TimelinePreviewInputPoint
    {
        public string InputId;
        public float Time;
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
        // 方案的输入 Timeline，方案资产的子资产；第一次编辑输入时创建。
        public TimelineAsset InputTimeline;
        // 由输入 Timeline 读出的值变化，按时刻排序；两档与领域取值只读这一份，不保存。
        [NonSerialized] public List<TimelinePreviewInputPoint> Inputs = new();
        // 输入 Timeline 的读取错误；有错误时 Inputs 为空。
        [NonSerialized] public List<string> InputErrors = new();
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

    // 一段 Timeline 播放区间。Branch 为 0 是主链，其余是状态 Root 中并行状态机的分支或 Clip 调用拉起的嵌套播放；
    // Playback 是整条路线中的第几次播放。嵌套播放带调用点与调用方播放编号，State 为 null。
    public readonly struct TimelineAssetPreviewTraversal
    {
        public TimelineAssetPreviewTraversal(TimelineAsset source, TimelineRuntimePreparedContent content,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, OrchestrationStaticTransition transition,
            OrchestrationStaticState state, bool playbackCompleted, int branch, int playback,
            OrchestrationStaticTimelineCall call = null, int callerPlayback = -1)
        {
            Call = call;
            CallerPlayback = callerPlayback;
            Branch = branch;
            Playback = playback;
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
        public int Branch { get; }
        public int Playback { get; }
        public OrchestrationStaticTimelineCall Call { get; }
        public int CallerPlayback { get; }
        public OrchestrationTime LocalAt(OrchestrationTime time) =>
            OrchestrationTime.Min(LocalEnd, LocalStart + time - Start);

        // 截到 time 为止的部分；到达原终点时才保留播完。
        public TimelineAssetPreviewTraversal Until(OrchestrationTime time)
        {
            OrchestrationTime end = OrchestrationTime.Min(End, time);
            return new TimelineAssetPreviewTraversal(Source, Content, Start, end, LocalStart, LocalAt(end), Cycle,
                Transition, State, PlaybackCompleted && end == End, Branch, Playback, Call, CallerPlayback);
        }
    }

    // 两档共用的路线显示信息：经过的一段 Timeline 播放区间。Playback 相同的段属于同一次播放，只是按圈或分支边界拆开；
    // Occurrence 是该次播放在同一 Timeline 的第几次播放。真实档段带执行记录，本地与预览时刻经其正式游标换算。
    public readonly struct TimelinePreviewRouteSegment
    {
        public TimelinePreviewRouteSegment(TimelineAsset source, OrchestrationStaticState state, OrchestrationTime start,
            OrchestrationTime end, OrchestrationTime localStart, OrchestrationTime localEnd, int cycle, int playback,
            int occurrence, bool playbackCompleted, BtsmtlExecutionRecord.RecordedPlayback? recorded,
            int callerPlayback = -1, string callerClipId = null, OrchestrationStaticTimelineCall call = null)
        {
            CallerPlayback = callerPlayback;
            CallerClipId = callerClipId ?? string.Empty;
            Call = call;
            Source = source;
            State = state;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            Playback = playback;
            Occurrence = occurrence;
            PlaybackCompleted = playbackCompleted;
            Recorded = recorded;
        }

        public int Occurrence { get; }
        // Clip 或 Marker 调用拉起的嵌套播放：调用方是路线中的第几次播放与调用所在元素；快速档另带静态调用点。
        // 调用方不在路线中为 -1。
        public int CallerPlayback { get; }
        public string CallerClipId { get; }
        public OrchestrationStaticTimelineCall Call { get; }
        public bool IsNested => CallerClipId.Length != 0;
        // 真实档的执行记录；快速档为 null。
        public BtsmtlExecutionRecord.RecordedPlayback? Recorded { get; }
        public string Label => $"第 {Occurrence + 1} 次 · 第 {Cycle + 1} 圈";
        public bool Contains(OrchestrationTime time) => Start <= time && time <= End;

        // 预览时刻 → 本地时刻：快速档按推导的一倍速，真实档读该次播放的正式游标；区间外与无记录处拒绝，不外推。
        public bool TryLocal(OrchestrationTime time, out OrchestrationTime local, out string message)
        {
            local = OrchestrationTime.Zero;
            message = null;
            if (!Contains(time))
            {
                message = $"预览第 {time.AsSingle:0.000} 秒不在{Label}内。";
                return false;
            }
            if (Recorded is not { } recorded)
            {
                local = OrchestrationTime.Min(LocalEnd, LocalStart + time - Start);
                return true;
            }
            if (!recorded.TryLocal(Math.Clamp(time.AsSingle, recorded.Start, recorded.End), Source.Data.Duration,
                out double value, out message))
                return false;
            local = OrchestrationTime.FromSeconds(value);
            return true;
        }

        // 本地时刻 → 预览时刻：快速档该次播放从同一起点按一倍速继续；真实档只在本圈的正式游标记录内换算。
        public bool TryGlobal(OrchestrationTime local, out OrchestrationTime time, out string message)
        {
            message = null;
            if (Recorded is not { } recorded)
            {
                time = Start + local - LocalStart;
                if (LocalStart == LocalEnd && End > Start)
                    message = "播放已停留在末帧；定位到首次到达该帧的时刻。";
                return true;
            }
            time = OrchestrationTime.Zero;
            if (!recorded.TryGlobal(local.AsSingle, Source.Data.Duration, out double value, out message))
                return false;
            time = OrchestrationTime.FromSeconds(value);
            return true;
        }

        public TimelineAsset Source { get; }
        // 不在当前静态查询中的状态为 null。
        public OrchestrationStaticState State { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        public int Playback { get; }
        public bool PlaybackCompleted { get; }
    }

    public readonly struct TimelineAssetPreviewTarget
    {
        public TimelineAssetPreviewTarget(TimelineAsset source, OrchestrationTime time, int cycle,
            ulong revision, TimelineAssetPreviewPreferences preferences, OrchestrationTime previewTime,
            IReadOnlyList<TimelineAssetPreviewTraversal> traversals,
            IReadOnlyList<TimelineAssetPreviewTraversal> parallel, long chainRevision)
        {
            Source = source;
            Time = time;
            Cycle = cycle;
            Revision = revision;
            Preferences = preferences;
            PreviewTime = previewTime;
            Traversals = traversals;
            Parallel = parallel;
            ChainRevision = chainRevision;
        }

        // 并行分支的播放区间（截到 PreviewTime），按分支、再按时刻排列；领域在每个时刻把生效分支的贡献与主链一起交给正式模块。
        public IReadOnlyList<TimelineAssetPreviewTraversal> Parallel { get; }

        // time 时各分支生效的那一段；同一分支在转移处取后一段。
        public void ActiveParallel(OrchestrationTime time, List<TimelineAssetPreviewTraversal> result)
        {
            result.Clear();
            foreach (TimelineAssetPreviewTraversal traversal in Parallel)
            {
                if (traversal.Start > time || traversal.End < time)
                    continue;
                if (result.Count != 0 && result[^1].Branch == traversal.Branch)
                    result[^1] = traversal;
                else
                    result.Add(traversal);
            }
        }

        // 截到 time 为止的目标：较早时刻的整组播放与贡献由同一组区间重建。
        public TimelineAssetPreviewTarget Until(OrchestrationTime time)
        {
            var traversals = new List<TimelineAssetPreviewTraversal>();
            foreach (TimelineAssetPreviewTraversal traversal in Traversals)
                if (traversal.Start <= time)
                    traversals.Add(traversal.Until(time));
            var parallel = new List<TimelineAssetPreviewTraversal>();
            foreach (TimelineAssetPreviewTraversal traversal in Parallel)
                if (traversal.Start <= time)
                    parallel.Add(traversal.Until(time));
            TimelineAssetPreviewTraversal current = traversals[^1];
            return new TimelineAssetPreviewTarget(current.Source, current.LocalEnd, current.Cycle, Revision,
                Preferences, time, traversals, parallel, ChainRevision);
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

    // 真实档控制分三处放入 Timeline 窗口唯一的预览工具栏：内联播放组、⚙ 设置面板与状态行尾部；布局由窗口持有。
    public sealed class TimelinePreviewRealControls
    {
        public TimelinePreviewRealControls(VisualElement transport, VisualElement settings, VisualElement status)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Status = status ?? throw new ArgumentNullException(nameof(status));
        }

        public VisualElement Transport { get; }
        public VisualElement Settings { get; }
        public VisualElement Status { get; }
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
        TimelinePreviewRealControls CreateRealControls();
        bool CanObserveReal { get; }
        float RealTime { get; }
        void SeekReal(float time);
        bool TryResolveRealTimeline(string timelineAuthoringId, out TimelineAsset source);
        // 输入方案修改后替换会话的输入日程，不立即重演；由宿主原“重新播放”或“应用并重新预览”入口采用。
        void ReplaceRealInputs(TimelineAssetPreviewPreferences preferences);
        // 当前执行记录是否按最新输入日程与作者内容得到；否则给出待重演原因。
        bool IsRealResultCurrent(out string reason);
    }
}
