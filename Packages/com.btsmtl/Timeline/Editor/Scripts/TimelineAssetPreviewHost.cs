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
    // 预览生命周期：等待在条件解除后自动继续；出错保留路线，Scene 部分待重试。
    public enum TimelinePreviewLifecycle : byte { Closed, Preparing, Waiting, Ready, Failed }

    [InitializeOnLoad]
    public sealed class TimelineAssetPreviewHost
    {
        internal const string MissingProviderReason = "当前项目尚未注册 Timeline 预览宿主。";
        public static TimelineAssetPreviewHost Shared { get; } = new();
        static readonly Unity.Profiling.ProfilerMarker s_BuildChainMarker = new("TimelinePreview.BuildChain");
        static readonly Unity.Profiling.ProfilerMarker s_PrepareContentMarker = new("TimelinePreview.PrepareContent");
        static readonly Unity.Profiling.ProfilerMarker s_DiscoverContentMarker = new("TimelinePreview.Content.Discover");
        static readonly Unity.Profiling.ProfilerMarker s_CreateDomainsMarker = new("TimelinePreview.Content.CreateDomains");
        static readonly Unity.Profiling.ProfilerMarker s_PrepareBindingsMarker = new("TimelinePreview.Content.PrepareBindings");
        static readonly Unity.Profiling.ProfilerMarker s_PrepareDomainsMarker = new("TimelinePreview.Content.PrepareDomains");
        static readonly Unity.Profiling.ProfilerMarker s_ReleaseDomainsMarker = new("TimelinePreview.Content.ReleaseDomains");
        static readonly Unity.Profiling.ProfilerMarker s_OpenSceneMarker = new("TimelinePreview.OpenScene");
        static readonly Unity.Profiling.ProfilerMarker s_EnterStageMarker = new("TimelinePreview.EnterStage");
        static readonly Unity.Profiling.ProfilerMarker s_CreateInstanceMarker = new("TimelinePreview.CreateInstance");

        ITimelineAssetPreviewProvider m_Provider;
        ITimelineAssetPreviewInstance m_Instance;
        TimelineAssetPreviewStage m_Stage;
        sealed class PreparedTimeline
        {
            internal TimelineAsset Source;
            internal string Fingerprint;
            internal TimelineAssetPreviewContent Domains;
            internal TimelineRuntimePreparedContent Content;
            internal TimelineRuntimePresentationTargetSampling Sampling;
        }

        readonly Dictionary<TimelineAsset, PreparedTimeline> m_Contents = new();
        readonly HashSet<TimelineAsset> m_Dirty = new();
        // 路线经过的 Timeline 的作者改动通知；不显示 Scene 时也据此重新推导路线。
        readonly Dictionary<TimelineAsset, Action> m_Watched = new();
        bool m_Active;
        bool m_ReleasingStage;
        bool m_PendingScene;
        const double PrefetchSeconds = 0.008;
        bool m_Prefetching;
        readonly List<TimelineAssetPreviewTraversal> m_Traversals = new();
        readonly List<TimelineAssetPreviewTraversal> m_Parallel = new();
        readonly List<TimelinePreviewRouteSegment> m_Route = new();
        OrchestrationTime m_RouteEnd;
        readonly List<OrchestrationStaticRead> m_Conditions = new();
        readonly List<TimelinePreviewConditionSuggestion> m_Suggestions = new();
        TimelineOrchestrationQuery m_Query;
        TimelinePreviewRealRoute m_Real;
        long m_RealVersion = -1;
        long m_RealProjection = -1;
        Guid m_RealCapture;
        // 预览起点：根的入口状态机；-1 表示独立 Timeline 根。与 m_Query 均取自 Root。
        int m_EntryMachine = -1;
        TimelinePreviewRoot m_PendingRoot;
        TimelinePreviewMode m_PendingMode;
        CancellationTokenSource m_SessionOpening;
        bool m_ChainRequested;
        bool m_Opening;
        bool m_SampleRequested;
        double m_LastUpdate;
        ulong m_Revision;
        // 上一条交给领域的链：新链与它相同的前段，领域已推进的状态可以直接沿用。
        long m_GivenChain = -1;
        TimelineAssetPreviewTraversal[] m_GivenTraversals = Array.Empty<TimelineAssetPreviewTraversal>();
        TimelineAssetPreviewTraversal[] m_GivenParallel = Array.Empty<TimelineAssetPreviewTraversal>();
        List<TimelinePreviewInputPoint> m_GivenInputs;
        List<TimelinePreviewConditionPoint> m_GivenConditions;
        string m_GivenSettings;
        long m_ChainBase = -1;
        OrchestrationTime m_ChainBaseUntil;
        enum TargetStage : byte { None, Beginning, Completing, Sampling }
        TargetStage m_TargetStep;
        int m_SampledDomain;
        TimelineAssetPreviewTarget m_PendingTarget;
        const double StepSeconds = 0.012d;

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
        // 预览身份；窗口显示路线上的哪个 Timeline 不改变它。
        public TimelinePreviewRoot Root { get; private set; }
        // 根的入口 Timeline（独立根即该 Timeline 本身）。
        public TimelineAsset Source { get; private set; }
        // 当前根的输入方案存储；方案的切换、另存、重命名与删除都经它完成，Host 只在改动后重新推导。
        public TimelinePreviewInputSchemeStore InputSchemes { get; } = new();
        public TimelineAssetPreviewPreferences Preferences => InputSchemes.Current;
        public ITimelineAssetPreviewInstance Instance => m_Instance;
        public TimelineContractCatalog Contracts => m_Provider.Contracts;
        public bool IsRegistered => m_Provider != null;
        // 打开即有路线与推导；Scene 角色只在 ShowsScene 时存在。
        public bool IsOpen => m_Active;
        public bool ShowsScene { get; private set; }
        public TimelinePreviewLifecycle Lifecycle { get; private set; }
        // 一句中文结论；原始异常文本只在 LifecycleDetail。
        public string LifecycleReason { get; private set; } = string.Empty;
        public string LifecycleDetail { get; private set; } = string.Empty;
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
        // 真实执行离开本根的时刻；此后超出快速档范围。
        public double? RealLeftRootAt => m_Real?.LeftRootAt;
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
        // 路线经过的状态读到的运行数据条件，按含义键去重；取值来自输入 Timeline 上的"预览条件"，没写过取默认值。
        public IReadOnlyList<OrchestrationStaticRead> Conditions => m_Conditions;
        // 路线上没走成、且读到未全程成立条件的出边。
        public IReadOnlyList<TimelinePreviewConditionSuggestion> ConditionSuggestions => m_Suggestions;

        // 在输入 Timeline 上生成条件 Clip，经宿主的正式写法与 Undo；改动随后照常重新读取与推导。
        public void AddConditionClip(OrchestrationStaticRead read, bool satisfied, OrchestrationTime start, OrchestrationTime end)
        {
            TimelineAsset inputs = InputSchemes.EnsureInputTimeline();
            m_Provider.AddConditionClip(inputs, read.Key, read.Label, satisfied, start.AsSingle,
                Math.Min(end.AsSingle, Preferences.RangeEnd));
            InputSchemes.Save();
        }
        public IReadOnlyList<TimelinePreviewInputRequest> InputRequests => m_Provider.DescribeInputRequests();
        public bool TryDescribeInputGraph(ScriptableObject graph, out string name, out IReadOnlyList<string> inputIds) =>
            m_Provider.TryDescribeInputGraph(graph, out name, out inputIds);
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
                m_Provider.Configuration = value;
                if (m_Active)
                    Retry();
                Changed?.Invoke();
            }
        }

        public void Register(ITimelineAssetPreviewProvider provider)
        {
            m_Provider = provider;
            provider.SessionChanged += ContinueOpen;
            Changed?.Invoke();
        }

        public void Open(TimelinePreviewRoot root, TimelinePreviewMode mode, bool showScene)
        {
            Close();
            Error = string.Empty;
            if (m_Provider == null)
            {
                SetLifecycle(TimelinePreviewLifecycle.Failed, MissingProviderReason, string.Empty);
                return;
            }
            if (!root.Entry)
            {
                SetLifecycle(TimelinePreviewLifecycle.Failed, $"“{root.Label}”的起始状态没有 Timeline，无法预览。", string.Empty);
                return;
            }
            m_PendingRoot = root;
            m_PendingMode = mode;
            m_PendingScene = showScene;
            ContinueOpen();
        }

        // 按当前根、档位与 Scene 开关重新准备一次。
        public void Retry()
        {
            if (Root != null)
                Open(Root, Mode, ShowsScene);
        }

        void ContinueOpen()
        {
            if (m_PendingRoot == null || m_Opening)
                return;
            m_Opening = true;
            bool closed;
            string reason;
            try { closed = m_Provider.TryCloseSession(out reason); }
            finally { m_Opening = false; }
            if (!closed)
            {
                SetLifecycle(TimelinePreviewLifecycle.Waiting, reason, string.Empty);
                return;
            }
            StageUtility.GoToMainStage();
            Mode = m_PendingMode;
            UseRoot(m_PendingRoot);
            ShowsScene = Mode == TimelinePreviewMode.Real || m_PendingScene;
            m_PendingRoot = null;
            m_Active = true;
            InputSchemes.Load(Root);
            Error = string.Empty;
            RefreshInputs();
            if (Mode == TimelinePreviewMode.Real)
            {
                m_Real = new TimelinePreviewRealRoute(RuntimeDebugSession.Shared);
                m_RealVersion = -1;
                m_SessionOpening = new CancellationTokenSource();
                SetLifecycle(TimelinePreviewLifecycle.Preparing, "正在准备会话…", string.Empty);
                m_Provider.OpenSession(Preferences, m_SessionOpening.Token);
                BuildFastChain();
                Changed?.Invoke();
                return;
            }
            m_ChainRequested = true;
            m_LastUpdate = EditorApplication.timeSinceStartup;
            SetLifecycle(TimelinePreviewLifecycle.Preparing, "正在准备预览…", string.Empty);
            if (ShowsScene)
                OpenScene();
            Changed?.Invoke();
        }

        void OpenScene()
        {
            using var measure = s_OpenSceneMarker.Auto();
            if (!m_Provider.Configuration)
            {
                SetLifecycle(TimelinePreviewLifecycle.Failed, "没有选择预览场景，在右侧“场景”里选择", string.Empty);
                return;
            }
            m_Stage = ScriptableObject.CreateInstance<TimelineAssetPreviewStage>();
            m_Stage.Owner = this;
            m_Stage.ScenePath = m_Provider.ScenePath;
            try
            {
                using (s_EnterStageMarker.Auto())
                    StageUtility.GoToStage(m_Stage, false);
                using (s_CreateInstanceMarker.Auto())
                    m_Instance = m_Provider.CreateInstance(m_Stage.scene, m_Provider.Configuration);
                // 路线已按无 Scene 推导过时，内容要按实例重新准备。
                m_ChainRequested = true;
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                Fail("打开预览场景失败", exception);
            }
        }

        // Scene 角色开关：关掉只释放 Stage 与实例，路线、输入组与状态机高亮保留。
        public void SetShowsScene(bool show)
        {
            if (!m_Active || m_Real != null || show == ShowsScene)
                return;
            ShowsScene = show;
            if (show)
            {
                SetLifecycle(TimelinePreviewLifecycle.Preparing, "正在准备预览…", string.Empty);
                OpenScene();
            }
            else
            {
                ReleaseScene();
                m_ChainRequested = true;
            }
            Changed?.Invoke();
        }

        void SetLifecycle(TimelinePreviewLifecycle state, string reason, string detail)
        {
            if (Lifecycle == state && LifecycleReason == reason && LifecycleDetail == detail)
                return;
            Lifecycle = state;
            LifecycleReason = reason;
            LifecycleDetail = detail;
            Changed?.Invoke();
        }

        // 出错只释放 Scene 部分并停在出错，路线与输入组保留；原始异常进悬停说明。
        void Fail(string reason, Exception exception)
        {
            IsPlaying = false;
            m_TargetStep = TargetStage.None;
            m_SampleRequested = false;
            ReleaseScene();
            SetLifecycle(TimelinePreviewLifecycle.Failed, reason, exception.Message);
        }

        // 本宿主主动释放：先释放实例并解除 Stage 归属，再回主 Stage；Stage 迟到的关闭回调不再被当成作者离开。
        void ReleaseScene()
        {
            TimelineAssetPreviewStage stage = m_Stage;
            m_ReleasingStage = true;
            try
            {
                if (stage || m_Instance != null)
                    ReleaseStage();
            }
            finally
            {
                m_ReleasingStage = false;
            }
            if (!stage)
                return;
            stage.Owner = null;
            if (StageUtility.GetCurrentStage() == stage)
                StageUtility.GoToMainStage();
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

        // 单个 Timeline 编辑时对应的一次播放（按圈排列）：当前游标所在的，否则第一次。
        public bool TryGetPlayback(TimelineAsset source, out TimelinePreviewRouteSegment[] segments)
        {
            int playback = -1;
            OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
            foreach (TimelinePreviewRouteSegment segment in m_Route)
            {
                if (segment.Source != source)
                    continue;
                if (playback < 0 || segment.Contains(cursor))
                    playback = segment.Playback;
            }
            segments = playback < 0 ? Array.Empty<TimelinePreviewRouteSegment>()
                : m_Route.Where(segment => segment.Playback == playback).ToArray();
            return segments.Length != 0;
        }

        // 作者改动后同一根的查询重建：只换查询并续推，不重新打开；换到别的根由窗口重新打开预览。
        public void RefreshRoot(TimelinePreviewRoot root)
        {
            if (!m_Active || !root.SameAs(Root) || root.Query == m_Query && root.Entry == Source)
                return;
            if (!root.Entry)
            {
                SetLifecycle(TimelinePreviewLifecycle.Failed, $"“{root.Label}”的起始状态没有 Timeline，无法预览。", string.Empty);
                return;
            }
            UseRoot(root);
            foreach (TimelineAsset preparedSource in m_Contents.Keys)
                m_Dirty.Add(preparedSource);
            m_ChainRequested = true;
        }

        void UseRoot(TimelinePreviewRoot root)
        {
            Root = root;
            Source = root.Entry;
            m_Query = root.Query;
            m_EntryMachine = root.Machine;
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
                Revision++;
                Changed?.Invoke();
            }
        }

        void OnUndo()
        {
            if (m_Active && m_Instance == null)
                m_ChainRequested = true;
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
                return InputSchemes.RefreshInputs(m_Provider, Inputs);
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        public bool IsInputTimeline(TimelineAsset timeline) => timeline && Preferences.InputTimeline == timeline;

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
            try
            {
                m_Provider.ImportRecordedInputs(path, InputSchemes.EnsureInputTimeline());
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                Changed?.Invoke();
                return;
            }
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
            SetLifecycle(TimelinePreviewLifecycle.Failed, "会话准备失败", reason);
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
            m_PendingRoot = null;
            IsPlaying = false;
            InputSchemes.Save();
            m_Active = false;
            ReleaseScene();
            foreach (KeyValuePair<TimelineAsset, Action> watched in m_Watched)
                if (watched.Key)
                    watched.Key.Data.OnValueChanged -= watched.Value;
            m_Watched.Clear();
            m_Dirty.Clear();
            m_Traversals.Clear();
            m_Parallel.Clear();
            ClearRoute();
            m_Conditions.Clear();
            m_Suggestions.Clear();
            Chain = null;
            SampledSource = null;
            SetLifecycle(TimelinePreviewLifecycle.Closed, string.Empty, string.Empty);
            if (closeSession && !m_Provider.TryCloseSession(out string reason))
            {
                Error = reason;
                Changed?.Invoke();
            }
        }

        // 释放 Scene 部分：领域、实例与 Stage；路线与推导留给仍打开的预览。
        // 作者从 Scene 面包屑离开预览 Stage 时也经这里，按关闭 Scene 开关处理。
        internal void ReleaseStage()
        {
            foreach (PreparedTimeline timeline in m_Contents.Values)
                foreach (ITimelineDomainPreview domain in timeline.Domains.Domains)
                {
                    domain.Stop();
                    domain.Dispose();
                }
            m_Contents.Clear();
            SampledRevision = 0;
            m_SampleRequested = false;
            m_TargetStep = TargetStage.None;
            m_GivenChain = -1;
            m_GivenInputs = null;
            m_GivenConditions = null;
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
                if (m_Active && !m_ReleasingStage)
                {
                    ShowsScene = false;
                    m_ChainRequested = true;
                }
                Changed?.Invoke();
            }
        }

        void Update()
        {
            if (!IsOpen)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (Lifecycle != TimelinePreviewLifecycle.Failed)
                    SetLifecycle(TimelinePreviewLifecycle.Waiting, "脚本编译或资源导入中，完成后自动继续。", string.Empty);
                return;
            }
            if (RefreshInputs())
                ApplyInputs();
            if (m_Real != null)
            {
                if (Lifecycle != TimelinePreviewLifecycle.Failed)
                    SetLifecycle(IsOpeningSession ? TimelinePreviewLifecycle.Preparing : TimelinePreviewLifecycle.Ready,
                        IsOpeningSession ? "正在准备会话…" : string.Empty, string.Empty);
                UpdateReal();
                return;
            }
            // 编译、导入或资源等待解除后回到就绪；仍在补算的目标完成时也会回到就绪。
            if (Lifecycle == TimelinePreviewLifecycle.Waiting && m_TargetStep == TargetStage.None &&
                (m_Instance == null || m_Instance.IsReady))
                SetLifecycle(TimelinePreviewLifecycle.Ready, string.Empty, string.Empty);
            try
            {
                if (m_Dirty.Count != 0)
                {
                    TimelineAsset[] changed = m_Dirty.ToArray();
                    m_Dirty.Clear();
                    // 只有作者内容真的变了才重建链；编辑器刷新触发的同内容通知不打断预览的接续推进。
                    foreach (TimelineAsset timeline in changed)
                    {
                        if (m_Instance == null)
                        {
                            m_ChainRequested = true;
                            continue;
                        }
                        m_Contents.TryGetValue(timeline, out PreparedTimeline before);
                        if (before != null && before.Fingerprint == TimelineAuthoringFingerprint.Compute(timeline.Data))
                            continue;
                        if (Prepare(timeline, true) != before)
                            m_ChainRequested = true;
                    }
                }
                if (m_ChainRequested)
                    BuildChain();
                double now = EditorApplication.timeSinceStartup;
                if (IsPlaying)
                {
                    Time = Mathf.Min(Duration, Time + (float)(now - m_LastUpdate));
                    m_SampleRequested = true;
                    if (Time == Duration)
                        IsPlaying = false;
                    if (m_Instance == null)
                        Changed?.Invoke();
                }
                m_LastUpdate = now;
                if (m_Instance == null)
                {
                    // 不显示 Scene：只有路线与推导。
                    m_SampleRequested = false;
                    if (Lifecycle != TimelinePreviewLifecycle.Failed)
                        SetLifecycle(TimelinePreviewLifecycle.Ready, string.Empty, string.Empty);
                    return;
                }
                if (!m_Instance.IsReady)
                {
                    m_Instance.AdvancePreparation();
                    SetLifecycle(TimelinePreviewLifecycle.Waiting, "动画资源加载中，完成后自动继续。", string.Empty);
                    return;
                }
                if (m_SampleRequested && m_TargetStep != TargetStage.None)
                {
                    // 新取样请求放弃未完成的目标，Scene 回到上一完整发布。
                    m_TargetStep = TargetStage.None;
                    m_Instance.RestorePublished();
                }
                else if (m_TargetStep != TargetStage.None)
                {
                    AdvanceTarget(Deadline(StepSeconds));
                    return;
                }
                if (!m_SampleRequested)
                {
                    // 空闲时每帧花一点时间把链往后推进，之后拖到哪里都不用现算。
                    if (m_Prefetching && !IsPlaying && m_Traversals.Count != 0)
                    {
                        TimelineAssetPreviewTarget horizon = Horizon(m_Revision);
                        m_Prefetching = m_Instance.Prefetch(in horizon, Deadline(PrefetchSeconds));
                    }
                    return;
                }
                long deadline = Deadline(StepSeconds);
                m_SampleRequested = false;
                if (m_Traversals.Count == 0)
                    return;
                // 截到游标的整组播放：主链与全部并行分支一起交给实例与领域。
                OrchestrationTime cursor = OrchestrationTime.FromSeconds(Time);
                m_PendingTarget = Horizon(++m_Revision).Until(cursor);
                m_TargetStep = TargetStage.Beginning;
                m_Prefetching = false;
                AdvanceTarget(deadline);
            }
            catch (TimelineAssetPreviewInputUnavailableException exception)
            {
                m_TargetStep = TargetStage.None;
                m_Instance?.AbortTarget();
                IsPlaying = false;
                m_SampleRequested = false;
                Error = exception.Message;
                SceneView.RepaintAll();
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                string at = SampledSource ? $"在 {SampledSource.name} 本地 {LocalTime:0.000} 秒" : "准备路线时";
                Fail($"预览{at}出错，点“重试”重新准备。", exception);
            }
        }

        static long Deadline(double seconds) => System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(seconds * System.Diagnostics.Stopwatch.Frequency);

        // 目标分三步，整次 Editor 更新共用一个截止时刻：实例准备（位移积分）→ 逐帧累积的领域追到目标 → 各领域在目标时刻取样。
        // 走完才发布；没走完时让出并恢复上一完整发布，Scene、状态行与游标标注停在同一已发布时刻，窗口照常响应。
        void AdvanceTarget(long deadline)
        {
            if (m_TargetStep == TargetStage.Beginning)
            {
                if (!m_Instance.BeginTarget(in m_PendingTarget, deadline))
                {
                    YieldTarget();
                    return;
                }
                m_TargetStep = TargetStage.Completing;
            }
            if (m_TargetStep == TargetStage.Completing)
            {
                if (!m_Instance.CompleteTarget(in m_PendingTarget, deadline))
                {
                    YieldTarget();
                    return;
                }
                m_TargetStep = TargetStage.Sampling;
                m_SampledDomain = 0;
            }
            TimelineAssetPreviewTraversal current = m_PendingTarget.Traversals[^1];
            PreparedTimeline sampled = m_Contents[current.Source];
            TimelineRuntimePresentationOperations operations = sampled.Sampling.SampleAt(m_PendingTarget.Time,
                m_PendingTarget.Cycle, current.PlaybackCompleted
                    ? TimelinePresentationSampleReason.Completed : TimelinePresentationSampleReason.Correction);
            IReadOnlyList<ITimelineDomainPreview> domains = sampled.Domains.Domains;
            for (; m_SampledDomain < domains.Count; m_SampledDomain++)
                if (!domains[m_SampledDomain].Sample(in m_PendingTarget, in operations, deadline))
                {
                    YieldTarget();
                    return;
                }
            m_Instance.PublishTarget(in m_PendingTarget);
            FinishTarget(in m_PendingTarget);
        }

        void YieldTarget()
        {
            m_Instance.RestorePublished();
            if (!m_Instance.IsReady)
                SetLifecycle(TimelinePreviewLifecycle.Waiting, "动画资源加载中，完成后自动继续。", string.Empty);
            SceneView.RepaintAll();
        }

        void FinishTarget(in TimelineAssetPreviewTarget target)
        {
            m_TargetStep = TargetStage.None;
            TimelineAssetPreviewTraversal current = target.Traversals[^1];
            LocalTime = current.LocalEnd.AsSingle;
            SampledSource = current.Source;
            SampledRevision = target.Revision;
            m_Prefetching = true;
            Error = string.Empty;
            SetLifecycle(TimelinePreviewLifecycle.Ready, string.Empty, string.Empty);
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        // 主链终点处的整条路线目标。链修订变化时与上一条交给领域的链比较，记下两者相同到哪一刻。
        TimelineAssetPreviewTarget Horizon(ulong revision)
        {
            TimelineAssetPreviewTraversal last = m_Traversals[^1];
            TimelineAssetPreviewTraversal[] traversals = m_Traversals.ToArray();
            TimelineAssetPreviewTraversal[] parallel = m_Parallel.ToArray();
            if (Revision != m_GivenChain)
            {
                string settings = PreviewSettings(Preferences);
                m_ChainBase = m_GivenChain;
                m_ChainBaseUntil = m_GivenChain < 0 || settings != m_GivenSettings
                    ? OrchestrationTime.Zero
                    : OrchestrationTime.Min(OrchestrationTime.Min(
                            SameUntil(m_GivenTraversals, traversals), SameUntil(m_GivenParallel, parallel)),
                        OrchestrationTime.Min(SameUntil(m_GivenInputs, Preferences.Inputs),
                            SameUntil(m_GivenConditions, Preferences.ConditionPoints)));
                m_GivenChain = Revision;
                m_GivenTraversals = traversals;
                m_GivenParallel = parallel;
                m_GivenInputs = Preferences.Inputs;
                m_GivenConditions = Preferences.ConditionPoints;
                m_GivenSettings = settings;
            }
            return new TimelineAssetPreviewTarget(last.Source, last.LocalEnd, last.Cycle, revision, Preferences,
                last.End, traversals, parallel, Revision, m_ChainBase, m_ChainBaseUntil);
        }

        static string PreviewSettings(TimelineAssetPreviewPreferences preferences)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(preferences.HasTarget).Append('|').Append(preferences.TargetPosition).Append('|')
                .Append(preferences.TargetEuler).Append('|').Append(preferences.StartPosition).Append('|')
                .Append(preferences.StartYaw);
            return builder.ToString();
        }

        static OrchestrationTime SameUntil(List<TimelinePreviewConditionPoint> before, List<TimelinePreviewConditionPoint> after)
        {
            int count = Math.Min(before.Count, after.Count);
            for (int i = 0; i < count; i++)
            {
                TimelinePreviewConditionPoint a = before[i];
                TimelinePreviewConditionPoint b = after[i];
                if (a.Key != b.Key || a.Time != b.Time || a.Satisfied != b.Satisfied || a.RestoreDefault != b.RestoreDefault)
                    return OrchestrationTime.FromSeconds(Math.Min(a.Time, b.Time));
            }
            if (before.Count > count)
                return OrchestrationTime.FromSeconds(before[count].Time);
            return after.Count > count ? OrchestrationTime.FromSeconds(after[count].Time) : OrchestrationTime.MaxValue;
        }

        // 两组播放区间逐段相同到哪一刻：内容、起点、圈数与所属状态都相同的段只差终点时取较早终点。
        static OrchestrationTime SameUntil(TimelineAssetPreviewTraversal[] before, TimelineAssetPreviewTraversal[] after)
        {
            int count = Math.Min(before.Length, after.Length);
            for (int i = 0; i < count; i++)
            {
                TimelineAssetPreviewTraversal a = before[i];
                TimelineAssetPreviewTraversal b = after[i];
                if (a.Source != b.Source || !ReferenceEquals(a.Content, b.Content) || a.Start != b.Start ||
                    a.LocalStart != b.LocalStart || a.Cycle != b.Cycle || a.Branch != b.Branch ||
                    a.Playback != b.Playback || a.CallerPlayback != b.CallerPlayback ||
                    a.State?.Location.DisplayPath != b.State?.Location.DisplayPath ||
                    a.Transition?.Identity != b.Transition?.Identity ||
                    a.Call?.OperationIndex != b.Call?.OperationIndex)
                    return OrchestrationTime.Min(a.Start, b.Start);
                if (a.End != b.End || a.PlaybackCompleted != b.PlaybackCompleted)
                    return OrchestrationTime.Min(a.End, b.End);
            }
            if (before.Length > count)
                return before[count].Start;
            return after.Length > count ? after[count].Start : OrchestrationTime.MaxValue;
        }

        static OrchestrationTime SameUntil(List<TimelinePreviewInputPoint> before, List<TimelinePreviewInputPoint> after)
        {
            int count = Math.Min(before.Count, after.Count);
            for (int i = 0; i < count; i++)
            {
                TimelinePreviewInputPoint a = before[i];
                TimelinePreviewInputPoint b = after[i];
                if (a.InputId != b.InputId || a.Time != b.Time || a.Boolean != b.Boolean || a.Integer != b.Integer ||
                    a.Scalar != b.Scalar || a.Vector2 != b.Vector2 || a.Vector3 != b.Vector3 ||
                    a.Rotation != b.Rotation || a.Text != b.Text)
                    return OrchestrationTime.FromSeconds(Math.Min(a.Time, b.Time));
            }
            if (before.Count > count)
                return OrchestrationTime.FromSeconds(before[count].Time);
            return after.Count > count ? OrchestrationTime.FromSeconds(after[count].Time) : OrchestrationTime.MaxValue;
        }

        PreparedTimeline Prepare(TimelineAsset source, bool discover = false)
        {
            using var measure = s_PrepareContentMarker.Auto();
            string fingerprint = TimelineAuthoringFingerprint.Compute(source.Data);
            m_Contents.TryGetValue(source, out PreparedTimeline previous);
            if (previous != null && !discover)
                return previous;
            TimelineContentDiscoveryResult discovery;
            using (s_DiscoverContentMarker.Auto())
                discovery = TimelineContentDiscovery.Discover(source, m_Provider.Contracts);
            if (!discovery.IsValid)
                throw new InvalidOperationException(string.Join("\n", discovery.Errors));
            if (previous != null && previous.Content.Content.ContentHash == discovery.Content.ContentHash)
            {
                previous.Fingerprint = fingerprint;
                return previous;
            }
            TimelineAssetPreviewContent domains;
            using (s_CreateDomainsMarker.Auto())
                domains = m_Instance.PrepareContent(source, discovery.Content);
            TimelineRuntimePreparedContent prepared;
            try
            {
                using (s_PrepareBindingsMarker.Auto())
                    prepared = TimelineRuntimePreparation.PrepareContent(discovery.Content,
                        m_Provider.NumericTarget, domains.Inputs,
                        domains.Bindings, domains.Dependencies, discovery.Identities);
                if (!prepared.IsReady)
                    throw new InvalidOperationException(string.Join("\n", prepared.Errors));
                using (s_PrepareDomainsMarker.Auto())
                    foreach (ITimelineDomainPreview domain in domains.Domains)
                    {
                        using var domainMeasure = new Unity.Profiling.ProfilerMarker(
                            "TimelinePreview.Domain.Prepare." + domain.GetType().Name).Auto();
                        domain.Prepare(prepared);
                    }
            }
            catch
            {
                foreach (ITimelineDomainPreview domain in domains.Domains)
                    domain.Dispose();
                throw;
            }
            if (previous != null)
            {
                using var releaseMeasure = s_ReleaseDomainsMarker.Auto();
                foreach (ITimelineDomainPreview domain in previous.Domains.Domains)
                {
                    domain.Stop();
                    domain.Dispose();
                }
            }
            var result = new PreparedTimeline
            {
                Source = source, Fingerprint = fingerprint, Domains = domains,
                Content = prepared, Sampling = new TimelineRuntimePresentationTargetSampling(prepared)
            };
            m_Contents[source] = result;
            return result;
        }

        // 路线上的 Timeline：关注作者改动；有 Scene 时按实例准备内容，没有时只推导路线。
        TimelineRuntimePreparedContent ContentFor(TimelineAsset source)
        {
            if (!m_Watched.ContainsKey(source))
            {
                Action changed = () => m_Dirty.Add(source);
                m_Watched.Add(source, changed);
                source.Data.OnValueChanged += changed;
            }
            return m_Instance == null ? null : Prepare(source).Content;
        }

        void BuildChain()
        {
            using var measure = s_BuildChainMarker.Auto();
            m_ChainRequested = false;
            if (!float.IsFinite(Preferences.RangeEnd) || Preferences.RangeEnd <= 0f)
                throw new TimelineAssetPreviewInputUnavailableException("预览范围终点必须是大于零的有限秒数。");
            m_Traversals.Clear();
            m_Parallel.Clear();
            m_Conditions.Clear();
            m_Suggestions.Clear();
            int playback = -1;
            int branch = 0;
            // 编排执行的容量覆盖整份程序，同一状态机里每个 Timeline 都要先准备。
            if (m_Query != null)
            {
                foreach (OrchestrationStaticState state in m_Query.Query.States)
                    if (state.Timeline != null)
                        ContentFor(m_Query.ResolveTimeline(state.TimelineIdentity));
                foreach (OrchestrationStaticTimelineCall call in m_Query.Query.TimelineCalls)
                    if (call.Callee != null)
                        ContentFor(m_Query.ResolveTimeline(call.CalleeIdentity));
            }
            if (m_EntryMachine < 0)
            {
                TimelineRuntimePreparedContent source = ContentFor(Source);
                BuildStandalone(source?.Content ?? Discover(Source), source);
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
            }
            if (m_Query != null)
            {
                foreach (IGrouping<int, TimelineAssetPreviewTraversal> caller in m_Traversals.Concat(m_Parallel)
                             .GroupBy(value => value.Playback).ToArray())
                    AddCalls(caller.ToArray(), new HashSet<string>(StringComparer.Ordinal), ref branch, ref playback);
            }
            RebuildFastRoute();
            if (Chain != null)
                CollectConditions(new TimelineAssetPreviewReadIntervals(Preferences, Inputs, InputRequests));
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
                TimelineRuntimePreparedContent content = ContentFor(source);
                if (previous == null || entry != null || source != previous)
                    playback++;
                result.Add(new TimelineAssetPreviewTraversal(source, content, span.Start, span.End,
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
                if (call.CallerOperationIndex != operation || !call.StartsOnCallback || call.Callee == null ||
                    chain.Contains(call.CalleeIdentity))
                    continue;
                for (int callerIndex = 0; callerIndex < caller.Length; callerIndex++)
                {
                    TimelineAssetPreviewTraversal span = caller[callerIndex];
                    bool destroy = call.Hook == OrchestrationTimelineHook.Destroy;
                    if (destroy && callerIndex != caller.Length - 1)
                        continue;
                    OrchestrationTime offset = destroy ? span.LocalEnd : call.Offset;
                    if (call.Hook == OrchestrationTimelineHook.Disable && callerIndex == caller.Length - 1 &&
                        call.Clip.Start <= span.LocalEnd && call.Clip.End > span.LocalEnd)
                        offset = span.LocalEnd;
                    if (offset < span.LocalStart || offset > span.LocalEnd ||
                        offset == span.LocalEnd && !span.PlaybackCompleted && !call.IsInstantaneous)
                        continue;
                    OrchestrationTime start = span.Start + offset - span.LocalStart;
                    OrchestrationTime end = OrchestrationTime.Min(playbackEnd, OrchestrationTime.FromSeconds(Preferences.RangeEnd));
                    if (!call.IsInstantaneous && call.Limit is { } limit)
                        end = OrchestrationTime.Min(end, span.Start + limit - span.LocalStart);
                    if (end < start || end == start && !call.IsInstantaneous)
                        continue;
                    TimelineAsset source = m_Query.ResolveTimeline(call.CalleeIdentity);
                    TimelineRuntimePreparedContent content = ContentFor(source);
                    int nestedPlayback = ++playback;
                    int nestedBranch = ++branch;
                    OrchestrationTime duration = call.Callee.Duration;
                    OrchestrationTime local = call.CalleeStartTime;
                    OrchestrationTime time = start;
                    int cycle = 0;
                    var nested = new List<TimelineAssetPreviewTraversal>();
                    if (call.IsInstantaneous)
                        nested.Add(new TimelineAssetPreviewTraversal(source, content, start, start, local, local,
                            cycle, null, null, false, nestedBranch, nestedPlayback, call, span.Playback));
                    while (!call.IsInstantaneous && time < end)
                    {
                        if (local >= duration)
                        {
                            if (call.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                            {
                                nested.Add(new TimelineAssetPreviewTraversal(source, content, time, end,
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
                        nested.Add(new TimelineAssetPreviewTraversal(source, content, time, spanEnd, local,
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

        TimelineContentUnit Discover(TimelineAsset source)
        {
            TimelineContentDiscoveryResult discovery = TimelineContentDiscovery.Discover(source, m_Provider.Contracts);
            if (!discovery.IsValid)
                throw new InvalidOperationException(string.Join("\n", discovery.Errors));
            return discovery.Content;
        }

        void BuildStandalone(TimelineContentUnit content, TimelineRuntimePreparedContent prepared)
        {
            if (content.Loop && content.Duration <= OrchestrationTime.Zero)
                throw new InvalidOperationException("循环资产预览的 Timeline 时长必须大于零。");
            OrchestrationTime limit = OrchestrationTime.FromSeconds(Preferences.RangeEnd);
            OrchestrationTime global = OrchestrationTime.Zero;
            int cycle = 0;
            do
            {
                OrchestrationTime end = OrchestrationTime.Min(global + content.Duration, limit);
                OrchestrationTime localEnd = end - global;
                m_Traversals.Add(new TimelineAssetPreviewTraversal(Source, prepared,
                    global, end, OrchestrationTime.Zero, localEnd, cycle, null, null,
                    !content.Loop && localEnd == content.Duration, 0, 0));
                global = end;
                if (!content.Loop)
                    break;
                cycle++;
            } while (global < limit);
        }

        // 路线经过的状态出边读到的条件（按含义键去重），以及没走成的出边所缺条件：读到的运行数据在该状态播放区间内没有全程成立。
        void CollectConditions(TimelineAssetPreviewReadIntervals inputs)
        {
            m_Conditions.Clear();
            m_Suggestions.Clear();
            var taken = new HashSet<OrchestrationStaticTransition>();
            CollectTaken(Chain, taken);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var suggested = new HashSet<(OrchestrationStaticTransition, OrchestrationTime)>();
            foreach (TimelinePreviewRouteSegment segment in m_Route)
            {
                OrchestrationStaticState state = segment.State;
                if (state == null)
                    continue;
                // 只列决定走哪条转移的读取；Clip 内部读取（如 Motion Warp 目标）按默认值，不进条件菜单。
                foreach (OrchestrationStaticTransition transition in state.Transitions)
                {
                    var missing = new List<OrchestrationStaticRead>();
                    foreach (OrchestrationStaticRead read in transition.Reads)
                        if (AddCondition(state.Timeline, in read, keys) &&
                            !Covers(inputs.RuntimeData(in read), segment.Start, segment.End) &&
                            !missing.Exists(value => value.Key == read.Key))
                            missing.Add(read);
                    if (missing.Count != 0 && !taken.Contains(transition) && suggested.Add((transition, segment.Start)))
                        m_Suggestions.Add(new TimelinePreviewConditionSuggestion(state, transition, missing,
                            segment.Start, segment.End));
                }
            }
        }

        // 是条件（运行数据，含本链没有 Clip 提供的状态）时登记并返回 true。
        bool AddCondition(OrchestrationStaticTimeline timeline, in OrchestrationStaticRead read, HashSet<string> keys)
        {
            OrchestrationStaticReadSource source = timeline == null ? read.Source : OrchestrationStaticReads.EffectiveSource(timeline, in read);
            if (source != OrchestrationStaticReadSource.RuntimeData)
                return false;
            if (keys.Add(read.Key))
                m_Conditions.Add(read);
            return true;
        }

        static void CollectTaken(TimelineStaticChain chain, HashSet<OrchestrationStaticTransition> taken)
        {
            foreach (TimelineStaticChainMarker marker in chain.Markers)
                taken.Add(marker.Transition);
            foreach (TimelineStaticChainBranch branch in chain.Branches)
                CollectTaken(branch.Chain, taken);
        }

        static bool Covers(IReadOnlyList<TimelineStaticInterval> intervals, OrchestrationTime start, OrchestrationTime end)
        {
            OrchestrationTime reached = start;
            foreach (TimelineStaticInterval interval in intervals.OrderBy(value => value.Start))
            {
                if (interval.Start > reached)
                    return false;
                if (interval.End > reached)
                    reached = interval.End;
                if (reached >= end)
                    return true;
            }
            return reached >= end;
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
            // 本地时刻读该次播放的正式游标，取此刻最后开始的一段；嵌套播放与调用方同时进行，不代替调用方作为当前播放。
            TimelinePreviewRouteSegment? at = null;
            foreach (TimelinePreviewRouteSegment segment in m_Route)
                if (segment.Contains(cursor) && !segment.IsNested)
                    at = segment;
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
