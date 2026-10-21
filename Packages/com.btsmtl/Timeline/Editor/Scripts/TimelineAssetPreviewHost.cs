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
        readonly List<TimelineAssetPreviewTraversal> m_Parallel = new();
        readonly List<TimelinePreviewRouteSegment> m_Route = new();
        OrchestrationTime m_RouteEnd;
        TimelineAsset m_SelectedSource;
        int m_SelectedOccurrence;
        readonly List<OrchestrationStaticRead> m_Assumptions = new();
        TimelineOrchestrationQuery m_Query;
        TimelinePreviewRealRoute m_Real;
        long m_RealVersion = -1;
        long m_RealProjection = -1;
        Guid m_RealCapture;
        // 预览起点：技能根状态机；-1 表示没有编排上下文，按独立 Timeline 预览。
        int m_EntryMachine = -1;
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
        // 当前入口的输入方案存储；方案的切换、另存、重命名与删除都经它完成，Host 只在改动后重新推导。
        public TimelinePreviewInputSchemeStore InputSchemes { get; } = new();
        public TimelineAssetPreviewPreferences Preferences => InputSchemes.Current;
        public ITimelineAssetPreviewInstance Instance => m_Instance;
        public TimelineContractCatalog Contracts => m_Provider.Contracts;
        public bool IsRegistered => m_Provider != null;
        public bool IsOpen => m_Stage || m_Real != null;
        public bool IsReal => m_Real != null;
        public IReadOnlyList<GraphHostInputDescriptor> Inputs =>
            m_Instance != null ? m_Instance.Inputs : m_Provider.DescribeInputs();
        // 真实档的静态推导路线，只供与执行记录对比。
        TimelineStaticChain FastChain { get; set; }
        // 真实档执行记录与快速推导的差异，按预览时刻排列；结果过期时不与新快速路线比较。
        public IReadOnlyList<TimelinePreviewDifference> Differences =>
            m_Real != null && !RealStale ? m_Real.Differences : Array.Empty<TimelinePreviewDifference>();
        // 真实档执行记录不是按最新输入方案与作者内容得到；旧记录保留显示，待原入口重演。
        public bool RealStale { get; private set; }
        public string RealStaleReason { get; private set; } = string.Empty;
        // 真实档正式选中的状态转移，按发生时刻排列。
        public IReadOnlyList<TimelinePreviewRealTransition> RealTransitions =>
            m_Real != null ? m_Real.Transitions : Array.Empty<TimelinePreviewRealTransition>();
        public TimelinePreviewRealControls CreateRealControls() => m_Provider?.CreateRealControls();
        public bool IsOpeningSession => m_SessionOpening != null;
        public bool IsPlaying { get; private set; }
        public float Time { get; private set; }
        public float Duration => m_RouteEnd.AsSingle;
        public float LocalTime { get; private set; }
        // 当前本地时刻的换算说明：停顿吸附、缺少游标记录等；为空表示按正式映射直接得到。
        public string LocalNote { get; private set; } = string.Empty;
        public TimelineAsset SampledSource { get; private set; }
        public ulong SampledRevision { get; private set; }
        public TimelineStaticChain Chain { get; private set; }
        public TimelineOrchestrationQuery Query => m_Query;
        // 两档共用的路线显示信息：快速档来自推导，真实档来自执行记录。
        public IReadOnlyList<TimelinePreviewRouteSegment> Route => m_Route;
        public string DescribeState(OrchestrationStaticState state) =>
            state == null ? "出口" : m_Query == null ? state.Name : m_Query.DescribeState(state);
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
            TimelineOrchestrationQuery query, int entryMachine)
        {
            Close();
            m_Query = query;
            m_EntryMachine = query == null ? -1 : entryMachine;
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
            InputSchemes.Load(Source, EntryIdentity());
            Error = string.Empty;
            RefreshInputs();
            if (Mode == TimelinePreviewMode.Real)
            {
                m_Real = new TimelinePreviewRealRoute(RuntimeDebugSession.Shared);
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
            source && (source == Source || m_Route.Exists(segment => segment.Source == source));

        // 在该 Timeline 的所选播放内定位：优先当前所在的那一圈，其次第一个经过该本地时刻的圈；换算经段的正式映射。
        public void SetLocalTime(TimelineAsset source, float time)
        {
            OrchestrationTime local = OrchestrationTime.FromSeconds(time);
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
            string message = IsReal ? "此播放尚无覆盖所选本地时刻的逐 Tick 游标，无法定位。"
                : "当前输入推导没有经过所选本地时刻；请调整输入／假设，或在会话中验证。";
            if (TryGetPlayback(source, out TimelinePreviewRouteSegment[] segments))
            {
                TimelinePreviewRouteSegment[] ordered = segments.Where(segment => segment.Contains(cursor))
                    .Concat(segments.Where(segment => !segment.Contains(cursor))).ToArray();
                foreach (TimelinePreviewRouteSegment segment in ordered)
                {
                    if (local < segment.LocalStart || local > segment.LocalEnd)
                        continue;
                    if (segment.TryGlobal(local, out OrchestrationTime mapped, out string reason))
                    {
                        Error = string.Empty;
                        SetTime(mapped.AsSingle);
                        return;
                    }
                    message = reason;
                }
            }
            Error = message;
            Changed?.Invoke();
        }

        // 用户在路线上选定一次播放；路线重建后按 Timeline 与第几次播放找回。
        public void SelectPlayback(int playback)
        {
            foreach (TimelinePreviewRouteSegment segment in m_Route)
                if (segment.Playback == playback)
                {
                    m_SelectedSource = segment.Source;
                    m_SelectedOccurrence = segment.Occurrence;
                    Changed?.Invoke();
                    return;
                }
        }

        // 该 Timeline 在编辑时间轴上对应的一次播放（按圈排列）：用户选定的，否则当前游标所在的，否则第一次。
        public bool TryGetPlayback(TimelineAsset source, out TimelinePreviewRouteSegment[] segments)
        {
            int playback = -1;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
            foreach (TimelinePreviewRouteSegment segment in m_Route)
            {
                if (segment.Source != source)
                    continue;
                if (source == m_SelectedSource && segment.Occurrence == m_SelectedOccurrence)
                {
                    playback = segment.Playback;
                    break;
                }
                if (playback < 0 || segment.Contains(cursor))
                    playback = segment.Playback;
            }
            segments = playback < 0 ? Array.Empty<TimelinePreviewRouteSegment>()
                : m_Route.Where(segment => segment.Playback == playback).ToArray();
            return segments.Length != 0;
        }

        public void SetOrchestrationContext(TimelineAsset source, TimelineOrchestrationQuery query, int entryMachine)
        {
            if (query == null)
                entryMachine = -1;
            // 窗口在预览链的 Timeline 间切换会重复送来同一上下文，不重新准备。
            if (source != Source || query == m_Query && entryMachine == m_EntryMachine)
                return;
            m_Query = query;
            m_EntryMachine = entryMachine;
            TimelinePreviewEntryIdentity entry = EntryIdentity();
            if (!entry.Equals(InputSchemes.Entry))
            {
                InputSchemes.Load(Source, entry);
                RefreshInputs();
            }
            foreach (TimelineAsset preparedSource in m_Contents.Keys)
                m_Dirty.Add(preparedSource);
            m_ChainRequested = true;
        }

        // 技能根提供入口技能；没有编排上下文时按独立 Timeline 归属。
        TimelinePreviewEntryIdentity EntryIdentity() =>
            new(m_EntryMachine >= 0 ? m_Query.Owner : string.Empty, Source.Data.AuthoringId);

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
                Revision++;
                Changed?.Invoke();
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
            InputSchemes.MarkChanged();
            RefreshInputs();
            ApplyInputs();
        }

        // 输入 Timeline 或其图改动后重新读出；读出结果变化时返回 true。宿主输入描述不可用时记为错误。
        bool RefreshInputs()
        {
            try
            {
                return InputSchemes.RefreshInputs(Inputs);
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        // 当前方案的输入 Timeline 读取错误；有错误时两档都不使用该输入 Timeline。
        public string InputError => string.Join("\n", Preferences.InputErrors);

        // 新读出的输入交给两档：快速档重新推导与采样，真实档替换会话输入日程并标为过期。
        void ApplyInputs()
        {
            Revision++;
            if (m_Real != null)
            {
                // 真实档：替换会话输入日程，旧录制标为过期，由原“重新播放”按新方案重演。
                Error = string.Empty;
                try
                {
                    m_Provider.ReplaceRealInputs(Preferences);
                }
                catch (Exception exception)
                {
                    Error = exception.Message;
                }
                BuildFastChain();
                RefreshRealStale();
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
            TimelinePreviewInputTimeline.Replace(InputSchemes.EnsureInputTimeline(), Contracts, Inputs, imported);
            PreferencesChanged();
            InputSchemes.Save();
        }

        // 编辑入口拒绝了无记录或越界的时间操作。
        public void ReportRejectedEdit(string reason)
        {
            Error = reason;
            Changed?.Invoke();
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
                RealStale = false;
                RealStaleReason = string.Empty;
                ClearRoute();
                FastChain = null;
                SampledSource = null;
                Changed?.Invoke();
            }
            m_SessionOpening?.Cancel();
            m_SessionOpening?.Dispose();
            m_SessionOpening = null;
            m_PendingSource = null;
            m_SelectedSource = null;
            IsPlaying = false;
            InputSchemes.Save();
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
            m_Parallel.Clear();
            ClearRoute();
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
            if (RefreshInputs())
                ApplyInputs();
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
                        TimelineAssetPreviewTarget horizon = Horizon(m_Revision);
                        m_Prefetching = m_Instance.Prefetch(in horizon, PrefetchSeconds);
                    }
                    return;
                }
                m_SampleRequested = false;
                if (m_Traversals.Count == 0)
                    return;
                // 截到游标的整组播放：主链与全部并行分支一起交给实例与领域。
                OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
                TimelineAssetPreviewTarget target = Horizon(++m_Revision).Until(cursor);
                TimelineAssetPreviewTraversal current = target.Traversals[^1];
                LocalTime = current.LocalEnd.AsSingle;
                SampledSource = current.Source;
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

        // 主链终点处的整条路线目标。
        TimelineAssetPreviewTarget Horizon(ulong revision)
        {
            TimelineAssetPreviewTraversal last = m_Traversals[^1];
            return new TimelineAssetPreviewTarget(last.Source, last.LocalEnd, last.Cycle, revision, Preferences,
                last.End, m_Traversals.ToArray(), m_Parallel.ToArray(), Revision);
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
            m_Parallel.Clear();
            m_Assumptions.Clear();
            int playback = -1;
            int branch = 0;
            // 编排执行的容量覆盖整份程序，同一状态机里每个 Timeline 都要先准备。
            if (m_Query != null)
            {
                foreach (OrchestrationStaticState state in m_Query.Query.States)
                    if (state.Timeline != null)
                        Prepare(m_Query.ResolveTimeline(state.TimelineIdentity));
                foreach (OrchestrationStaticTimelineCall call in m_Query.Query.TimelineCalls)
                    if (call.Callee != null)
                        Prepare(m_Query.ResolveTimeline(call.CalleeIdentity));
            }
            if (m_EntryMachine < 0)
            {
                PreparedTimeline source = Prepare(Source);
                BuildStandalone(source);
                playback = 0;
                Chain = null;
            }
            else
            {
                var inputs = new TimelineAssetPreviewReadIntervals(Preferences, Inputs, InputRequests);
                Chain = TimelineStaticChain.Build(m_Query.Query, m_EntryMachine,
                    OrchestrationTime.FromSeconds(Preferences.RangeEnd), inputs);
                AddChain(Chain, 0, m_Traversals, ref playback);
                AddBranches(Chain, ref branch, ref playback);
                foreach (OrchestrationStaticState state in m_Query.Query.States)
                {
                    foreach (OrchestrationStaticTransition transition in state.Transitions)
                        CollectAssumptions(state.Timeline, transition.Reads);
                    if (state.Timeline != null)
                        foreach (OrchestrationStaticClip clip in state.Timeline.Clips)
                            CollectAssumptions(state.Timeline, clip.Reads);
                }
            }
            if (m_Query != null)
            {
                foreach (IGrouping<int, TimelineAssetPreviewTraversal> caller in m_Traversals.Concat(m_Parallel)
                             .GroupBy(value => value.Playback).ToArray())
                    AddCalls(caller.ToArray(), new HashSet<string>(StringComparer.Ordinal), ref branch, ref playback);
            }
            RebuildFastRoute();
            Time = Mathf.Min(Time, Duration);
            m_SampleRequested = true;
            Revision++;
            Changed?.Invoke();
        }

        // 一条链的各段：经转移进入或换 Timeline 时开始一次新播放，其余为同一次播放按圈拆开。
        void AddChain(TimelineStaticChain chain, int branch, List<TimelineAssetPreviewTraversal> result, ref int playback)
        {
            OrchestrationStaticTransition entry = null;
            TimelineAsset previous = null;
            foreach (TimelineStaticChainSpan span in chain.Spans)
            {
                TimelineAsset source = m_Query.ResolveTimeline(span.State.TimelineIdentity);
                PreparedTimeline content = Prepare(source);
                if (previous == null || entry != null || source != previous)
                    playback++;
                result.Add(new TimelineAssetPreviewTraversal(source, content.Content, span.Start, span.End,
                    span.LocalStart, span.LocalEnd, span.Cycle, entry, span.State, span.PlaybackCompleted, branch, playback));
                previous = source;
                entry = null;
                foreach (TimelineStaticChainMarker marker in chain.Markers)
                    if (marker.Time == span.End)
                        entry = marker.Transition;
            }
        }

        // 状态 Root 中并行的状态机（含嵌套）各为一个分支，与主链共用播放编号。
        void AddBranches(TimelineStaticChain chain, ref int branch, ref int playback)
        {
            foreach (TimelineStaticChainBranch value in chain.Branches)
            {
                AddChain(value.Chain, ++branch, m_Parallel, ref playback);
                AddBranches(value.Chain, ref branch, ref playback);
            }
        }

        // Clip 与 Marker 调用拉起的嵌套播放：调用方该次播放的每一圈经过调用时刻就起播一次，与调用方共用预览时间，
        // 各为一个分支交给领域与主链合并，寿命受调用图执行帧与父播放共同限制。
        void AddCalls(TimelineAssetPreviewTraversal[] caller, HashSet<string> chain, ref int branch, ref int playback)
        {
            TimelineAssetPreviewTraversal first = caller[0];
            OrchestrationStaticTimeline timeline = first.Call != null ? first.Call.Callee : first.State?.Timeline;
            if (timeline == null)
                return;
            int operation = first.Call != null ? first.Call.OperationIndex : first.State.TimelineOperationIndex;
            OrchestrationTime playbackEnd = OrchestrationTime.Zero;
            foreach (TimelineAssetPreviewTraversal span in caller)
                playbackEnd = OrchestrationTime.Max(playbackEnd, span.End);
            chain.Add(timeline.Identity);
            foreach (OrchestrationStaticTimelineCall call in timeline.Calls)
            {
                if (call.CallerOperationIndex != operation || !call.HasStaticOffset || call.Callee == null ||
                    chain.Contains(call.CalleeIdentity))
                    continue;
                foreach (TimelineAssetPreviewTraversal span in caller)
                {
                    if (call.Offset < span.LocalStart || call.Offset > span.LocalEnd ||
                        call.Offset == span.LocalEnd && !span.PlaybackCompleted)
                        continue;
                    OrchestrationTime start = span.Start + call.Offset - span.LocalStart;
                    OrchestrationTime end = OrchestrationTime.Min(playbackEnd, OrchestrationTime.FromSeconds(Preferences.RangeEnd));
                    if (call.Limit is { } limit)
                        end = OrchestrationTime.Min(end, span.Start + limit - span.LocalStart);
                    if (end <= start)
                        continue;
                    TimelineAsset source = m_Query.ResolveTimeline(call.CalleeIdentity);
                    PreparedTimeline content = Prepare(source);
                    int nestedPlayback = ++playback;
                    int nestedBranch = ++branch;
                    OrchestrationTime duration = call.Callee.Duration;
                    OrchestrationTime local = call.CalleeStartTime;
                    OrchestrationTime time = start;
                    int cycle = 0;
                    var nested = new List<TimelineAssetPreviewTraversal>();
                    while (time < end)
                    {
                        if (local >= duration)
                        {
                            if (call.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                            {
                                nested.Add(new TimelineAssetPreviewTraversal(source, content.Content, time, end,
                                    duration, duration, cycle, null, null, false, nestedBranch, nestedPlayback, call,
                                    span.Playback));
                                break;
                            }
                            if (call.PlaybackMode != OrchestrationTimelinePlaybackMode.Loop || duration <= OrchestrationTime.Zero)
                                break;
                            local = OrchestrationTime.Zero;
                            cycle++;
                        }
                        OrchestrationTime spanEnd = OrchestrationTime.Min(end, time + duration - local);
                        bool completed = call.PlaybackMode == OrchestrationTimelinePlaybackMode.Once &&
                            spanEnd == time + duration - local;
                        nested.Add(new TimelineAssetPreviewTraversal(source, content.Content, time, spanEnd, local,
                            local + spanEnd - time, cycle, null, null, completed, nestedBranch, nestedPlayback, call,
                            span.Playback));
                        local += spanEnd - time;
                        time = spanEnd;
                        if (completed)
                            break;
                    }
                    if (nested.Count == 0)
                        continue;
                    m_Parallel.AddRange(nested);
                    AddCalls(nested.ToArray(), chain, ref branch, ref playback);
                }
            }
            chain.Remove(timeline.Identity);
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
                    !content.Loop && localEnd == content.Duration, 0, 0));
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

        // 真实档：执行记录变化时重建真实路线并与快速推导对比，游标跟随会话。
        void UpdateReal()
        {
            if (!m_Provider.CanObserveReal)
                return;
            bool stale = RealStale;
            if (RefreshRealStale() != stale)
            {
                Revision++;
                Changed?.Invoke();
            }
            RuntimeDebugSession session = RuntimeDebugSession.Shared;
            if (m_RealVersion != session.CaptureVersion || m_RealProjection != session.ProjectionVersion ||
                m_RealCapture != session.CaptureId)
            {
                RuntimeExecutionTimeline timeline = session.BuildExecutionTimeline();
                if (timeline != null)
                {
                    m_Real.Apply(timeline, m_Query, m_Provider);
                    m_RealVersion = session.CaptureVersion;
                    m_RealProjection = session.ProjectionVersion;
                    m_RealCapture = session.CaptureId;
                    RebuildRealRoute();
                    CompareReal();
                    Revision++;
                    Changed?.Invoke();
                }
            }
            float time = m_Provider.RealTime;
            if (time == Time)
                return;
            Time = time;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(time);
            // 本地时刻读该次播放的正式游标；所选播放在此刻时优先，其余取最后开始的一段。
            // 嵌套播放与调用方同时进行，只有被选定时才代替调用方作为当前播放。
            TimelinePreviewRouteSegment? at = null;
            foreach (TimelinePreviewRouteSegment segment in m_Route)
            {
                bool selected = segment.Source == m_SelectedSource && segment.Occurrence == m_SelectedOccurrence;
                if (segment.Contains(cursor) && (!segment.IsNested || selected) && (at == null ||
                    at.Value.Source != m_SelectedSource || at.Value.Occurrence != m_SelectedOccurrence))
                    at = segment;
            }
            if (at is { } current)
            {
                SampledSource = current.Source;
                if (current.TryLocal(cursor, out OrchestrationTime local, out string note))
                    LocalTime = local.AsSingle;
                LocalNote = note ?? string.Empty;
            }
            else
                LocalNote = "此刻不在任何有记录的播放内。";
            Changed?.Invoke();
        }

        void ClearRoute()
        {
            m_Route.Clear();
            m_RouteEnd = OrchestrationTime.Zero;
        }

        // 新一次播放的序号与它在同一 Timeline 的第几次播放。
        static int Occurrence(Dictionary<TimelineAsset, int> occurrences, TimelineAsset source)
        {
            occurrences.TryGetValue(source, out int occurrence);
            occurrences[source] = occurrence + 1;
            return occurrence;
        }

        void AddRoute(in TimelinePreviewRouteSegment segment)
        {
            m_Route.Add(segment);
            m_RouteEnd = OrchestrationTime.Max(m_RouteEnd, segment.End);
        }

        // 快速档：主链与并行分支的全部播放，与采样使用同一组区间；第几次播放按开始时刻计。
        void RebuildFastRoute()
        {
            ClearRoute();
            var occurrences = new Dictionary<TimelineAsset, int>();
            var playbacks = new Dictionary<int, int>();
            foreach (TimelineAssetPreviewTraversal traversal in m_Traversals.Concat(m_Parallel)
                         .OrderBy(value => value.Start).ThenBy(value => value.Branch))
            {
                if (!playbacks.TryGetValue(traversal.Playback, out int occurrence))
                    playbacks.Add(traversal.Playback, occurrence = Occurrence(occurrences, traversal.Source));
                AddRoute(new TimelinePreviewRouteSegment(traversal.Source, traversal.State, traversal.Start,
                    traversal.End, traversal.LocalStart, traversal.LocalEnd, traversal.Cycle, traversal.Playback,
                    occurrence, traversal.PlaybackCompleted, null, traversal.CallerPlayback,
                    traversal.CallerPlayback >= 0 ? traversal.Call?.CallerElementId : null, traversal.Call));
            }
        }

        // 真实档：同一状态激活下的同一播放实例为一次播放；段带执行记录，换算读正式游标。
        // 嵌套播放的调用方按记录的调用方身份找到其播放编号，调用方没有记录时为 -1。
        void RebuildRealRoute()
        {
            ClearRoute();
            var playbacks = new Dictionary<(BtsmtlExecutionStateIdentity, ulong), (int Playback, int Occurrence)>();
            var occurrences = new Dictionary<TimelineAsset, int>();
            foreach (TimelinePreviewRealSegment segment in m_Real.Segments)
                if (!playbacks.ContainsKey((segment.Record.Owner, segment.Record.PlaybackId)))
                    playbacks.Add((segment.Record.Owner, segment.Record.PlaybackId),
                        (playbacks.Count, Occurrence(occurrences, segment.Source)));
            foreach (TimelinePreviewRealSegment segment in m_Real.Segments)
            {
                BtsmtlExecutionRecord.RecordedPlayback played = segment.Record;
                var playback = playbacks[(played.Owner, played.PlaybackId)];
                int caller = segment.Caller == null ? -1
                    : playbacks[(segment.Caller.Record.Owner, segment.Caller.Record.PlaybackId)].Playback;
                AddRoute(new TimelinePreviewRouteSegment(segment.Source, segment.State,
                    OrchestrationTime.FromSeconds(played.Start), OrchestrationTime.FromSeconds(played.End),
                    OrchestrationTime.FromSeconds(played.LocalStart), OrchestrationTime.FromSeconds(played.LocalEnd),
                    played.Cycle, playback.Playback, playback.Occurrence, false, played, caller,
                    played.IsNested ? played.CallerClipId : null));
            }
        }

        // 只比较同输入、同内容得到的结果；过期时清空差异，最新完整记录到达后随记录重建恢复。
        void CompareReal() => m_Real?.Compare(RealStale ? null : FastChain,
            1d / Math.Max(1, InputRequests.Count != 0 ? InputRequests[0].TickRate : TimelineUtility.FrameRate), DescribeState);

        bool RefreshRealStale()
        {
            bool stale = !m_Provider.IsRealResultCurrent(out string reason);
            RealStaleReason = stale ? reason : string.Empty;
            if (stale == RealStale)
                return stale;
            RealStale = stale;
            CompareReal();
            return stale;
        }

        void BuildFastChain()
        {
            FastChain = null;
            if (m_EntryMachine >= 0 && float.IsFinite(Preferences.RangeEnd) && Preferences.RangeEnd > 0f)
                FastChain = TimelineStaticChain.Build(m_Query.Query, m_EntryMachine,
                    OrchestrationTime.FromSeconds(Preferences.RangeEnd),
                    new TimelineAssetPreviewReadIntervals(Preferences, Inputs, InputRequests));
            CompareReal();
        }
    }
}
