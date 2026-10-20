using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
        readonly List<TimelineAssetPreviewTraversal> m_Traversals = new();
        readonly List<OrchestrationStaticRead> m_Assumptions = new();
        TimelineOrchestrationQuery m_Query;
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
        public bool IsOpen => m_Stage;
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
            if (Mode == TimelinePreviewMode.Session)
            {
                m_SessionOpening = new CancellationTokenSource();
                m_Provider.OpenSession(Source, Preferences, m_SessionOpening.Token);
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
            Time = Mathf.Clamp(time, 0f, Duration);
            m_SampleRequested = true;
            Changed?.Invoke();
        }

        public void SetLocalTime(float time)
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
                if (traversal.Source == Source && time >= traversal.LocalStart.AsSingle &&
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
            if (source != Source)
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
            m_ChainRequested = true;
        }

        public void PreferencesChanged()
        {
            EditorPrefs.SetString(PreferenceKey(Source), JsonUtility.ToJson(Preferences));
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
            bool closeSession = IsOpeningSession;
            m_SessionOpening?.Cancel();
            m_SessionOpening?.Dispose();
            m_SessionOpening = null;
            m_PendingSource = null;
            IsPlaying = false;
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
            try
            {
                if (m_Dirty.Count != 0)
                {
                    TimelineAsset[] changed = m_Dirty.ToArray();
                    m_Dirty.Clear();
                    foreach (TimelineAsset timeline in changed)
                        Prepare(timeline, true);
                    m_ChainRequested = true;
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
                    return;
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
                    current.Cycle, ++m_Revision, Preferences, cursor, traversals);
                m_Instance.BeginTarget(in target);
                PreparedTimeline sampled = m_Contents[current.Source];
                TimelineRuntimePresentationOperations operations = sampled.Sampling.SampleAt(target.Time, target.Cycle,
                    current.PlaybackCompleted ? TimelinePresentationSampleReason.Completed : TimelinePresentationSampleReason.Correction);
                foreach (ITimelineDomainPreview domain in sampled.Domains.Domains)
                    domain.Sample(in target, in operations);
                m_Instance.CompleteTarget(in target);
                SampledRevision = target.Revision;
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
            Action changed = () => { m_Dirty.Add(source); m_ChainRequested = true; };
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
                var inputs = new TimelineAssetPreviewReadIntervals(Preferences, m_Instance.Inputs, InputRequests);
                Chain = TimelineStaticChain.Build(m_EntryState, Preferences.StartingSectionId,
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
                        if (marker.Time == span.End && !marker.IsSectionJump)
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
            OrchestrationTime local = Preferences.StartingSectionId.Length == 0 ? OrchestrationTime.Zero
                : content.Sections.First(section => section.AuthoringId == Preferences.StartingSectionId).Time;
            OrchestrationTime limit = OrchestrationTime.FromSeconds(Preferences.RangeEnd);
            OrchestrationTime global = OrchestrationTime.Zero;
            OrchestrationTime[] boundaries = content.Sections.Select(section => section.Time)
                .Append(content.Duration).Distinct().OrderBy(time => time).ToArray();
            int cycle = 0;
            do
            {
                TimelineContentSection currentSection = content.Sections.LastOrDefault(section => section.Time <= local);
                OrchestrationTime boundary = content.Duration;
                foreach (OrchestrationTime next in boundaries)
                    if (next > local) { boundary = next; break; }
                OrchestrationTime end = OrchestrationTime.Min(global + boundary - local, limit);
                m_Traversals.Add(new TimelineAssetPreviewTraversal(Source, prepared.Content,
                    global, end, local, local + end - global, cycle, null, null,
                    !content.Loop && local + end - global == boundary &&
                    (currentSection.NextSectionId?.Length > 0 || boundary == content.Duration)));
                local += end - global;
                global = end;
                if (global == limit || currentSection.NextSectionId?.Length > 0 ||
                    local == content.Duration && !content.Loop)
                    break;
                if (local == content.Duration)
                {
                    local = OrchestrationTime.Zero;
                    cycle++;
                }
            } while (global < limit);
        }

        void CollectAssumptions(OrchestrationStaticTimeline timeline, IReadOnlyList<OrchestrationStaticRead> reads)
        {
            foreach (OrchestrationStaticRead value in reads)
            {
                OrchestrationStaticReadSource source = timeline == null ? value.Source : TimelineDoorReads.EffectiveSource(timeline, in value);
                if (source == OrchestrationStaticReadSource.RuntimeData && !m_Assumptions.Contains(value))
                    m_Assumptions.Add(value);
            }
        }

        static string PreferenceKey(TimelineAsset source) =>
            "BTSMTL.Timeline.AssetPreview." + GlobalObjectId.GetGlobalObjectIdSlow(source);

        static TimelineAssetPreviewPreferences LoadPreferences(TimelineAsset source)
        {
            string json = EditorPrefs.GetString(PreferenceKey(source), string.Empty);
            return json.Length == 0 ? new TimelineAssetPreviewPreferences()
                : JsonUtility.FromJson<TimelineAssetPreviewPreferences>(json);
        }
    }
}
