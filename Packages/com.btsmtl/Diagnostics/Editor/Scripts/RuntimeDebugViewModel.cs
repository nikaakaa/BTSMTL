using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Diagnostics.Editor
{
    public static class RuntimeTraceDisplay
    {
        static readonly string[] s_MotionChannels = { "Locomotion", "Action", "GameplayResult" };
        static readonly string[] s_MotionBlends = { "Additive", "WeightedBlend", "Override" };
        static readonly string[] s_AnimationCommands = { "", "Select", "Sample", "Complete", "Release", "ProjectedSample", "Withdraw" };

        public static string Status(in RuntimeTracePayload value) => value.DetailKind == RuntimeTraceDetailKind.AnimationCommand
            ? s_AnimationCommands[value.LifecycleOperation] : value.Status.ToString();

        static string AnimationPlayback(in RuntimeTracePayload value) =>
            $"{value.OwnerId}/{value.RelatedElementId}@{value.ActivationGeneration}";

        static string AnimationSource(in RuntimeTracePayload value)
        {
            if (value.SourceEventId.IsValid)
                return value.SourceEventId.ToString();
            if (value.ClipIndex == 0)
                return string.Empty;
            UnityEngine.AnimationClip clip = value.SourceAnimationClip ??
                (UnityEngine.AnimationClip)UnityEditor.EditorUtility.InstanceIDToObject(value.ClipIndex);
            return $"{clip.name}#{value.ClipIndex}";
        }

        public static string Detail(in RuntimeTracePayload value) => value.DetailKind switch
        {
            RuntimeTraceDetailKind.CharacterControl => ControlDetail(
                (CharacterControlTraceKind)value.LifecycleOperation, value.OwnerId,
                value.RelatedElementId, value.Flag, value.StartTick),
            RuntimeTraceDetailKind.GameplayEffectFailure =>
                $"{value.OwnerId}/{value.ActivationGeneration}->{value.Name}:{(GameplayEffectApplyResultKind)value.ActionResult}:{value.Detail}",
            RuntimeTraceDetailKind.GameplayTagCounts => $"{value.BeforeCount}->{value.Value.Signed}",
            RuntimeTraceDetailKind.EquipmentChange =>
                $"{value.Status}:{value.ActivationGeneration}:{value.RelatedElementId}->{value.OwnerId}@{value.Revision}",
            RuntimeTraceDetailKind.ControlFlow =>
                $"{value.RelatedElementId}:{value.Priority}->{value.ClipIndex}:condition={(value.TrackIndex >= 0 ? value.TrackIndex.ToString() : string.Empty)}:result={value.Flag}",
            RuntimeTraceDetailKind.Motion => MotionDetail(in value.MotionDetail, value.Detail),
            RuntimeTraceDetailKind.AbilityRuleSource =>
                $"{value.Detail};source={value.RuleSourceSkillId}/{value.RuleSourceInstanceId};skillGeneration={value.RuleSourceSkillExecutionGeneration};segmentGeneration={value.RuleSourceSegmentGeneration}",
            RuntimeTraceDetailKind.AnimationCommand =>
                $"logicTick={value.StartTick};playback={AnimationPlayback(in value)};channel={value.AnimationChannelId};action={value.ActionInstanceId};generation={value.ActivationGeneration};time={value.Time:0.######};continuous={value.SecondaryTime:0.######};cycle={value.Cycle};weight={value.Weight:0.######};loop={value.Flag}",
            _ => value.Detail.ToString()
        };

        static string MotionDetail(in SimulationMotionTraceDetail value, CanonicalTextValue owner) => value.Kind switch
        {
            SimulationMotionTraceDetailKind.Contribution =>
                $"channel={s_MotionChannels[value.Channel]};blend={s_MotionBlends[value.BlendMode]};priority={value.Priority};weight={FixedScalar.FromRaw(value.WeightRaw)};delta=({FixedScalar.FromRaw(value.DisplacementXRaw)},{FixedScalar.FromRaw(value.DisplacementYRaw)},{FixedScalar.FromRaw(value.DisplacementZRaw)});yaw={FixedScalar.FromRaw(value.YawDegreesRaw)};claim={value.ClaimsLowerChannels};movementClock={MovementClock(in value.MovementClock)}",
            SimulationMotionTraceDetailKind.Channel =>
                $"channel={s_MotionChannels[value.Channel]};owner={owner};delta=({FixedScalar.FromRaw(value.DisplacementXRaw)},{FixedScalar.FromRaw(value.DisplacementYRaw)},{FixedScalar.FromRaw(value.DisplacementZRaw)});yaw={FixedScalar.FromRaw(value.YawDegreesRaw)};planarBasis=({FixedScalar.FromRaw(value.PlanarBasisXRaw)},{FixedScalar.FromRaw(value.PlanarBasisYRaw)});claim={value.ClaimsLowerChannels};sources={value.ParticipatingSourceCount};fingerprint={value.ParticipatingSourceFingerprint:x16};movementClock={MovementClock(in value.MovementClock)}",
            SimulationMotionTraceDetailKind.Resolved =>
                $"delta=({FixedScalar.FromRaw(value.DisplacementXRaw)},{FixedScalar.FromRaw(value.DisplacementYRaw)},{FixedScalar.FromRaw(value.DisplacementZRaw)});yaw={FixedScalar.FromRaw(value.YawDegreesRaw)};hasMotion={value.HasMotion};movementClock={MovementClock(in value.MovementClock)}",
            SimulationMotionTraceDetailKind.None => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(value.Kind))
        };

        static string ControlDetail(CharacterControlTraceKind kind, CanonicalTextValue source,
            CanonicalTextValue target, bool passed, ulong tick) => kind switch
        {
            CharacterControlTraceKind.StateEntered or CharacterControlTraceKind.StateExited => $"state={source}:tick={tick}",
            CharacterControlTraceKind.TransitionEvaluated => $"source={source}:target={target}:result={passed}",
            CharacterControlTraceKind.TransitionSelected => $"source={source}:target={target}:tick={tick}",
            _ => throw new InvalidOperationException("Character Control trace kind is unsupported.")
        };

        static string MovementClock(in CommittedMovementPlaybackClock value) => value.IsValid
            ? $"{value.OwnerIdentity}@{value.Generation}:{value.ContinuousTicks}/{value.TickRate}#tick{value.AuthorityTick.Value}"
            : "none";

        public static RuntimeTraceEvent ForDisplay(in RuntimeTraceEvent value) => ForDisplay(in value, out _);

        internal static RuntimeTraceEvent ForDisplay(in RuntimeTraceEvent value, out RuntimeLiveStateKey stateKey)
        {
            RuntimeTraceEvent display = value.CaptureDisplayText();
            stateKey = new RuntimeLiveStateKey(in display);
            RuntimeTracePayload payload = display.Payload;
            payload.Detail = Detail(in payload);
            if (payload.DetailKind == RuntimeTraceDetailKind.AnimationCommand)
            {
                payload.Status = Status(in payload);
                string playback = AnimationPlayback(in payload);
                payload.RelatedElementId = AnimationSource(in payload);
                payload.OwnerId = playback;
            }
            payload.DetailKind = RuntimeTraceDetailKind.Text;
            return new RuntimeTraceEvent(display.SessionId, display.ContentRevision, display.RuntimeEpoch,
                display.ExecutionBranchId, display.Domain, display.Channel, display.Position, display.TimeSeconds,
                display.Sequence, display.RuntimeInstance, display.Source, display.Kind, in payload);
        }
    }

    public readonly struct RuntimeDebugTargetInfo
    {
        public RuntimeDebugTargetInfo(RuntimeDiagnosticsTarget target)
        {
            DisplayName = target?.DisplayName ?? string.Empty;
            HostInstanceId = target?.HostInstanceId ?? 0;
            CharacterRuntimeId = target?.CharacterRuntimeId ?? Guid.Empty;
            SessionId = target?.SessionId ?? Guid.Empty;
            Revision = target?.Revision ?? default;
            RuntimeEpoch = target?.RuntimeEpoch ?? 0;
        }

        public string DisplayName { get; }
        public int HostInstanceId { get; }
        public Guid CharacterRuntimeId { get; }
        public Guid SessionId { get; }
        public RuntimeContentRevision Revision { get; }
        public ulong RuntimeEpoch { get; }
    }

    public readonly struct RuntimeDebugEventView
    {
        public RuntimeDebugEventView(in RuntimeTraceEvent traceEvent, RuntimeSourceElementKey source, string sourceName)
        {
            Event = traceEvent;
            Source = source;
            SourceName = sourceName ?? string.Empty;
        }

        public readonly RuntimeTraceEvent Event;
        public RuntimeSourceElementKey Source { get; }
        public string SourceName { get; }
    }

    public readonly struct RuntimeElementDebugState
    {
        static readonly string[] s_EventKindText = Enum.GetNames(typeof(RuntimeTraceEventKind));

        public RuntimeElementDebugState(in RuntimeDebugEventView eventView)
        {
            Source = eventView.Source;
            SourceName = eventView.SourceName;
            Instance = eventView.Event.RuntimeInstance;
            Kind = eventView.Event.Kind;
            Domain = eventView.Event.Domain;
            Position = eventView.Event.Position;
            Sequence = eventView.Event.Sequence;
            Payload = eventView.Event.Payload;
        }

        public RuntimeSourceElementKey Source { get; }
        public string SourceName { get; }
        public RuntimeInstanceKey Instance { get; }
        public RuntimeTraceEventKind Kind { get; }
        public RuntimeTraceDomain Domain { get; }
        public ulong Position { get; }
        public ulong Sequence { get; }
        public RuntimeTracePayload Payload { get; }
        public string Status => Payload.DetailKind == RuntimeTraceDetailKind.AnimationCommand || !Payload.Status.IsEmpty
            ? RuntimeTraceDisplay.Status(Payload) : s_EventKindText[(int)Kind];
        public string Detail => RuntimeTraceDisplay.Detail(Payload);
    }

    public readonly struct RuntimeTimelinePlaybackDebugSummary
    {
        public RuntimeTimelinePlaybackDebugSummary(
            RuntimeInstanceKey playback,
            RuntimeTimelinePlaybackProvenance provenance,
            ulong latestLogicTick,
            ulong latestPresentationFrame,
            float logicTime,
            float visualTime,
            int logicCycle,
            int visualCycle,
            RuntimeTraceEventKind lifecycle,
            CanonicalTextValue lifecycleStatus,
            RuntimeTraceEventKind terminal,
            CanonicalTextValue terminalCause)
        {
            Playback = playback;
            Provenance = provenance;
            LatestLogicTick = latestLogicTick;
            LatestPresentationFrame = latestPresentationFrame;
            LogicTime = logicTime;
            VisualTime = visualTime;
            LogicCycle = logicCycle;
            VisualCycle = visualCycle;
            Lifecycle = lifecycle;
            m_LifecycleStatus = lifecycleStatus.IsNull ? CanonicalTextValue.Empty : lifecycleStatus;
            Terminal = terminal;
            m_TerminalCause = terminalCause.IsNull ? CanonicalTextValue.Empty : terminalCause;
        }

        public RuntimeInstanceKey Playback { get; }
        public RuntimeTimelinePlaybackProvenance Provenance { get; }
        public ulong LatestLogicTick { get; }
        public ulong LatestPresentationFrame { get; }
        public float LogicTime { get; }
        public float VisualTime { get; }
        public int LogicCycle { get; }
        public int VisualCycle { get; }
        public RuntimeTraceEventKind Lifecycle { get; }
        readonly CanonicalTextValue m_LifecycleStatus;
        public string LifecycleStatus => m_LifecycleStatus.ToString();
        public RuntimeTraceEventKind Terminal { get; }
        readonly CanonicalTextValue m_TerminalCause;
        public string TerminalCause => m_TerminalCause.ToString();
        public bool IsTerminal => Terminal is RuntimeTraceEventKind.TimelineCompleted or RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped;
    }

    public sealed class RuntimeDebugChangeSet
    {
        readonly HashSet<RuntimeSourceElementKey> m_Sources;
        readonly HashSet<RuntimeInstanceKey> m_Instances;

        internal RuntimeDebugChangeSet(HashSet<RuntimeSourceElementKey> sources, HashSet<RuntimeInstanceKey> instances)
        {
            m_Sources = sources;
            m_Instances = instances;
        }

        internal void Update(long revision, bool fullSync, long captureVersion)
        {
            Revision = revision;
            FullSync = fullSync;
            CaptureVersion = captureVersion;
        }

        public long Revision { get; private set; }
        public bool FullSync { get; private set; }
        public long CaptureVersion { get; private set; }
        public IReadOnlyCollection<RuntimeSourceElementKey> Sources => m_Sources;
        public IReadOnlyCollection<RuntimeInstanceKey> Instances => m_Instances;

        public HashSet<RuntimeSourceElementKey>.Enumerator GetSourceEnumerator() => m_Sources.GetEnumerator();
        public bool AffectsInstance(RuntimeInstanceKey instance) => FullSync || m_Instances.Contains(instance);

        public bool AffectsSourceKind(RuntimeSourceElementKind kind)
        {
            if (FullSync)
                return true;
            foreach (RuntimeSourceElementKey source in m_Sources)
                if (source.Kind == kind)
                    return true;
            return false;
        }

        public bool AffectsSource(RuntimeSourceElementKey source)
        {
            return FullSync || m_Sources.Contains(source);
        }

        public bool AffectsGraph(string graphAuthoringId, RuntimeInstanceKey instance)
        {
            if (FullSync)
                return true;
            foreach (RuntimeSourceElementKey source in m_Sources)
            {
                if (string.Equals(source.GraphAuthoringId, graphAuthoringId, StringComparison.Ordinal))
                    return !instance.IsValid || m_Instances.Contains(instance);
            }
            return false;
        }

        public bool AffectsTimeline(string timelineAuthoringId, RuntimeInstanceKey playback)
        {
            if (FullSync)
                return true;
            foreach (RuntimeSourceElementKey source in m_Sources)
            {
                if (string.Equals(source.TimelineAuthoringId, timelineAuthoringId, StringComparison.Ordinal))
                    return !playback.IsValid || m_Instances.Contains(playback);
            }
            return false;
        }
    }

    public sealed class RuntimeDebugViewModel
    {
        readonly RuntimeDebugSourceMapSnapshot m_SourceMap;
        readonly Dictionary<RuntimeLiveStateKey, int> m_EventSlots;
        readonly RuntimeDebugEventView[] m_Events;
        readonly int[] m_NextInstanceEvent;
        sealed class InstanceEventIndex
        {
            public int Head;
            public int Latest;
            public int GraphSamplingLimits;
            public int StateMachineSamplingLimits;
            public int ValueSamplingLimits;
        }

        readonly Dictionary<RuntimeInstanceKey, InstanceEventIndex> m_InstanceEvents;
        readonly Stack<InstanceEventIndex> m_FreeInstanceEvents;
        readonly HashSet<RuntimeInstanceKey> m_RemovedLatestInstances;
        readonly Dictionary<ElementInstanceKey, ElementEventIndex> m_ElementEventSlots;
        readonly int[] m_NextElementEvent;
        readonly RuntimeDebugSourceMapSnapshot[] m_EventSourceMaps;
        readonly int[] m_FreeEventSlots;
        readonly Dictionary<(RuntimeInstanceKey Instance, string Graph), (ulong Sequence, bool HasRemoval)> m_PendingGraphUpdates;
        readonly HashSet<RuntimeInstanceKey> m_RemovedTimelinePlaybacks;
        readonly Dictionary<string, Dictionary<RuntimeInstanceKey, ulong>> m_GraphInstances = new(StringComparer.Ordinal);
        readonly Dictionary<string, long> m_GraphInstanceRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly InstanceSequenceOrder m_InstanceSequenceOrder;
        readonly TimelineInstanceSequenceOrder m_TimelineInstanceSequenceOrder;
        readonly Dictionary<RuntimeInstanceKey, TimelinePlaybackSummaryBuilder> m_TimelinePlayback;
        readonly Dictionary<TimelineSourceKey, HashSet<RuntimeInstanceKey>> m_TimelinePlaybackMembership = new Dictionary<TimelineSourceKey, HashSet<RuntimeInstanceKey>>();
        readonly Dictionary<TimelineSourceKey, long> m_TimelinePlaybackRevisions = new Dictionary<TimelineSourceKey, long>();
        readonly HashSet<RuntimeSourceElementKey> m_PendingSources;
        readonly HashSet<RuntimeInstanceKey> m_PendingInstances;
        readonly RuntimeDebugChangeSet m_Changes;
        RuntimeTraceChannel m_Channels;
        bool m_PendingFullSync;
        ulong m_LatestLogicTick;
        ulong m_LatestPresentationFrame;
        bool m_HasCoverageGap;
        long m_EvictedStates;
        int m_EventCount;
        int m_FreeEventSlotCount;
        bool m_LogicPositionNeedsRefresh;
        bool m_PresentationPositionNeedsRefresh;

        internal RuntimeDebugViewModel(RuntimeDebugTargetInfo target, RuntimeDebugSourceMapSnapshot sourceMap, RuntimeTraceChannel channels, int eventCapacity)
        {
            Target = target;
            m_SourceMap = sourceMap;
            m_Channels = channels;
            m_EventSlots = new Dictionary<RuntimeLiveStateKey, int>(eventCapacity);
            m_Events = new RuntimeDebugEventView[eventCapacity];
            m_ElementEventSlots = new Dictionary<ElementInstanceKey, ElementEventIndex>(eventCapacity);
            m_NextElementEvent = new int[eventCapacity];
            m_EventSourceMaps = new RuntimeDebugSourceMapSnapshot[eventCapacity];
            m_FreeEventSlots = new int[eventCapacity];
            m_PendingGraphUpdates = new Dictionary<(RuntimeInstanceKey, string), (ulong, bool)>(eventCapacity);
            m_RemovedTimelinePlaybacks = new HashSet<RuntimeInstanceKey>(eventCapacity);
            m_PendingSources = new HashSet<RuntimeSourceElementKey>(eventCapacity);
            m_PendingInstances = new HashSet<RuntimeInstanceKey>(eventCapacity);
            m_NextInstanceEvent = new int[eventCapacity];
            m_InstanceEvents = new Dictionary<RuntimeInstanceKey, InstanceEventIndex>(eventCapacity);
            m_FreeInstanceEvents = new Stack<InstanceEventIndex>(eventCapacity);
            for (int i = 0; i < eventCapacity; i++)
                m_FreeInstanceEvents.Push(new InstanceEventIndex());
            m_RemovedLatestInstances = new HashSet<RuntimeInstanceKey>(eventCapacity);
            m_TimelinePlayback = new Dictionary<RuntimeInstanceKey, TimelinePlaybackSummaryBuilder>(eventCapacity);
            m_Changes = new RuntimeDebugChangeSet(m_PendingSources, m_PendingInstances);
            m_InstanceSequenceOrder = new InstanceSequenceOrder();
            m_TimelineInstanceSequenceOrder = new TimelineInstanceSequenceOrder(this);
        }

        public static RuntimeDebugViewModel Detached { get; } = new RuntimeDebugViewModel(default, RuntimeDebugSourceMapSnapshot.Empty, RuntimeTraceChannel.None, 0);
        public RuntimeDebugTargetInfo Target { get; }
        public RuntimeTraceChannel Channels => m_Channels;
        public bool Attached => Target.CharacterRuntimeId != Guid.Empty;
        public ulong LatestLogicTick => m_LatestLogicTick;
        public ulong LatestPresentationFrame => m_LatestPresentationFrame;
        public long Revision => m_Changes.Revision;
        public bool HasCoverageGap => m_HasCoverageGap;
        public long EvictedStates => m_EvictedStates;

        public RuntimeExecutionTimeline BuildExecutionTimeline(
            RuntimeCaptureSnapshot capture,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default) =>
            RuntimeExecutionTimelineBuilder.Build(capture, m_SourceMap, historyOffset, instance);

        public RuntimeExecutionHistory BuildExecutionHistory(
            RuntimeCaptureSnapshot capture,
            int historyOffset = 0,
            RuntimeInstanceKey instance = default) =>
            RuntimeExecutionTimelineBuilder.BuildHistory(capture, m_SourceMap, null, historyOffset, instance);

        internal RuntimeExecutionHistory BuildExecutionHistory(
            RuntimeCaptureSnapshot capture,
            int historyOffset,
            RuntimeInstanceKey instance,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps,
            ulong throughSequence) =>
            RuntimeExecutionTimelineBuilder.BuildHistory(capture, m_SourceMap, sourceMaps, historyOffset, instance, throughSequence);

        internal void SetCoverage(long evictedStates, bool incompleteHistory)
        {
            m_EvictedStates = evictedStates;
            m_HasCoverageGap = incompleteHistory;
        }
        public RuntimeDebugChangeSet Changes => m_Changes;
        public IReadOnlyList<RuntimeGraphInvocation> GetGraphInvocations(RuntimeInstanceKey instance) =>
            TryGetLatestInstanceEvent(instance, out int slot)
                ? m_EventSourceMaps[slot].GraphInvocations
                : Array.Empty<RuntimeGraphInvocation>();

        public bool TryGetInvocation(RuntimeInstanceKey instance, CanonicalTextValue path, out RuntimeGraphInvocation invocation)
        {
            invocation = default;
            return TryGetLatestInstanceEvent(instance, out int slot) &&
                   m_EventSourceMaps[slot].TryGetInvocation(path, out invocation);
        }
        public bool TryGetParentInvocation(RuntimeInstanceKey instance, out string path,
            out Guid runtimeId, out ulong generation)
        {
            if (instance.Kind == RuntimeInstanceKind.SkillExecution &&
                TryGetLatestInstanceEvent(instance, out int slot))
            {
                ref readonly RuntimeTraceEvent traceEvent = ref m_Events[slot].Event;
                path = traceEvent.GetParentInvocationPath().ToString();
                generation = traceEvent.GetParentInvocationGeneration();
                runtimeId = traceEvent.GetParentGraphRuntimeId();
                return !string.IsNullOrEmpty(path);
            }
            path = null;
            runtimeId = Guid.Empty;
            generation = 0;
            return false;
        }
        public ulong InvocationSequence(RuntimeInstanceKey instance) =>
            instance.Kind == RuntimeInstanceKind.SkillExecution && TryGetLatestInstanceEvent(instance, out int slot)
                ? m_Events[slot].Event.Sequence : 0;

        public RuntimeDebugTargetMatch MatchSource(RuntimeDebugTargetRequest request)
        {
            return m_SourceMap.Match(request);
        }

        public void CopyGraphInstances(string graphAuthoringId, List<RuntimeInstanceKey> destination)
        {
            destination.Clear();
            if (!m_GraphInstances.TryGetValue(graphAuthoringId, out Dictionary<RuntimeInstanceKey, ulong> instances))
                return;
            foreach (KeyValuePair<RuntimeInstanceKey, ulong> instance in instances)
                destination.Add(instance.Key);
            m_InstanceSequenceOrder.Sequences = instances;
            destination.Sort(m_InstanceSequenceOrder.Comparison);
        }

        public bool ContainsGraphInstance(string graphAuthoringId, RuntimeInstanceKey instance) =>
            m_GraphInstances.TryGetValue(graphAuthoringId, out Dictionary<RuntimeInstanceKey, ulong> instances) &&
            instances.ContainsKey(instance);

        public long GetGraphInstanceRevision(string graphAuthoringId)
        {
            return !string.IsNullOrEmpty(graphAuthoringId) && m_GraphInstanceRevisions.TryGetValue(graphAuthoringId, out long revision)
                ? revision
                : 0;
        }

        public void CopyTimelineInstances(
            string timelineAuthoringId,
            string graphAuthoringId,
            List<RuntimeInstanceKey> destination)
        {
            destination.Clear();
            if (!m_TimelinePlaybackMembership.TryGetValue(new TimelineSourceKey(timelineAuthoringId, graphAuthoringId), out HashSet<RuntimeInstanceKey> playbacks))
                return;
            foreach (RuntimeInstanceKey playback in playbacks)
            {
                if (MatchesTimeline(
                        m_TimelinePlayback[playback],
                        timelineAuthoringId,
                        graphAuthoringId))
                    destination.Add(playback);
            }
            destination.Sort(m_TimelineInstanceSequenceOrder.Comparison);
        }

        public long GetTimelinePlaybackRevision(
            string timelineAuthoringId,
            string graphAuthoringId = "")
        {
            TimelineSourceKey key = new TimelineSourceKey(timelineAuthoringId, graphAuthoringId);
            return !string.IsNullOrEmpty(timelineAuthoringId) && m_TimelinePlaybackRevisions.TryGetValue(key, out long revision)
                ? revision
                : 0;
        }

        public IReadOnlyList<RuntimeTimelinePlaybackDebugSummary> GetTimelinePlaybackSummaries(
            string timelineAuthoringId,
            string graphAuthoringId = "")
        {
            var result = new List<RuntimeTimelinePlaybackDebugSummary>();
            CopyTimelinePlaybackSummaries(timelineAuthoringId, graphAuthoringId, result);
            return result;
        }

        public void CopyTimelinePlaybackSummaries(
            string timelineAuthoringId,
            string graphAuthoringId,
            List<RuntimeTimelinePlaybackDebugSummary> destination)
        {
            destination.Clear();
            if (!m_TimelinePlaybackMembership.TryGetValue(new TimelineSourceKey(timelineAuthoringId, graphAuthoringId), out HashSet<RuntimeInstanceKey> playbacks))
                return;
            foreach (RuntimeInstanceKey playback in playbacks)
            {
                TimelinePlaybackSummaryBuilder builder = m_TimelinePlayback[playback];
                if (MatchesTimeline(builder, timelineAuthoringId, graphAuthoringId))
                    destination.Add(builder.Build());
            }
            destination.Sort((left, right) => right.LatestLogicTick != left.LatestLogicTick
                ? right.LatestLogicTick.CompareTo(left.LatestLogicTick)
                : right.LatestPresentationFrame.CompareTo(left.LatestPresentationFrame));
        }

        public bool TryGetTimelinePlaybackSummary(
            string timelineAuthoringId,
            RuntimeInstanceKey playback,
            out RuntimeTimelinePlaybackDebugSummary summary,
            CanonicalTextValue graphAuthoringId = default)
        {
            if (m_TimelinePlayback.TryGetValue(playback, out TimelinePlaybackSummaryBuilder builder) &&
                MatchesTimeline(builder, timelineAuthoringId, graphAuthoringId))
            {
                summary = builder.Build();
                return true;
            }

            summary = default;
            return false;
        }

        public void CopyTimelineCurrentEvents(
            string timelineAuthoringId,
            RuntimeInstanceKey playback,
            List<RuntimeDebugEventView> destination,
            CanonicalTextValue graphAuthoringId = default)
        {
            destination.Clear();
            if (!TryGetInstanceEventHead(playback, out int slot))
                return;
            if (!m_TimelinePlayback.TryGetValue(playback, out TimelinePlaybackSummaryBuilder builder) ||
                !MatchesTimeline(builder, timelineAuthoringId, graphAuthoringId))
                return;

            for (; slot != -1; slot = m_NextInstanceEvent[slot])
                destination.Add(m_Events[slot]);
        }

        static bool MatchesTimeline(
            TimelinePlaybackSummaryBuilder builder,
            string timelineAuthoringId,
            CanonicalTextValue graphAuthoringId)
        {
            return string.Equals(
                       builder.TimelineAuthoringId,
                       timelineAuthoringId,
                       StringComparison.Ordinal) &&
                   (builder.Provenance.SourceGraphAuthoringId.Equals(graphAuthoringId) ||
                    CanonicalTextValue.IsNullOrEmpty(builder.Provenance.SourceGraphAuthoringId) &&
                    CanonicalTextValue.IsNullOrEmpty(graphAuthoringId));
        }

        public void CopyCurrentEvents(RuntimeTraceChannel channels, List<RuntimeDebugEventView> destination)
        {
            destination.Clear();
            for (int slot = 0; slot < m_EventCount; slot++)
            {
                ref readonly RuntimeDebugEventView eventView = ref m_Events[slot];
                if ((eventView.Event.Channel & channels) != 0)
                    destination.Add(eventView);
            }
        }

        public void CopyCurrentEvents(
            string graphAuthoringId,
            RuntimeInstanceKey instance,
            RuntimeTraceChannel channels,
            List<RuntimeDebugEventView> destination)
        {
            destination.Clear();
            if (!TryGetInstanceEventHead(EventInstance(instance), out int slot))
                return;
            for (; slot != -1; slot = m_NextInstanceEvent[slot])
            {
                ref readonly RuntimeDebugEventView eventView = ref m_Events[slot];
                if ((eventView.Event.Channel & channels) != 0 &&
                    eventView.Event.RuntimeInstance.Equals(instance) &&
                    (string.Equals(eventView.Source.GraphAuthoringId, graphAuthoringId, StringComparison.Ordinal) ||
                     eventView.Event.Kind is RuntimeTraceEventKind.TraceSamplingLimited or RuntimeTraceEventKind.ValueSamplingLimited))
                    destination.Add(eventView);
            }
        }

        public void CopyCurrentEvents(
            RuntimeSourceElementKey source,
            RuntimeInstanceKey instance,
            RuntimeTraceChannel channels,
            List<RuntimeDebugEventView> destination)
        {
            destination.Clear();
            if (!m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index) ||
                !m_ElementEventSlots.TryGetValue(new ElementInstanceKey(source, index), out ElementEventIndex element))
                return;
            for (int slot = element.Head; slot != -1; slot = m_NextElementEvent[slot])
            {
                ref readonly RuntimeDebugEventView eventView = ref m_Events[slot];
                if ((eventView.Event.Channel & channels) != 0)
                    destination.Add(eventView);
            }
        }

        public bool HasSamplingLimit(RuntimeInstanceKey instance, RuntimeTraceChannel channels)
        {
            return m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index) &&
                   ((channels & RuntimeTraceChannel.Graph) != 0 && index.GraphSamplingLimits != 0 ||
                    (channels & RuntimeTraceChannel.StateMachine) != 0 && index.StateMachineSamplingLimits != 0 ||
                    (channels & RuntimeTraceChannel.Values) != 0 && index.ValueSamplingLimits != 0);
        }

        public bool TryGetState(RuntimeSourceElementKey source, RuntimeInstanceKey instance, out RuntimeElementDebugState state)
        {
            state = default;
            if (!instance.IsValid || !m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index) ||
                !m_ElementEventSlots.TryGetValue(new ElementInstanceKey(source, index), out ElementEventIndex element))
                return false;
            state = new RuntimeElementDebugState(in m_Events[element.Latest]);
            return true;
        }

        internal void SetChannels(RuntimeTraceChannel channels)
        {
            m_Channels = channels;
        }

        internal void ApplyRecordedState(in RuntimeLiveStateRead state,
            IReadOnlyDictionary<RuntimeContentRevision, RuntimeDebugSourceMapSnapshot> sourceMaps)
        {
            BeginUpdate(state.RequiresFullSync);
            for (int i = 0; i < state.Changes.Count; i++)
            {
                RuntimeLiveStateChange change = state.Changes[i];
                RuntimeLiveStateKey key = change.Key;
                if (change.IsRemoval)
                {
                    Remove(in key);
                    continue;
                }
                RuntimeTraceEvent value = change.TraceEvent;
                if (m_EventSlots.ContainsKey(key))
                    Remove(in key);
                Apply(in value, sourceMaps[value.ContentRevision]);
            }
            CommitUpdate();
        }

        internal void BeginUpdate(bool fullSync)
        {
            m_PendingFullSync = fullSync;
            m_PendingSources.Clear();
            m_PendingInstances.Clear();
            m_RemovedLatestInstances.Clear();
            m_PendingGraphUpdates.Clear();
            m_RemovedTimelinePlaybacks.Clear();
            m_LogicPositionNeedsRefresh = false;
            m_PresentationPositionNeedsRefresh = false;
            if (!fullSync)
                return;

            m_EventSlots.Clear();
            Array.Clear(m_Events, 0, m_EventCount);
            Array.Clear(m_EventSourceMaps, 0, m_EventCount);
            m_EventCount = 0;
            m_FreeEventSlotCount = 0;
            foreach (InstanceEventIndex index in m_InstanceEvents.Values)
                m_FreeInstanceEvents.Push(index);
            m_InstanceEvents.Clear();
            m_ElementEventSlots.Clear();
            foreach (Dictionary<RuntimeInstanceKey, ulong> instances in m_GraphInstances.Values)
                instances.Clear();
            m_GraphInstanceRevisions.Clear();
            m_TimelinePlayback.Clear();
            foreach (HashSet<RuntimeInstanceKey> playbacks in m_TimelinePlaybackMembership.Values)
                playbacks.Clear();
            m_TimelinePlaybackRevisions.Clear();
            m_LatestLogicTick = 0;
            m_LatestPresentationFrame = 0;
        }

        internal void Apply(
            in RuntimeTraceEvent sourceTrace,
            RuntimeDebugSourceMapSnapshot sourceMap)
        {
            RuntimeTraceEvent traceEvent = RuntimeTraceDisplay.ForDisplay(in sourceTrace, out RuntimeLiveStateKey key);
            RuntimeSourceElementKey source = default;
            string sourceName = string.Empty;
            if (traceEvent.Source.IsValid)
            {
                DebugSourceMapEntry entry = sourceMap.Get(traceEvent.Source);
                source = entry.Source;
                sourceName = entry.DisplayName;
            }

            if (traceEvent.Domain == RuntimeTraceDomain.Logic && traceEvent.Position >= m_LatestLogicTick)
            {
                m_LatestLogicTick = traceEvent.Position;
                m_LogicPositionNeedsRefresh = false;
            }
            else if (traceEvent.Domain == RuntimeTraceDomain.Presentation && traceEvent.Position >= m_LatestPresentationFrame)
            {
                m_LatestPresentationFrame = traceEvent.Position;
                m_PresentationPositionNeedsRefresh = false;
            }

            var eventView = new RuntimeDebugEventView(in traceEvent, source, sourceName);
            RuntimeInstanceKey eventInstance = EventInstance(traceEvent.RuntimeInstance);
            InstanceEventIndex eventIndex = GetInstanceEventIndex(eventInstance);
            InstanceEventIndex instanceIndex = eventIndex;
            bool added = !m_EventSlots.TryGetValue(key, out int slot);
            if (added)
            {
                slot = m_FreeEventSlotCount != 0 ? m_FreeEventSlots[--m_FreeEventSlotCount] : m_EventCount++;
                m_EventSlots.Add(key, slot);
                m_NextInstanceEvent[slot] = eventIndex.Head;
                eventIndex.Head = slot;
            }
            m_Events[slot] = eventView;
            m_EventSourceMaps[slot] = sourceMap;
            if (source.IsValid)
                m_PendingSources.Add(source);
            if (traceEvent.RuntimeInstance.IsValid)
            {
                m_PendingInstances.Add(traceEvent.RuntimeInstance);
                if (traceEvent.RuntimeInstance.Kind == RuntimeInstanceKind.TreeClip)
                    instanceIndex = GetInstanceEventIndex(traceEvent.RuntimeInstance);
                if (instanceIndex.Latest == -1 || traceEvent.Sequence > m_Events[instanceIndex.Latest].Event.Sequence)
                    instanceIndex.Latest = slot;
                if (added)
                    UpdateSamplingLimits(instanceIndex, in traceEvent, 1);
            }

            if (source.IsValid && traceEvent.RuntimeInstance.IsValid)
            {
                var elementKey = new ElementInstanceKey(source, instanceIndex);
                if (!m_ElementEventSlots.TryGetValue(elementKey, out ElementEventIndex element))
                    element = new ElementEventIndex { Head = -1, Latest = -1 };
                if (added)
                {
                    m_NextElementEvent[slot] = element.Head;
                    element.Head = slot;
                }
                if (element.Latest == -1 || traceEvent.Sequence > m_Events[element.Latest].Event.Sequence)
                    element.Latest = slot;
                m_ElementEventSlots[elementKey] = element;
                var membership = (traceEvent.RuntimeInstance, source.GraphAuthoringId);
                m_PendingGraphUpdates.TryGetValue(membership, out var update);
                m_PendingGraphUpdates[membership] = (Math.Max(update.Sequence, traceEvent.Sequence), update.HasRemoval);
            }

            ApplyTimeline(in eventView);
        }

        internal void CommitUpdate(long captureVersion = 0)
        {
            foreach (RuntimeInstanceKey instance in m_RemovedLatestInstances)
                RefreshLatestInstanceEvent(instance);
            foreach (var membership in m_PendingGraphUpdates)
            {
                if (membership.Value.HasRemoval)
                {
                    RefreshGraphMembership(membership.Key.Instance, membership.Key.Graph);
                    continue;
                }
                ulong sequence = membership.Value.Sequence;
                if (m_GraphInstances.TryGetValue(membership.Key.Graph, out Dictionary<RuntimeInstanceKey, ulong> instances) &&
                    instances.TryGetValue(membership.Key.Instance, out ulong previousSequence))
                    sequence = Math.Max(sequence, previousSequence);
                RegisterGraphInstance(membership.Key.Graph, membership.Key.Instance, sequence);
            }
            foreach (RuntimeInstanceKey playback in m_RemovedTimelinePlaybacks)
                RefreshTimelinePlayback(playback);
            if (m_LogicPositionNeedsRefresh || m_PresentationPositionNeedsRefresh)
            {
                if (m_LogicPositionNeedsRefresh)
                    m_LatestLogicTick = 0;
                if (m_PresentationPositionNeedsRefresh)
                    m_LatestPresentationFrame = 0;
                for (int slot = 0; slot < m_EventCount; slot++)
                {
                    ref readonly RuntimeTraceEvent traceEvent = ref m_Events[slot].Event;
                    if (m_LogicPositionNeedsRefresh && traceEvent.Domain == RuntimeTraceDomain.Logic)
                        m_LatestLogicTick = Math.Max(m_LatestLogicTick, traceEvent.Position);
                    else if (m_PresentationPositionNeedsRefresh && traceEvent.Domain == RuntimeTraceDomain.Presentation)
                        m_LatestPresentationFrame = Math.Max(m_LatestPresentationFrame, traceEvent.Position);
                }
            }
            m_Changes.Update(m_Changes.Revision + 1, m_PendingFullSync, captureVersion);
            m_PendingFullSync = false;
        }

        internal void Remove(in RuntimeLiveStateKey key)
        {
            int slot = m_EventSlots[key];
            ref readonly RuntimeDebugEventView item = ref m_Events[slot];
            if (item.Event.Domain == RuntimeTraceDomain.Logic && item.Event.Position == m_LatestLogicTick)
                m_LogicPositionNeedsRefresh = true;
            else if (item.Event.Domain == RuntimeTraceDomain.Presentation && item.Event.Position == m_LatestPresentationFrame)
                m_PresentationPositionNeedsRefresh = true;
            RuntimeInstanceKey instance = item.Event.RuntimeInstance;
            RuntimeInstanceKey eventInstance = EventInstance(instance);
            InstanceEventIndex eventIndex = m_InstanceEvents[eventInstance];
            InstanceEventIndex instanceIndex = instance.IsValid && instance.Kind == RuntimeInstanceKind.TreeClip
                ? m_InstanceEvents[instance] : eventIndex;
            if (item.Source.IsValid)
            {
                m_PendingSources.Add(item.Source);
                if (instance.IsValid)
                    m_PendingGraphUpdates[(instance, item.Source.GraphAuthoringId)] = (0, true);
            }
            if (instance.IsValid)
            {
                UpdateSamplingLimits(instanceIndex, in item.Event, -1);
                m_PendingInstances.Add(instance);
                if (instanceIndex.Latest == slot)
                {
                    instanceIndex.Latest = -1;
                    m_RemovedLatestInstances.Add(instance);
                }
            }
            if (eventInstance.Kind == RuntimeInstanceKind.TimelinePlayback)
                m_RemovedTimelinePlaybacks.Add(eventInstance);
            int head = eventIndex.Head;
            int next = m_NextInstanceEvent[slot];
            if (head == slot)
                eventIndex.Head = next;
            else
            {
                int previous = head;
                while (m_NextInstanceEvent[previous] != slot)
                    previous = m_NextInstanceEvent[previous];
                m_NextInstanceEvent[previous] = next;
            }
            if (item.Source.IsValid && instance.IsValid)
            {
                var elementKey = new ElementInstanceKey(item.Source, instanceIndex);
                ElementEventIndex element = m_ElementEventSlots[elementKey];
                if (element.Head == slot)
                    element.Head = m_NextElementEvent[slot];
                else
                {
                    int previous = element.Head;
                    while (m_NextElementEvent[previous] != slot)
                        previous = m_NextElementEvent[previous];
                    m_NextElementEvent[previous] = m_NextElementEvent[slot];
                }
                if (element.Latest == slot)
                {
                    element.Latest = -1;
                    for (int remaining = element.Head; remaining != -1; remaining = m_NextElementEvent[remaining])
                    {
                        if (element.Latest == -1 || m_Events[remaining].Event.Sequence > m_Events[element.Latest].Event.Sequence)
                            element.Latest = remaining;
                    }
                }
                if (element.Head == -1)
                    m_ElementEventSlots.Remove(elementKey);
                else
                    m_ElementEventSlots[elementKey] = element;
            }
            m_EventSlots.Remove(key);
            m_Events[slot] = default;
            m_EventSourceMaps[slot] = default;
            m_FreeEventSlots[m_FreeEventSlotCount++] = slot;
            ReleaseInstanceEventIndex(eventInstance, eventIndex);
        }

        InstanceEventIndex GetInstanceEventIndex(RuntimeInstanceKey instance)
        {
            if (m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index))
                return index;
            index = m_FreeInstanceEvents.Count != 0 ? m_FreeInstanceEvents.Pop() : new InstanceEventIndex();
            index.Head = -1;
            index.Latest = -1;
            index.GraphSamplingLimits = 0;
            index.StateMachineSamplingLimits = 0;
            index.ValueSamplingLimits = 0;
            m_InstanceEvents.Add(instance, index);
            return index;
        }

        static void UpdateSamplingLimits(InstanceEventIndex index, in RuntimeTraceEvent traceEvent, int change)
        {
            if (traceEvent.Kind == RuntimeTraceEventKind.TraceSamplingLimited)
            {
                if ((traceEvent.Channel & RuntimeTraceChannel.Graph) != 0)
                    index.GraphSamplingLimits += change;
                if ((traceEvent.Channel & RuntimeTraceChannel.StateMachine) != 0)
                    index.StateMachineSamplingLimits += change;
            }
            if ((traceEvent.Channel & RuntimeTraceChannel.Values) != 0 &&
                traceEvent.Kind is RuntimeTraceEventKind.TraceSamplingLimited or RuntimeTraceEventKind.ValueSamplingLimited)
                index.ValueSamplingLimits += change;
        }

        void ReleaseInstanceEventIndex(RuntimeInstanceKey instance, InstanceEventIndex index)
        {
            if (index.Head != -1 || index.Latest != -1)
                return;
            m_InstanceEvents.Remove(instance);
            m_FreeInstanceEvents.Push(index);
        }

        bool TryGetInstanceEventHead(RuntimeInstanceKey instance, out int slot)
        {
            slot = m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index) ? index.Head : -1;
            return slot != -1;
        }

        bool TryGetLatestInstanceEvent(RuntimeInstanceKey instance, out int slot)
        {
            slot = m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index) ? index.Latest : -1;
            return slot != -1;
        }

        void RefreshLatestInstanceEvent(RuntimeInstanceKey instance)
        {
            int latest = -1;
            if (TryGetInstanceEventHead(EventInstance(instance), out int slot))
            {
                for (; slot != -1; slot = m_NextInstanceEvent[slot])
                {
                    ref readonly RuntimeTraceEvent traceEvent = ref m_Events[slot].Event;
                    if (traceEvent.RuntimeInstance.Equals(instance) &&
                        (latest == -1 || traceEvent.Sequence > m_Events[latest].Event.Sequence))
                        latest = slot;
                }
            }
            if (latest == -1)
            {
                if (m_InstanceEvents.TryGetValue(instance, out InstanceEventIndex index))
                {
                    index.Latest = -1;
                    ReleaseInstanceEventIndex(instance, index);
                }
                return;
            }
            GetInstanceEventIndex(instance).Latest = latest;
        }

        void RefreshGraphMembership(RuntimeInstanceKey instance, string graphAuthoringId)
        {
            bool found = false;
            ulong sequence = 0;
            if (TryGetInstanceEventHead(EventInstance(instance), out int slot))
            {
                for (; slot != -1; slot = m_NextInstanceEvent[slot])
                {
                    ref readonly RuntimeDebugEventView item = ref m_Events[slot];
                    if (item.Source.IsValid && item.Event.RuntimeInstance.Equals(instance) &&
                        string.Equals(item.Source.GraphAuthoringId, graphAuthoringId, StringComparison.Ordinal))
                    {
                        found = true;
                        sequence = Math.Max(sequence, item.Event.Sequence);
                    }
                }
            }
            if (found)
                RegisterGraphInstance(graphAuthoringId, instance, sequence);
            else if (m_GraphInstances.TryGetValue(graphAuthoringId, out Dictionary<RuntimeInstanceKey, ulong> instances) &&
                     instances.Remove(instance) && !string.IsNullOrEmpty(graphAuthoringId))
                m_GraphInstanceRevisions[graphAuthoringId] = GetGraphInstanceRevision(graphAuthoringId) + 1;
        }

        void RefreshTimelinePlayback(RuntimeInstanceKey playback)
        {
            TimelinePlaybackSummaryBuilder previous = m_TimelinePlayback[playback];
            string previousGraph = previous.Provenance.SourceGraphAuthoringId.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(previous.TimelineAuthoringId))
            {
                var previousKey = new TimelineSourceKey(previous.TimelineAuthoringId, previousGraph);
                m_TimelinePlaybackMembership[previousKey].Remove(playback);
                m_TimelinePlaybackRevisions[previousKey] = GetTimelinePlaybackRevision(previous.TimelineAuthoringId, previousGraph) + 1;
            }
            if (!TryGetInstanceEventHead(playback, out int slot))
            {
                m_TimelinePlayback.Remove(playback);
                return;
            }
            var builder = new TimelinePlaybackSummaryBuilder(playback);
            for (; slot != -1; slot = m_NextInstanceEvent[slot])
                builder.Apply(in m_Events[slot]);
            m_TimelinePlayback[playback] = builder;
            RegisterTimelinePlayback(builder.TimelineAuthoringId, builder.Provenance.SourceGraphAuthoringId.ToString() ?? string.Empty, playback);
        }

        static RuntimeInstanceKey EventInstance(RuntimeInstanceKey instance) =>
            instance.Kind == RuntimeInstanceKind.TreeClip
                ? RuntimeInstanceKey.Timeline(instance.CharacterRuntimeId, instance.SourceOperationIndex,
                    instance.TimelinePlaybackId, instance.ActionInstanceId)
                : instance;

        void ApplyTimeline(in RuntimeDebugEventView eventView)
        {
            ref readonly RuntimeTraceEvent traceEvent = ref eventView.Event;
            RuntimeInstanceKey playback = EventInstance(traceEvent.RuntimeInstance);
            if (playback.Kind != RuntimeInstanceKind.TimelinePlayback)
                return;

            if (!m_TimelinePlayback.TryGetValue(playback, out TimelinePlaybackSummaryBuilder builder))
                builder = new TimelinePlaybackSummaryBuilder(playback);
            string previousTimelineAuthoringId = builder.TimelineAuthoringId;
            string previousGraphAuthoringId = builder.Provenance.SourceGraphAuthoringId.ToString() ?? string.Empty;
            builder.Apply(in eventView);
            m_TimelinePlayback[playback] = builder;
            string graphAuthoringId = builder.Provenance.SourceGraphAuthoringId.ToString() ?? string.Empty;
            if (!string.Equals(previousTimelineAuthoringId, builder.TimelineAuthoringId, StringComparison.Ordinal) ||
                !string.Equals(previousGraphAuthoringId, graphAuthoringId, StringComparison.Ordinal))
                RegisterTimelinePlayback(builder.TimelineAuthoringId, graphAuthoringId, playback);
            if (traceEvent.Kind is (RuntimeTraceEventKind.TimelineCompleted or
                RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped) &&
                !string.IsNullOrEmpty(builder.TimelineAuthoringId))
            {
                var revisionKey = new TimelineSourceKey(builder.TimelineAuthoringId, graphAuthoringId);
                m_TimelinePlaybackRevisions[revisionKey] = GetTimelinePlaybackRevision(
                    builder.TimelineAuthoringId, graphAuthoringId) + 1;
            }
        }

        ulong GetTimelineSequence(RuntimeInstanceKey playback)
        {
            return m_TimelinePlayback.TryGetValue(playback, out TimelinePlaybackSummaryBuilder builder)
                ? Math.Max(builder.LatestLogicTick, builder.LatestPresentationFrame)
                : 0;
        }

        void RegisterGraphInstance(string graphAuthoringId, RuntimeInstanceKey instance, ulong sequence)
        {
            if (!m_GraphInstances.TryGetValue(graphAuthoringId, out Dictionary<RuntimeInstanceKey, ulong> instances))
            {
                instances = new Dictionary<RuntimeInstanceKey, ulong>();
                m_GraphInstances.Add(graphAuthoringId, instances);
            }
            if (!instances.TryGetValue(instance, out ulong previousSequence))
            {
                    instances.Add(instance, sequence);
                if (!string.IsNullOrEmpty(graphAuthoringId))
                    m_GraphInstanceRevisions[graphAuthoringId] = GetGraphInstanceRevision(graphAuthoringId) + 1;
            }
            else if (sequence != previousSequence)
                instances[instance] = sequence;
        }

        sealed class InstanceSequenceOrder
        {
            internal readonly Comparison<RuntimeInstanceKey> Comparison;
            internal Dictionary<RuntimeInstanceKey, ulong> Sequences { get; set; }

            internal InstanceSequenceOrder() => Comparison = Compare;

            int Compare(RuntimeInstanceKey left, RuntimeInstanceKey right) =>
                Sequences[right].CompareTo(Sequences[left]);
        }

        sealed class TimelineInstanceSequenceOrder
        {
            readonly RuntimeDebugViewModel m_Owner;
            internal readonly Comparison<RuntimeInstanceKey> Comparison;

            internal TimelineInstanceSequenceOrder(RuntimeDebugViewModel owner)
            {
                m_Owner = owner;
                Comparison = Compare;
            }

            int Compare(RuntimeInstanceKey left, RuntimeInstanceKey right) =>
                m_Owner.GetTimelineSequence(right).CompareTo(m_Owner.GetTimelineSequence(left));
        }

        void RegisterTimelinePlayback(
            string timelineAuthoringId,
            string graphAuthoringId,
            RuntimeInstanceKey playback)
        {
            if (string.IsNullOrEmpty(timelineAuthoringId))
                return;
            var key = new TimelineSourceKey(timelineAuthoringId, graphAuthoringId);
            if (!m_TimelinePlaybackMembership.TryGetValue(key, out HashSet<RuntimeInstanceKey> playbacks))
            {
                playbacks = new HashSet<RuntimeInstanceKey>();
                m_TimelinePlaybackMembership.Add(key, playbacks);
            }
            if (playbacks.Add(playback))
                m_TimelinePlaybackRevisions[key] = GetTimelinePlaybackRevision(timelineAuthoringId, graphAuthoringId) + 1;
        }

        internal readonly struct TimelineSourceKey : IEquatable<TimelineSourceKey>
        {
            public TimelineSourceKey(string timelineAuthoringId, string graphAuthoringId)
            {
                TimelineAuthoringId = timelineAuthoringId ?? string.Empty;
                GraphAuthoringId = graphAuthoringId ?? string.Empty;
            }

            public string TimelineAuthoringId { get; }
            public string GraphAuthoringId { get; }
            public bool Equals(TimelineSourceKey other) =>
                string.Equals(TimelineAuthoringId, other.TimelineAuthoringId, StringComparison.Ordinal) &&
                string.Equals(GraphAuthoringId, other.GraphAuthoringId, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is TimelineSourceKey other && Equals(other);
            public override int GetHashCode() =>
                (TimelineAuthoringId?.GetHashCode() ?? 0) * 397 ^ (GraphAuthoringId?.GetHashCode() ?? 0);
        }

        struct ElementEventIndex
        {
            public int Head;
            public int Latest;
        }

        readonly struct ElementInstanceKey : IEquatable<ElementInstanceKey>
        {
            public ElementInstanceKey(RuntimeSourceElementKey source, InstanceEventIndex instance)
            {
                Source = source;
                Instance = instance;
            }

            public RuntimeSourceElementKey Source { get; }
            public InstanceEventIndex Instance { get; }
            public bool Equals(ElementInstanceKey other) => Source.Equals(other.Source) && ReferenceEquals(Instance, other.Instance);
            public override bool Equals(object obj) => obj is ElementInstanceKey other && Equals(other);
            public override int GetHashCode() => Source.GetHashCode() * 397 ^ Instance.GetHashCode();
        }

        struct TimelinePlaybackSummaryBuilder
        {
            ulong m_LogicTimeSequence;
            ulong m_VisualTimeSequence;
            ulong m_LifecycleSequence;
            ulong m_TerminalSequence;

            public TimelinePlaybackSummaryBuilder(RuntimeInstanceKey playback)
            {
                this = default;
                Playback = playback;
                TimelineAuthoringId = string.Empty;
                LifecycleStatus = string.Empty;
                TerminalCause = string.Empty;
            }

            public RuntimeInstanceKey Playback { get; }
            public string TimelineAuthoringId { get; private set; }
            public RuntimeTimelinePlaybackProvenance Provenance { get; private set; }
            public ulong LatestLogicTick { get; private set; }
            public ulong LatestPresentationFrame { get; private set; }
            public float LogicTime { get; private set; }
            public float VisualTime { get; private set; }
            public int LogicCycle { get; private set; }
            public int VisualCycle { get; private set; }
            public RuntimeTraceEventKind Lifecycle { get; private set; }
            public CanonicalTextValue LifecycleStatus { get; private set; }
            public RuntimeTraceEventKind Terminal { get; private set; }
            public CanonicalTextValue TerminalCause { get; private set; }

            public void Apply(in RuntimeDebugEventView eventView)
            {
                ref readonly RuntimeTraceEvent traceEvent = ref eventView.Event;
                RuntimeTracePayload payload = traceEvent.Payload;
                if (!string.IsNullOrEmpty(eventView.Source.TimelineAuthoringId))
                    TimelineAuthoringId = eventView.Source.TimelineAuthoringId;
                if (payload.TimelinePlayback.IsValid)
                    Provenance = payload.TimelinePlayback;

                if (traceEvent.Domain == RuntimeTraceDomain.Logic)
                    LatestLogicTick = Math.Max(LatestLogicTick, traceEvent.Position);
                else if (traceEvent.Domain == RuntimeTraceDomain.Presentation)
                    LatestPresentationFrame = Math.Max(LatestPresentationFrame, traceEvent.Position);

                if (traceEvent.Kind == RuntimeTraceEventKind.TimelineLogicTime &&
                    traceEvent.Sequence > m_LogicTimeSequence)
                {
                    m_LogicTimeSequence = traceEvent.Sequence;
                    LogicTime = payload.Time;
                    LogicCycle = payload.Cycle;
                }
                else if (traceEvent.Kind == RuntimeTraceEventKind.TimelineVisualTime &&
                         traceEvent.Sequence > m_VisualTimeSequence)
                {
                    m_VisualTimeSequence = traceEvent.Sequence;
                    VisualTime = payload.Time;
                    VisualCycle = payload.Cycle;
                }

                if (traceEvent.Kind is RuntimeTraceEventKind.TimelineCompleted or RuntimeTraceEventKind.TimelineCancelled or RuntimeTraceEventKind.TimelineStopped)
                {
                    if (traceEvent.Sequence > m_TerminalSequence)
                    {
                        m_TerminalSequence = traceEvent.Sequence;
                        Terminal = traceEvent.Kind;
                        TerminalCause = payload.Cause;
                        Lifecycle = traceEvent.Kind;
                        LifecycleStatus = payload.Status;
                    }
                }
                else if (m_TerminalSequence == 0 && traceEvent.Sequence > m_LifecycleSequence &&
                         traceEvent.Kind is (RuntimeTraceEventKind.TimelineRequested or RuntimeTraceEventKind.TimelineStarted or
                             RuntimeTraceEventKind.TimelineLogicTime or RuntimeTraceEventKind.TimelineVisualTime))
                {
                    m_LifecycleSequence = traceEvent.Sequence;
                    Lifecycle = traceEvent.Kind;
                    LifecycleStatus = payload.Status;
                }
            }

            public RuntimeTimelinePlaybackDebugSummary Build()
            {
                return new RuntimeTimelinePlaybackDebugSummary(
                    Playback,
                    Provenance,
                    LatestLogicTick,
                    LatestPresentationFrame,
                    LogicTime,
                    VisualTime,
                    LogicCycle,
                    VisualCycle,
                    Lifecycle,
                    LifecycleStatus,
                    Terminal,
                    TerminalCause);
            }
        }
    }

    internal readonly struct RuntimeDebugSourceMapSnapshot
    {
        readonly IDebugSourceMap m_SourceMap;

        RuntimeDebugSourceMapSnapshot(IDebugSourceMap sourceMap)
        {
            m_SourceMap = sourceMap;
            Revision = sourceMap.Revision;
        }

        public RuntimeContentRevision Revision { get; }
        public bool IsValid => m_SourceMap != null;
        internal int TimelineCount => IsValid ? m_SourceMap.TimelineCount : 0;
        internal int TrackCount => IsValid ? m_SourceMap.TrackCount : 0;
        public IReadOnlyList<RuntimeGraphInvocation> GraphInvocations => IsValid
            ? m_SourceMap.GraphInvocations : Array.Empty<RuntimeGraphInvocation>();
        public bool TryGetInvocation(CanonicalTextValue path, out RuntimeGraphInvocation invocation) =>
            m_SourceMap.TryGetInvocation(path, out invocation);

        public static RuntimeDebugSourceMapSnapshot Empty => default;

        public static RuntimeDebugSourceMapSnapshot Capture(IDebugSourceMap sourceMap) => new(sourceMap);

        public bool TryResolve(RuntimeSourceElementHandle handle, out RuntimeSourceElementKey source, out string sourceName)
        {
            if (TryGet(handle, out DebugSourceMapEntry entry))
            {
                source = entry.Source;
                sourceName = entry.DisplayName ?? string.Empty;
                return true;
            }
            source = default;
            sourceName = string.Empty;
            return false;
        }

        public bool TryGet(RuntimeSourceElementHandle handle, out DebugSourceMapEntry entry)
        {
            if (IsValid)
                return m_SourceMap.TryGet(handle, out entry);
            entry = default;
            return false;
        }

        public DebugSourceMapEntry Get(RuntimeSourceElementHandle handle) => m_SourceMap.Entries[handle.Value - 1];

        public RuntimeDebugTargetMatch Match(RuntimeDebugTargetRequest request)
        {
            return IsValid && m_SourceMap.TryGetHandle(request.Source, out _)
                ? RuntimeDebugTargetMatch.Exact
                : RuntimeDebugTargetMatch.SourceMissing;
        }
    }
}
