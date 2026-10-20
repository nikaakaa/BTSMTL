using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Diagnostics.Editor;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    [InitializeOnLoad]
    public sealed class TimelineAssetPreviewHost
    {
        internal const string MissingProviderReason = "当前项目尚未注册 Timeline 预览宿主。";
        public static TimelineAssetPreviewHost Shared { get; } = new();

        ITimelineAssetPreviewProvider m_Provider;
        ITimelineAssetPreviewInstance m_Instance;
        TimelineAssetPreviewStage m_Stage;
        sealed class PreparedTimeline
        {
            internal TimelineAsset Source;
            internal string Fingerprint;
            internal Action Changed;
            internal TimelineAssetPreviewContent Domains;
            internal TimelineRuntimePreparedContent Content;
            internal TimelineRuntimePresentationTargetSampling Sampling;
        }

        readonly Dictionary<TimelineAsset, PreparedTimeline> m_Contents = new();
        readonly HashSet<TimelineAsset> m_Dirty = new();
        const double PrefetchSeconds = 0.008;
        bool m_Prefetching;
        readonly List<TimelineAssetPreviewTraversal> m_Traversals = new();
        readonly List<OrchestrationStaticRead> m_Assumptions = new();
        TimelineOrchestrationQuery m_Query;
        TimelinePreviewInputSchemes m_Schemes;
        BtsmtlExecutionTimeBarDataSource m_Real;
        long m_RealVersion = -1;
        long m_RealProjection = -1;
        Guid m_RealCapture;
        OrchestrationStaticState m_EntryState;
        TimelineAsset m_PendingSource;
        TimelinePreviewMode m_PendingMode;
        CancellationTokenSource m_SessionOpening;
        bool m_ChainRequested;
        bool m_Opening;
        bool m_SampleRequested;
        double m_LastUpdate;
        ulong m_Revision;

        static TimelineAssetPreviewHost()
        {
            EditorApplication.update += Shared.Update;
            Undo.undoRedoPerformed += Shared.OnUndo;
            AssemblyReloadEvents.beforeAssemblyReload += Shared.Close;
            EditorApplication.quitting += Shared.Close;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    Shared.Close();
            };
        }

        public event Action Changed;
        public TimelinePreviewMode Mode { get; private set; }
        public TimelineAsset Source { get; private set; }
        public TimelineAssetPreviewPreferences Preferences { get; private set; }
        public ITimelineAssetPreviewInstance Instance => m_Instance;
        public TimelineContractCatalog Contracts => m_Provider.Contracts;
        public bool IsRegistered => m_Provider != null;
        public bool IsOpen => m_Stage || m_Real != null;
        public bool IsReal => m_Real != null;
        public IReadOnlyList<GraphHostInputDescriptor> Inputs =>
            m_Instance != null ? m_Instance.Inputs : m_Provider.DescribeInputs();
        // 真实档的静态推导路线，只供与执行记录对比。
        public TimelineStaticChain FastChain { get; private set; }
        // 真实档执行记录与快速推导的差异，按预览时刻排列。
        public IReadOnlyList<TimelinePreviewDifference> Differences => m_Differences;
        readonly List<TimelinePreviewDifference> m_Differences = new();
        public VisualElement CreateRealControls() => m_Provider?.CreateRealControls();
        public bool IsOpeningSession => m_SessionOpening != null;
        public bool IsPlaying { get; private set; }
        public float Time { get; private set; }
        public float Duration => m_Traversals.Count == 0 ? 0f : m_Traversals[^1].End.AsSingle;
        public float LocalTime { get; private set; }
        public TimelineAsset SampledSource { get; private set; }
        public ulong SampledRevision { get; private set; }
        public TimelineStaticChain Chain { get; private set; }
        public TimelineOrchestrationQuery Query => m_Query;
        public IReadOnlyList<TimelineAssetPreviewTraversal> Traversals => m_Traversals;
        public IReadOnlyList<OrchestrationStaticRead> Assumptions => m_Assumptions;
        public IReadOnlyList<TimelinePreviewInputRequest> InputRequests => m_Provider.DescribeInputRequests();
        public long Revision { get; private set; }
        public string Error { get; private set; } = string.Empty;
        public string Status => m_Instance == null ? Error : m_Instance.Status;
        public Type ConfigurationType => m_Provider == null ? typeof(UnityEngine.Object) : m_Provider.ConfigurationType;
        public UnityEngine.Object Configuration
        {
            get => m_Provider == null ? null : m_Provider.Configuration;
            set
            {
                if (!IsRegistered)
                    throw new InvalidOperationException(MissingProviderReason);
                Close();
                m_Provider.Configuration = value;
                Changed?.Invoke();
            }
        }

        public void Register(ITimelineAssetPreviewProvider provider)
        {
            m_Provider = provider;
            provider.SessionChanged += ContinueOpen;
            Changed?.Invoke();
        }

        public void Open(TimelineAsset source, TimelinePreviewMode mode,
            TimelineOrchestrationQuery query, OrchestrationStaticState entryState)
        {
            Close();
            m_Query = query;
            m_EntryState = entryState;
            Error = string.Empty;
            if (m_Provider == null)
            {
                Error = MissingProviderReason;
                Changed?.Invoke();
                return;
            }
            m_PendingSource = source;
            m_PendingMode = mode;
            ContinueOpen();
        }

        void ContinueOpen()
        {
            if (!m_PendingSource || m_Opening)
                return;
            m_Opening = true;
            bool closed;
            string reason;
            try { closed = m_Provider.TryCloseSession(out reason); }
            finally { m_Opening = false; }
            if (!closed)
            {
                Error = reason;
                Changed?.Invoke();
                return;
            }
            StageUtility.GoToMainStage();
            Mode = m_PendingMode;
            Source = m_PendingSource;
            m_PendingSource = null;
            Preferences = LoadPreferences(Source);
            Error = string.Empty;
            if (Mode == TimelinePreviewMode.Real)
            {
                m_Real = new BtsmtlExecutionTimeBarDataSource(RuntimeDebugSession.Shared, m_Provider.OpenRealSource,
                    m_Provider.SeekReal, () => m_Provider.RealTime, () => m_Provider.CanObserveReal);
                m_Real.ConfigureAuthoring(m_Provider.RealAuthoring);
                m_RealVersion = -1;
                m_SessionOpening = new CancellationTokenSource();
                m_Provider.OpenSession(Source, Preferences, m_SessionOpening.Token);
                BuildFastChain();
                Changed?.Invoke();
                return;
            }
            if (!m_Provider.Configuration)
            {
                Error = "请选择资产预览配置。";
                Changed?.Invoke();
                return;
            }
            m_Stage = ScriptableObject.CreateInstance<TimelineAssetPreviewStage>();
            m_Stage.Owner = this;
            m_Stage.ScenePath = m_Provider.ScenePath;
            try
            {
                StageUtility.GoToStage(m_Stage, false);
                m_Instance = m_Provider.CreateInstance(m_Stage.scene, m_Provider.Configuration);
                m_Dirty.Add(Source);
                m_ChainRequested = true;
                m_LastUpdate = EditorApplication.timeSinceStartup;
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                Close();
                Error = exception.Message;
                StageUtility.GoToMainStage();
            }
            Changed?.Invoke();
        }

        public void SetTime(float time)
        {
            if (m_Real != null)
            {
                if (m_Provider.CanObserveReal)
                    m_Provider.SeekReal(Mathf.Clamp(time, 0f, Duration));
                return;
            }
            Time = Mathf.Clamp(time, 0f, Duration);
            m_SampleRequested = true;
            Changed?.Invoke();
        }

        // 预览链经过的 Timeline 都由同一预览显示，编辑窗口切到其中任一条时预览保持打开。
        public bool Covers(TimelineAsset source) =>
            source && (source == Source || m_Traversals.Exists(traversal => traversal.Source == source));

        public void SetLocalTime(TimelineAsset source, float time)
        {
            int current = 0;
            for (int i = 0; i < m_Traversals.Count; i++)
                if (m_Traversals[i].Start.AsSingle <= Time)
                    current = i;
            int first = current;
            while (first > 0 && m_Traversals[first].Transition == null &&
                   m_Traversals[first - 1].Source == m_Traversals[current].Source &&
                   m_Traversals[first - 1].Cycle == m_Traversals[current].Cycle)
                first--;
            int last = current;
            while (last + 1 < m_Traversals.Count && m_Traversals[last + 1].Transition == null &&
                   m_Traversals[last + 1].Source == m_Traversals[current].Source &&
                   m_Traversals[last + 1].Cycle == m_Traversals[current].Cycle)
                last++;
            float nearest = 0f;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < m_Traversals.Count; i++)
            {
                TimelineAssetPreviewTraversal traversal = m_Traversals[i];
                if (traversal.Source == source && time >= traversal.LocalStart.AsSingle &&
                    time <= traversal.LocalEnd.AsSingle)
                {
                    float mapped = traversal.Start.AsSingle + time - traversal.LocalStart.AsSingle;
                    if (i >= first && i <= last)
                    {
                        SetTime(mapped);
                        return;
                    }
                    float difference = Mathf.Abs(mapped - Time);
                    if (difference < distance)
                    {
                        nearest = mapped;
                        distance = difference;
                    }
                }
            }
            if (float.IsFinite(distance))
                SetTime(nearest);
            else
            {
                Error = "当前输入推导没有经过所选本地时刻；请调整输入／假设，或在会话中验证。";
                Changed?.Invoke();
            }
        }

        public void SetOrchestrationContext(TimelineAsset source, TimelineOrchestrationQuery query, OrchestrationStaticState state)
        {
            // 窗口在预览链的 Timeline 间切换会重复送来同一上下文，不重新准备。
            if (source != Source || query == m_Query && state == m_EntryState)
                return;
            m_Query = query;
            m_EntryState = state;
            foreach (TimelineAsset preparedSource in m_Contents.Keys)
                m_Dirty.Add(preparedSource);
            m_ChainRequested = true;
        }

        public void SetPlaying(bool playing)
        {
            IsPlaying = playing;
            m_LastUpdate = EditorApplication.timeSinceStartup;
            Changed?.Invoke();
        }

        public void Invalidate()
        {
            if (IsOpen)
            {
                m_Dirty.Add(Source);
                m_ChainRequested = true;
            }
        }

        void OnUndo()
        {
            foreach (PreparedTimeline timeline in m_Contents.Values)
                if (timeline.Fingerprint != TimelineAuthoringFingerprint.Compute(timeline.Source.Data))
                    m_Dirty.Add(timeline.Source);
        }

        public void PreferencesChanged()
        {
            Persist();
            if (m_Real != null)
            {
                // 真实档的会话已按打开时的方案重演；新方案在重新进入真实档时生效。
                BuildFastChain();
                Error = "输入方案已修改：重新进入真实档后按新方案重演。";
                Changed?.Invoke();
                return;
            }
            m_ChainRequested = true;
            m_SampleRequested = true;
            Changed?.Invoke();
        }

        public void ImportInputs(string path)
        {
            IReadOnlyList<TimelinePreviewInputPoint> imported;
            try
            {
                imported = m_Provider.ImportRecordedInputs(path);
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                Changed?.Invoke();
                return;
            }
            Preferences.Inputs.Clear();
            Preferences.Inputs.AddRange(imported);
            PreferencesChanged();
            AssetDatabase.SaveAssetIfDirty(m_Schemes);
        }

        public void ReportSessionFailure(string reason)
        {
            ReportSessionReady();
            Error = reason;
            Changed?.Invoke();
        }

        public void ReportSessionReady()
        {
            m_SessionOpening?.Dispose();
            m_SessionOpening = null;
        }

        public void Close()
        {
            bool closeSession = IsOpeningSession || m_Real != null;
            if (m_Real != null)
            {
                m_Real.Clear();
                m_Real = null;
                m_Traversals.Clear();
                m_Differences.Clear();
                FastChain = null;
                SampledSource = null;
                Changed?.Invoke();
            }
            m_SessionOpening?.Cancel();
            m_SessionOpening?.Dispose();
            m_SessionOpening = null;
            m_PendingSource = null;
            IsPlaying = false;
            if (m_Schemes)
                AssetDatabase.SaveAssetIfDirty(m_Schemes);
            if (m_Stage)
                StageUtility.GoToMainStage();
            else
                ReleaseStage();
            if (closeSession && !m_Provider.TryCloseSession(out string reason))
            {
                Error = reason;
                Changed?.Invoke();
            }
        }

        internal void ReleaseStage()
        {
            foreach (PreparedTimeline timeline in m_Contents.Values)
            {
                timeline.Source.Data.OnValueChanged -= timeline.Changed;
                foreach (ITimelineDomainPreview domain in timeline.Domains.Domains)
                {
                    domain.Stop();
                    domain.Dispose();
                }
            }
            m_Contents.Clear();
            m_Dirty.Clear();
            m_Traversals.Clear();
            m_Assumptions.Clear();
            Chain = null;
            SampledSource = null;
            SampledRevision = 0;
            m_SampleRequested = false;
            try
            {
                if (m_Instance != null)
                    m_Instance.Stop();
            }
            finally
            {
                m_Instance?.Dispose();
                m_Instance = null;
                m_Stage = null;
                Changed?.Invoke();
            }
        }

        void Update()
        {
            if (!IsOpen || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (m_Real != null)
            {
                UpdateReal();
                return;
            }
            try
            {
                if (m_Dirty.Count != 0)
                {
                    TimelineAsset[] changed = m_Dirty.ToArray();
                    m_Dirty.Clear();
                    // 只有作者内容真的变了才重建链；编辑器刷新触发的同内容通知不打断预览的接续推进。
                    foreach (TimelineAsset timeline in changed)
                    {
                        m_Contents.TryGetValue(timeline, out PreparedTimeline before);
                        if (before != null && before.Fingerprint == TimelineAuthoringFingerprint.Compute(timeline.Data))
                            continue;
                        if (Prepare(timeline, true) != before)
                            m_ChainRequested = true;
                    }
                }
                if (m_ChainRequested)
                    BuildChain();
                if (!m_Instance.IsReady)
                {
                    m_Instance.AdvancePreparation();
                    return;
                }
                double now = EditorApplication.timeSinceStartup;
                if (IsPlaying)
                {
                    Time = Mathf.Min(Duration, Time + (float)(now - m_LastUpdate));
                    m_SampleRequested = true;
                    if (Time == Duration)
                        IsPlaying = false;
                }
                m_LastUpdate = now;
                if (!m_SampleRequested)
                {
                    // 空闲时每帧花一点时间把链往后推进，之后拖到哪里都不用现算。
                    if (m_Prefetching && !IsPlaying && m_Traversals.Count != 0)
                    {
                        TimelineAssetPreviewTraversal last = m_Traversals[^1];
                        var horizon = new TimelineAssetPreviewTarget(last.Source, last.LocalEnd, last.Cycle,
                            m_Revision, Preferences, last.End, m_Traversals.ToArray(), Revision);
                        m_Prefetching = m_Instance.Prefetch(in horizon, PrefetchSeconds);
                    }
                    return;
                }
                m_SampleRequested = false;
                if (m_Traversals.Count == 0)
                    return;
                var traversals = new List<TimelineAssetPreviewTraversal>();
                OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
                foreach (TimelineAssetPreviewTraversal span in m_Traversals)
                {
                    if (span.Start > cursor)
                        break;
                    OrchestrationTime end = OrchestrationTime.Min(span.End, cursor);
                    traversals.Add(new TimelineAssetPreviewTraversal(span.Source, span.Content, span.Start,
                        end, span.LocalStart, span.LocalStart + end - span.Start, span.Cycle, span.Transition, span.State,
                        span.PlaybackCompleted && end == span.End));
                }
                TimelineAssetPreviewTraversal current = traversals[^1];
                LocalTime = current.LocalEnd.AsSingle;
                SampledSource = current.Source;
                var target = new TimelineAssetPreviewTarget(current.Source, current.LocalEnd,
                    current.Cycle, ++m_Revision, Preferences, cursor, traversals, Revision);
                m_Instance.BeginTarget(in target);
                PreparedTimeline sampled = m_Contents[current.Source];
                TimelineRuntimePresentationOperations operations = sampled.Sampling.SampleAt(target.Time, target.Cycle,
                    current.PlaybackCompleted ? TimelinePresentationSampleReason.Completed : TimelinePresentationSampleReason.Correction);
                foreach (ITimelineDomainPreview domain in sampled.Domains.Domains)
                    domain.Sample(in target, in operations);
                m_Instance.CompleteTarget(in target);
                SampledRevision = target.Revision;
                m_Prefetching = true;
                Error = string.Empty;
                SceneView.RepaintAll();
                Changed?.Invoke();
            }
            catch (TimelineAssetPreviewInputUnavailableException exception)
            {
                m_Instance.AbortTarget();
                IsPlaying = false;
                m_SampleRequested = false;
                Error = exception.Message;
                SceneView.RepaintAll();
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                Close();
                Error = exception.Message;
                Changed?.Invoke();
            }
        }

        PreparedTimeline Prepare(TimelineAsset source, bool discover = false)
        {
            string fingerprint = TimelineAuthoringFingerprint.Compute(source.Data);
            m_Contents.TryGetValue(source, out PreparedTimeline previous);
            if (previous != null && !discover)
                return previous;
            TimelineContentDiscoveryResult discovery = TimelineContentDiscovery.Discover(source, m_Provider.Contracts);
            if (!discovery.IsValid)
                throw new InvalidOperationException(string.Join("\n", discovery.Errors));
            if (previous != null && previous.Content.Content.ContentHash == discovery.Content.ContentHash)
            {
                previous.Fingerprint = fingerprint;
                return previous;
            }
            TimelineAssetPreviewContent domains = m_Instance.PrepareContent(source, discovery.Content);
            TimelineRuntimePreparedContent prepared;
            try
            {
                prepared = TimelineRuntimePreparation.PrepareContent(discovery.Content,
                    m_Provider.NumericTarget, domains.Inputs,
                    domains.Bindings, domains.Dependencies, discovery.Identities);
                if (!prepared.IsReady)
                    throw new InvalidOperationException(string.Join("\n", prepared.Errors));
                foreach (ITimelineDomainPreview domain in domains.Domains)
                    domain.Prepare(prepared);
            }
            catch
            {
                foreach (ITimelineDomainPreview domain in domains.Domains)
                    domain.Dispose();
                throw;
            }
            Action changed = () => m_Dirty.Add(source);
            if (previous != null)
            {
                source.Data.OnValueChanged -= previous.Changed;
                foreach (ITimelineDomainPreview domain in previous.Domains.Domains)
                {
                    domain.Stop();
                    domain.Dispose();
                }
            }
            var result = new PreparedTimeline
            {
                Source = source, Fingerprint = fingerprint, Changed = changed, Domains = domains,
                Content = prepared, Sampling = new TimelineRuntimePresentationTargetSampling(prepared)
            };
            m_Contents[source] = result;
            source.Data.OnValueChanged += changed;
            return result;
        }

        void BuildChain()
        {
            m_ChainRequested = false;
            if (!float.IsFinite(Preferences.RangeEnd) || Preferences.RangeEnd <= 0f)
                throw new TimelineAssetPreviewInputUnavailableException("预览范围终点必须是大于零的有限秒数。");
            m_Traversals.Clear();
            m_Assumptions.Clear();
            // 编排执行的容量覆盖整份程序，同一状态机里每个 Timeline 都要先准备。
            if (m_Query != null)
                foreach (OrchestrationStaticState state in m_Query.Query.States)
                    if (state.Timeline != null)
                        Prepare(m_Query.ResolveTimeline(state.TimelineIdentity));
            if (m_Query == null || m_EntryState == null ||
                m_Query.ResolveTimeline(m_EntryState.TimelineIdentity) != Source)
            {
                PreparedTimeline source = Prepare(Source);
                BuildStandalone(source);
                Chain = null;
            }
            else
            {
                var inputs = new TimelineAssetPreviewReadIntervals(Preferences, Inputs, InputRequests);
                Chain = TimelineStaticChain.Build(m_Query.Query, m_EntryState,
                    OrchestrationTime.FromSeconds(Preferences.RangeEnd), inputs);
                OrchestrationStaticTransition entry = null;
                foreach (TimelineStaticChainSpan span in Chain.Spans)
                {
                    TimelineAsset source = m_Query.ResolveTimeline(span.State.TimelineIdentity);
                    PreparedTimeline content = Prepare(source);
                    m_Traversals.Add(new TimelineAssetPreviewTraversal(source, content.Content,
                        span.Start, span.End, span.LocalStart, span.LocalEnd, span.Cycle, entry, span.State, span.PlaybackCompleted));
                    entry = null;
                    foreach (TimelineStaticChainMarker marker in Chain.Markers)
                        if (marker.Time == span.End)
                            entry = marker.Transition;
                }
                foreach (OrchestrationStaticState state in m_Query.Query.States)
                {
                    foreach (OrchestrationStaticTransition transition in state.Transitions)
                        CollectAssumptions(state.Timeline, transition.Reads);
                    if (state.Timeline != null)
                        foreach (OrchestrationStaticClip clip in state.Timeline.Clips)
                            CollectAssumptions(state.Timeline, clip.Reads);
                }
            }
            Time = Mathf.Min(Time, Duration);
            m_SampleRequested = true;
            Revision++;
            Changed?.Invoke();
        }

        void BuildStandalone(PreparedTimeline prepared)
        {
            TimelineContentUnit content = prepared.Content.Content;
            if (content.Loop && content.Duration <= OrchestrationTime.Zero)
                throw new InvalidOperationException("循环资产预览的 Timeline 时长必须大于零。");
            OrchestrationTime limit = OrchestrationTime.FromSeconds(Preferences.RangeEnd);
            OrchestrationTime global = OrchestrationTime.Zero;
            int cycle = 0;
            do
            {
                OrchestrationTime end = OrchestrationTime.Min(global + content.Duration, limit);
                OrchestrationTime localEnd = end - global;
                m_Traversals.Add(new TimelineAssetPreviewTraversal(Source, prepared.Content,
                    global, end, OrchestrationTime.Zero, localEnd, cycle, null, null,
                    !content.Loop && localEnd == content.Duration));
                global = end;
                if (!content.Loop)
                    break;
                cycle++;
            } while (global < limit);
        }

        void CollectAssumptions(OrchestrationStaticTimeline timeline, IReadOnlyList<OrchestrationStaticRead> reads)
        {
            foreach (OrchestrationStaticRead value in reads)
            {
                OrchestrationStaticReadSource source = timeline == null ? value.Source : OrchestrationStaticReads.EffectiveSource(timeline, in value);
                if (source == OrchestrationStaticReadSource.RuntimeData && !m_Assumptions.Contains(value))
                    m_Assumptions.Add(value);
            }
        }

        // 真实档：执行记录变化时按状态与播放重建经过区间，游标跟随会话。
        void UpdateReal()
        {
            if (!m_Provider.CanObserveReal)
                return;
            RuntimeDebugSession session = RuntimeDebugSession.Shared;
            if (m_RealVersion != session.CaptureVersion || m_RealProjection != session.ProjectionVersion ||
                m_RealCapture != session.CaptureId)
            {
                RuntimeExecutionTimeline timeline = session.BuildExecutionTimeline();
                if (timeline != null && m_Real.Apply(timeline))
                {
                    m_RealVersion = session.CaptureVersion;
                    m_RealProjection = session.ProjectionVersion;
                    m_RealCapture = session.CaptureId;
                    RebuildRealTraversals();
                    Compare();
                    Revision++;
                    Changed?.Invoke();
                }
            }
            float time = m_Provider.RealTime;
            if (time == Time)
                return;
            Time = time;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(time);
            foreach (TimelineAssetPreviewTraversal traversal in m_Traversals)
                if (traversal.Start <= cursor && cursor <= traversal.End)
                {
                    SampledSource = traversal.Source;
                    LocalTime = (traversal.LocalStart + cursor - traversal.Start).AsSingle;
                }
            Changed?.Invoke();
        }

        void RebuildRealTraversals()
        {
            m_Traversals.Clear();
            foreach (BtsmtlExecutionTimeBarDataSource.RecordedPlayback played in m_Real.Recorded)
            {
                if (!played.HasLocal ||
                    !m_Provider.RealAuthoring.TryResolveTimeline(played.TimelineAuthoringId, out TimelineAsset source, out _))
                    continue;
                OrchestrationStaticState state = null;
                if (m_Query != null)
                    foreach (OrchestrationStaticState candidate in m_Query.Query.States)
                        if (candidate.Location.GraphId == played.StateMachineGraphId && candidate.Location.NodeId == played.StateId)
                            state = candidate;
                m_Traversals.Add(new TimelineAssetPreviewTraversal(source, null,
                    OrchestrationTime.FromSeconds(played.Start), OrchestrationTime.FromSeconds(played.End),
                    OrchestrationTime.FromSeconds(played.LocalStart), OrchestrationTime.FromSeconds(played.LocalEnd),
                    played.Cycle, null, state, false));
            }
        }

        // 按发生顺序配对真实转移与快速推导的转移：目标不同、未发生或时刻偏差超过一个逻辑 Tick 时记一处差异；
        // 目标一旦不同后续路线已分叉，不再继续配对。
        void Compare()
        {
            m_Differences.Clear();
            if (FastChain == null)
                return;
            var real = new List<(OrchestrationTime Time, OrchestrationStaticState From, OrchestrationStaticState To)>();
            for (int i = 0; i + 1 < m_Traversals.Count; i++)
                if (m_Traversals[i + 1].State != m_Traversals[i].State)
                    real.Add((m_Traversals[i].End, m_Traversals[i].State, m_Traversals[i + 1].State));
            double tick = 1d / Math.Max(1, InputRequests.Count != 0 ? InputRequests[0].TickRate : TimelineUtility.FrameRate);
            IReadOnlyList<TimelineStaticChainMarker> fast = FastChain.Markers;
            int count = Math.Max(real.Count, fast.Count);
            for (int k = 0; k < count; k++)
            {
                if (k >= fast.Count)
                {
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time.AsSingle, real[k].From,
                        $"快速推导没有这次转移：真实在第 {real[k].Time.AsSingle:0.000} 秒从 {Describe(real[k].From)} 转到 {Describe(real[k].To)}。"));
                    break;
                }
                TimelineStaticChainMarker marker = fast[k];
                string basis = FastBasis(marker);
                if (k >= real.Count)
                {
                    m_Differences.Add(new TimelinePreviewDifference(marker.Time.AsSingle, marker.Source,
                        $"真实重演没有发生：快速推导在第 {marker.Time.AsSingle:0.000} 秒 {Describe(marker.Source)} → " +
                        $"{Describe(marker.Transition.Target)}。{basis}"));
                    break;
                }
                if (real[k].From != marker.Source || real[k].To != marker.Transition.Target)
                {
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time.AsSingle, real[k].From,
                        $"目标不同：真实 {Describe(real[k].From)} → {Describe(real[k].To)}（第 {real[k].Time.AsSingle:0.000} 秒），" +
                        $"快速 {Describe(marker.Source)} → {Describe(marker.Transition.Target)}（第 {marker.Time.AsSingle:0.000} 秒）。{basis}"));
                    break;
                }
                double offset = real[k].Time.AsSingle - marker.Time.AsSingle;
                if (Math.Abs(offset) > tick + 1e-6)
                    m_Differences.Add(new TimelinePreviewDifference(real[k].Time.AsSingle, real[k].From,
                        $"时刻偏差 {offset * 1000:+0;-0} ms：真实第 {real[k].Time.AsSingle:0.000} 秒，快速第 {marker.Time.AsSingle:0.000} 秒。{basis}"));
            }
        }

        string Describe(OrchestrationStaticState state) => m_Query == null ? state?.Name ?? "出口" : m_Query.DescribeState(state);

        static string FastBasis(TimelineStaticChainMarker marker)
        {
            OrchestrationTransitionWindow window = OrchestrationStateTransitionWindows.Build(marker.Source).Of(marker.Transition);
            return window == null ? string.Empty : $"快速依据：{window.RowLabel}，可走 {window.EdgeLabel}。";
        }

        void BuildFastChain()
        {
            FastChain = null;
            if (m_Query == null || m_EntryState == null || !float.IsFinite(Preferences.RangeEnd) || Preferences.RangeEnd <= 0f)
                return;
            var inputs = new TimelineAssetPreviewReadIntervals(Preferences, Inputs, InputRequests);
            FastChain = TimelineStaticChain.Build(m_Query.Query, m_EntryState,
                OrchestrationTime.FromSeconds(Preferences.RangeEnd), inputs);
            Compare();
        }

        TimelineAssetPreviewPreferences LoadPreferences(TimelineAsset source)
        {
            m_Schemes = TimelinePreviewInputSchemes.Load(source);
            return m_Schemes != null && m_Schemes.Schemes.Count != 0 ? m_Schemes.Current : new TimelineAssetPreviewPreferences();
        }

        public IReadOnlyList<TimelineAssetPreviewPreferences> Schemes =>
            m_Schemes != null ? m_Schemes.Schemes : new[] { Preferences };

        public string SchemesPath => Source ? TimelinePreviewInputSchemes.PathFor(Source) : string.Empty;

        // 当前方案里按键身份已不在宿主输入中的项：加载时列出，由作者删除或改键。
        public IEnumerable<TimelinePreviewInputPoint> InvalidInputs()
        {
            if (Preferences == null || !IsOpen)
                yield break;
            foreach (TimelinePreviewInputPoint point in Preferences.Inputs)
            {
                bool declared = false;
                foreach (GraphHostInputDescriptor descriptor in Inputs)
                    declared |= descriptor.InputId == point.InputId;
                if (!declared)
                    yield return point;
            }
        }

        public void SelectScheme(int index)
        {
            if (m_Schemes == null || index < 0 || index >= m_Schemes.Schemes.Count)
                return;
            m_Schemes.Selected = index;
            EditorUtility.SetDirty(m_Schemes);
            Preferences = m_Schemes.Current;
            PreferencesChanged();
        }

        public void SaveSchemeAs(string name)
        {
            TimelineAssetPreviewPreferences copy = JsonUtility.FromJson<TimelineAssetPreviewPreferences>(JsonUtility.ToJson(Preferences));
            copy.Name = name;
            Persist();
            m_Schemes.Schemes.Add(copy);
            m_Schemes.Selected = m_Schemes.Schemes.Count - 1;
            Preferences = copy;
            PreferencesChanged();
            AssetDatabase.SaveAssetIfDirty(m_Schemes);
        }

        public void RenameScheme(string name)
        {
            Preferences.Name = name;
            PreferencesChanged();
            AssetDatabase.SaveAssetIfDirty(m_Schemes);
        }

        public void DeleteScheme()
        {
            if (m_Schemes == null || m_Schemes.Schemes.Count <= 1)
                return;
            m_Schemes.Schemes.RemoveAt(Mathf.Clamp(m_Schemes.Selected, 0, m_Schemes.Schemes.Count - 1));
            m_Schemes.Selected = Mathf.Clamp(m_Schemes.Selected, 0, m_Schemes.Schemes.Count - 1);
            Preferences = m_Schemes.Current;
            PreferencesChanged();
            AssetDatabase.SaveAssetIfDirty(m_Schemes);
        }

        // 第一次改动时在作者 Timeline 旁创建方案资产，此后改动写入该资产。
        void Persist()
        {
            if (m_Schemes == null)
                m_Schemes = TimelinePreviewInputSchemes.Create(Source, Preferences);
            EditorUtility.SetDirty(m_Schemes);
        }
    }
}
