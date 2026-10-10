using System;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Diagnostics
{
    public readonly struct RuntimeContentRevision : IEquatable<RuntimeContentRevision>
    {
        public RuntimeContentRevision(string runtimeId, CanonicalTextValue sourceRevision, CanonicalTextValue contentHash)
        {
            RuntimeId = runtimeId ?? string.Empty;
            SourceRevisionText = sourceRevision;
            ContentHashText = contentHash;
        }

        public string RuntimeId { get; }
        public CanonicalTextValue SourceRevisionText { get; }
        public CanonicalTextValue ContentHashText { get; }
        public string SourceRevision => SourceRevisionText.ToString();
        public string ContentHash => ContentHashText.ToString();
        public bool IsValid => !string.IsNullOrEmpty(RuntimeId) && !SourceRevisionText.IsEmpty && !ContentHashText.IsEmpty;
        public void Retain() { SourceRevisionText.Retain(); ContentHashText.Retain(); }
        public void Release() { ContentHashText.Release(); SourceRevisionText.Release(); }

        public bool Equals(RuntimeContentRevision other)
        {
            return string.Equals(RuntimeId, other.RuntimeId, StringComparison.Ordinal) &&
                   SourceRevisionText == other.SourceRevisionText && ContentHashText == other.ContentHashText;
        }

        public override bool Equals(object obj) => obj is RuntimeContentRevision other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RuntimeId?.GetHashCode() ?? 0;
                hash = hash * 31 + SourceRevisionText.GetHashCode();
                hash = hash * 31 + ContentHashText.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"{RuntimeId}@{SourceRevision}:{ContentHash}";
    }

    public enum RuntimeSourceTargetKind
    {
        Source,
        Operation,
        Constant,
        StateSlot,
        Reference,
        Producer,
        CatalogEntry,
        BodyMotion,
        ControlModule,
        ControlState,
        ControlTransition
    }

    public readonly struct RuntimeSourceTarget : IEquatable<RuntimeSourceTarget>
    {
        public RuntimeSourceTarget(RuntimeSourceTargetKind kind, int index)
        {
            if (kind == RuntimeSourceTargetKind.Source && index != -1)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (kind != RuntimeSourceTargetKind.Source && index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Kind = kind;
            Index = index;
        }

        public RuntimeSourceTargetKind Kind { get; }
        public int Index { get; }
        public bool IsIndexedTarget => Kind != RuntimeSourceTargetKind.Source;
        public static RuntimeSourceTarget Source => new RuntimeSourceTarget(RuntimeSourceTargetKind.Source, -1);
        public bool Equals(RuntimeSourceTarget other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is RuntimeSourceTarget other && Equals(other);
        public override int GetHashCode() => (int)Kind * 397 ^ Index;
        public override string ToString() => IsIndexedTarget ? $"{Kind}:{Index}" : "Source";
    }

    public enum RuntimeSourceElementKind
    {
        None,
        Graph,
        Node,
        Edge,
        BlackboardDeclaration,
        Timeline,
        Track,
        Clip,
        TreeClip,
        BodyMotionProfile,
        Port
    }

    public readonly struct RuntimeSourceElementKey : IEquatable<RuntimeSourceElementKey>
    {
        readonly int m_HashCode;

        public RuntimeSourceElementKey(
            RuntimeSourceElementKind kind,
            string graphAuthoringId = "",
            string elementAuthoringId = "",
            string timelineAuthoringId = "",
            string trackAuthoringId = "",
            string clipAuthoringId = "",
            string portAuthoringId = "")
        {
            Kind = kind;
            GraphAuthoringId = graphAuthoringId ?? string.Empty;
            ElementAuthoringId = elementAuthoringId ?? string.Empty;
            TimelineAuthoringId = timelineAuthoringId ?? string.Empty;
            TrackAuthoringId = trackAuthoringId ?? string.Empty;
            ClipAuthoringId = clipAuthoringId ?? string.Empty;
            PortAuthoringId = portAuthoringId ?? string.Empty;
            unchecked
            {
                int hash = (int)kind;
                hash = hash * 31 + GraphAuthoringId.GetHashCode();
                hash = hash * 31 + ElementAuthoringId.GetHashCode();
                hash = hash * 31 + TimelineAuthoringId.GetHashCode();
                hash = hash * 31 + TrackAuthoringId.GetHashCode();
                hash = hash * 31 + ClipAuthoringId.GetHashCode();
                m_HashCode = hash * 31 + PortAuthoringId.GetHashCode();
            }
        }

        public RuntimeSourceElementKind Kind { get; }
        public string GraphAuthoringId { get; }
        public string ElementAuthoringId { get; }
        public string TimelineAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public string PortAuthoringId { get; }
        public bool IsValid => Kind != RuntimeSourceElementKind.None &&
                               (Kind != RuntimeSourceElementKind.Port ||
                                !string.IsNullOrEmpty(ElementAuthoringId) && !string.IsNullOrEmpty(PortAuthoringId)) &&
                               (!string.IsNullOrEmpty(GraphAuthoringId) ||
                                !string.IsNullOrEmpty(TimelineAuthoringId) ||
                                Kind == RuntimeSourceElementKind.BodyMotionProfile && !string.IsNullOrEmpty(ElementAuthoringId));

        public bool Equals(RuntimeSourceElementKey other)
        {
            return Kind == other.Kind &&
                   string.Equals(GraphAuthoringId, other.GraphAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(ElementAuthoringId, other.ElementAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(TimelineAuthoringId, other.TimelineAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(TrackAuthoringId, other.TrackAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(ClipAuthoringId, other.ClipAuthoringId, StringComparison.Ordinal) &&
                   string.Equals(PortAuthoringId, other.PortAuthoringId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is RuntimeSourceElementKey other && Equals(other);

        public override int GetHashCode() => m_HashCode;

        public static RuntimeSourceElementKey Graph(string graphId) => new RuntimeSourceElementKey(RuntimeSourceElementKind.Graph, graphId);
        public static RuntimeSourceElementKey Node(string graphId, string nodeId) => new RuntimeSourceElementKey(RuntimeSourceElementKind.Node, graphId, nodeId);
        public static RuntimeSourceElementKey Port(string graphId, string nodeId, string portId) =>
            new RuntimeSourceElementKey(RuntimeSourceElementKind.Port, graphId, nodeId, portAuthoringId: portId);
        public static RuntimeSourceElementKey Edge(string graphId, string edgeId) => new RuntimeSourceElementKey(RuntimeSourceElementKind.Edge, graphId, edgeId);
        public static RuntimeSourceElementKey Declaration(string graphId, string declarationId) => new RuntimeSourceElementKey(RuntimeSourceElementKind.BlackboardDeclaration, graphId, declarationId);
        public static RuntimeSourceElementKey Timeline(string timelineId, string graphId = "") =>
            new RuntimeSourceElementKey(RuntimeSourceElementKind.Timeline, graphAuthoringId: graphId, timelineAuthoringId: timelineId);
        public static RuntimeSourceElementKey Track(string timelineId, string trackId, string graphId = "") =>
            new RuntimeSourceElementKey(RuntimeSourceElementKind.Track, graphAuthoringId: graphId, timelineAuthoringId: timelineId, trackAuthoringId: trackId);
        public static RuntimeSourceElementKey Clip(string timelineId, string trackId, string clipId, bool treeClip = false, string graphId = "") =>
            new RuntimeSourceElementKey(treeClip ? RuntimeSourceElementKind.TreeClip : RuntimeSourceElementKind.Clip,
                graphAuthoringId: graphId, timelineAuthoringId: timelineId, trackAuthoringId: trackId, clipAuthoringId: clipId);
        public static RuntimeSourceElementKey BodyMotionProfile(string assetPath) =>
            new RuntimeSourceElementKey(RuntimeSourceElementKind.BodyMotionProfile, elementAuthoringId: assetPath);
    }

    public readonly struct RuntimeSourceElementHandle : IEquatable<RuntimeSourceElementHandle>
    {
        public RuntimeSourceElementHandle(int value, RuntimeSourceElementKind kind)
        {
            Value = value;
            Kind = kind;
        }

        public int Value { get; }
        public RuntimeSourceElementKind Kind { get; }
        public bool IsValid => Value > 0 && Kind != RuntimeSourceElementKind.None;
        public static RuntimeSourceElementHandle Invalid => default;
        public bool Equals(RuntimeSourceElementHandle other) => Value == other.Value && Kind == other.Kind;
        public override bool Equals(object obj) => obj is RuntimeSourceElementHandle other && Equals(other);
        public override int GetHashCode() => Value * 397 ^ (int)Kind;
        public override string ToString() => IsValid ? $"{Kind}:{Value}" : "Invalid";
    }

    public enum RuntimeInstanceKind
    {
        None,
        Character,
        Graph,
        RunnableActivation,
        StateActivation,
        TimelinePlayback,
        TreeClip,
        ControlModule,
        ControlState,
        ControlTransition,
        ActionInstance,
        SkillExecution
    }

    public readonly struct RuntimeInstanceKey : IEquatable<RuntimeInstanceKey>
    {
        readonly int m_HashCode;

        public RuntimeInstanceKey(
            RuntimeInstanceKind kind,
            Guid characterRuntimeId,
            Guid graphRuntimeId,
            string stateId,
            ulong activationGeneration,
            ulong timelinePlaybackId,
            int treeClipCycle,
            ulong actionInstanceId = 0,
            CanonicalTextValue callSiteId = default,
            ulong invocationGeneration = 0,
            int sourceOperationIndex = -1,
            int treeClipOperationIndex = -1)
        {
            Kind = kind;
            CharacterRuntimeId = characterRuntimeId;
            GraphRuntimeId = graphRuntimeId;
            StateId = stateId ?? string.Empty;
            ActivationGeneration = activationGeneration;
            TimelinePlaybackId = timelinePlaybackId;
            TreeClipCycle = treeClipCycle;
            ActionInstanceId = actionInstanceId;
            CallSiteId = callSiteId.IsNull ? CanonicalTextValue.Empty : callSiteId;
            InvocationGeneration = invocationGeneration;
            SourceOperationIndex = sourceOperationIndex;
            TreeClipOperationIndex = treeClipOperationIndex;
            unchecked
            {
                int hash = (int)kind;
                hash = hash * 31 + characterRuntimeId.GetHashCode();
                hash = hash * 31 + graphRuntimeId.GetHashCode();
                hash = hash * 31 + StateId.GetHashCode();
                hash = hash * 31 + activationGeneration.GetHashCode();
                hash = hash * 31 + timelinePlaybackId.GetHashCode();
                hash = hash * 31 + treeClipCycle;
                hash = hash * 31 + actionInstanceId.GetHashCode();
                hash = hash * 31 + invocationGeneration.GetHashCode();
                hash = hash * 31 + sourceOperationIndex;
                hash = hash * 31 + treeClipOperationIndex;
                m_HashCode = hash * 31 + CallSiteId.GetHashCode();
            }
        }

        public RuntimeInstanceKind Kind { get; }
        public Guid CharacterRuntimeId { get; }
        public Guid GraphRuntimeId { get; }
        public string StateId { get; }
        public ulong ActivationGeneration { get; }
        public ulong TimelinePlaybackId { get; }
        public int TreeClipCycle { get; }
        public ulong ActionInstanceId { get; }
        public CanonicalTextValue CallSiteId { get; }
        public ulong InvocationGeneration { get; }
        public int SourceOperationIndex { get; }
        public int TreeClipOperationIndex { get; }
        public bool IsValid => Kind != RuntimeInstanceKind.None && CharacterRuntimeId != Guid.Empty;

        public static RuntimeInstanceKey Character(Guid characterId) => new RuntimeInstanceKey(RuntimeInstanceKind.Character, characterId, Guid.Empty, string.Empty, 0, 0, -1);
        public static RuntimeInstanceKey Graph(Guid characterId, Guid graphId) => new RuntimeInstanceKey(RuntimeInstanceKind.Graph, characterId, graphId, string.Empty, 0, 0, -1);
        public static RuntimeInstanceKey Runnable(Guid characterId, Guid graphId, string nodeId, ulong generation) => new RuntimeInstanceKey(RuntimeInstanceKind.RunnableActivation, characterId, graphId, nodeId, generation, 0, -1);
        public static RuntimeInstanceKey State(Guid characterId, Guid graphId, string stateId, ulong generation) => new RuntimeInstanceKey(RuntimeInstanceKind.StateActivation, characterId, graphId, stateId, generation, 0, -1);
        public static RuntimeInstanceKey Timeline(Guid characterId, int sourceOperationIndex, ulong playbackId, ulong actionInstanceId = 0) => new RuntimeInstanceKey(RuntimeInstanceKind.TimelinePlayback, characterId, Guid.Empty, string.Empty, 0, playbackId, -1, actionInstanceId, string.Empty, 0, sourceOperationIndex);
        public static RuntimeInstanceKey TreeClip(Guid characterId, Guid graphId, int sourceOperationIndex, ulong playbackId, int cycle, ulong actionInstanceId = 0, int treeClipOperationIndex = -1) => new RuntimeInstanceKey(RuntimeInstanceKind.TreeClip, characterId, graphId, string.Empty, 0, playbackId, cycle, actionInstanceId, string.Empty, 0, sourceOperationIndex, treeClipOperationIndex);
        public static RuntimeInstanceKey ControlModule(Guid characterId, Guid moduleRuntimeId) => new RuntimeInstanceKey(RuntimeInstanceKind.ControlModule, characterId, moduleRuntimeId, string.Empty, 0, 0, -1);
        public static RuntimeInstanceKey ControlState(Guid characterId, Guid moduleRuntimeId, string stateId, ulong generation) => new RuntimeInstanceKey(RuntimeInstanceKind.ControlState, characterId, moduleRuntimeId, stateId, generation, 0, -1);
        public static RuntimeInstanceKey ControlTransition(Guid characterId, Guid moduleRuntimeId, string transitionId, ulong generation) => new RuntimeInstanceKey(RuntimeInstanceKind.ControlTransition, characterId, moduleRuntimeId, transitionId, generation, 0, -1);
        public static RuntimeInstanceKey ActionInstance(Guid characterId, Guid actionRuntimeId, string skillId, ulong instanceId, ulong generation) =>
            new RuntimeInstanceKey(RuntimeInstanceKind.ActionInstance, characterId, actionRuntimeId, skillId, generation, 0, -1, instanceId);
        public static RuntimeInstanceKey SkillExecution(Guid characterId, Guid actionRuntimeId, string skillId, ulong instanceId, CanonicalTextValue callSiteId, ulong generation, ulong invocationGeneration = 0) =>
            new RuntimeInstanceKey(RuntimeInstanceKind.SkillExecution, characterId, actionRuntimeId, skillId, generation, 0, -1, instanceId, callSiteId, invocationGeneration);

        public bool Equals(RuntimeInstanceKey other)
        {
            return Kind == other.Kind && CharacterRuntimeId.Equals(other.CharacterRuntimeId) && GraphRuntimeId.Equals(other.GraphRuntimeId) &&
                   ActivationGeneration == other.ActivationGeneration && TimelinePlaybackId == other.TimelinePlaybackId && TreeClipCycle == other.TreeClipCycle &&
                   ActionInstanceId == other.ActionInstanceId &&
                   InvocationGeneration == other.InvocationGeneration &&
                   SourceOperationIndex == other.SourceOperationIndex &&
                   TreeClipOperationIndex == other.TreeClipOperationIndex &&
                   string.Equals(StateId, other.StateId, StringComparison.Ordinal) &&
                   CallSiteId.Equals(other.CallSiteId);
        }

        public override bool Equals(object obj) => obj is RuntimeInstanceKey other && Equals(other);

        public override int GetHashCode() => m_HashCode;
        public void Retain() => CallSiteId.Retain();
        public void Release() => CallSiteId.Release();

        public override string ToString()
        {
            switch (Kind)
            {
                case RuntimeInstanceKind.Character:
                    return $"Character:{CharacterRuntimeId:N}";
                case RuntimeInstanceKind.Graph:
                    return $"Graph:{GraphRuntimeId:N}";
                case RuntimeInstanceKind.RunnableActivation:
                    return $"Runnable:{GraphRuntimeId:N}/{StateId}/{ActivationGeneration}";
                case RuntimeInstanceKind.StateActivation:
                    return $"State:{GraphRuntimeId:N}/{StateId}/{ActivationGeneration}";
                case RuntimeInstanceKind.TimelinePlayback:
                    return $"Timeline:{SourceOperationIndex}/{TimelinePlaybackId}/{ActionInstanceId}";
                case RuntimeInstanceKind.TreeClip:
                    return $"TreeClip:{SourceOperationIndex}/{TreeClipOperationIndex}/{TimelinePlaybackId}/{TreeClipCycle}/{GraphRuntimeId:N}/{ActionInstanceId}";
                case RuntimeInstanceKind.ControlModule:
                    return $"ControlModule:{GraphRuntimeId:N}";
                case RuntimeInstanceKind.ControlState:
                    return $"ControlState:{GraphRuntimeId:N}/{StateId}/{ActivationGeneration}";
                case RuntimeInstanceKind.ControlTransition:
                    return $"ControlTransition:{GraphRuntimeId:N}/{StateId}/{ActivationGeneration}";
                case RuntimeInstanceKind.ActionInstance:
                    return $"Action:{GraphRuntimeId:N}/{StateId}/{ActionInstanceId}/{ActivationGeneration}";
                case RuntimeInstanceKind.SkillExecution:
                    return $"Skill:{GraphRuntimeId:N}/{StateId}/{ActionInstanceId}/{CallSiteId}/{ActivationGeneration}/{InvocationGeneration}";
                default:
                    return "None";
            }
        }
    }

    public enum RuntimeTraceDomain
    {
        Logic,
        Presentation,
        Lifecycle
    }

    [Flags]
    public enum RuntimeTraceChannel
    {
        None = 0,
        Graph = 1 << 0,
        StateMachine = 1 << 1,
        Timeline = 1 << 2,
        Blackboard = 1 << 3,
        Animation = 1 << 4,
        Motion = 1 << 5,
        GameplayEffect = 1 << 6,
        Network = 1 << 7,
        FootPlacement = 1 << 8,
        Equipment = 1 << 9,
        Values = 1 << 10,
        All = Graph | StateMachine | Timeline | Blackboard | Animation | Motion | GameplayEffect | Network | FootPlacement | Equipment | Values
    }

    public enum RuntimeTraceEventKind
    {
        None,
        TargetAttached,
        TargetDetached,
        GraphCreated,
        GraphDestroyed,
        NodeEntered,
        NodeStatus,
        NodeCompleted,
        NodeStopRequested,
        NodeStopping,
        NodeStopped,
        NodeForceStopped,
        EdgeEvaluated,
        EdgeSelected,
        ConditionGraphEvaluated,
        StateTransitionEvaluated,
        StateTransitionSelected,
        StateScopeEntered,
        StateScopeExited,
        StateExitStarted,
        StateExitWaiting,
        TimelineRequested,
        TimelineStarted,
        TimelineLogicTime,
        TimelineVisualTime,
        TimelineCompleted,
        TimelineCancelled,
        TimelineStopped,
        TrackActive,
        ClipActive,
        TreeClipEntered,
        TreeClipUpdated,
        TreeClipExited,
        TreeClipDestroyed,
        BlackboardWritten,
        BlackboardCleared,
        BlackboardProjected,
        MotionContribution,
        MotionResolved,
        ActionSnapshot,
        ActionActivationRequested,
        ActionLifecycleTransitioned,
        ActionWindowSampled,
        ActionResultSubmitted,
        GameplayEffectLifecycle,
        GameplayAttributeChanged,
        GameplayTagChanged,
        GameplayCueSubmitted,
        AnimationPlaybackCompleted,
        AnimationPlaybackReleased,
        AnimationSelectionSubmitted,
        AnimationProducerSampled,
        AnimationPlaybackPending,
        AnimationPlaybackSelected,
        AnimationPlaybackRetained,
        AnimationPlaybackRetired,
        AnimationPhaseSync,
        MotionMatchingQuery,
        MotionMatchingTrajectory,
        MotionMatchingPoseHistory,
        MotionMatchingAdmission,
        MotionMatchingCandidateRejected,
        MotionMatchingSearchTraversal,
        MotionMatchingTopK,
        MotionMatchingPlan,
        MotionMatchingSelection,
        MotionMatchingPoseSource,
        MotionMatchingReset,
        PresentationInterpolated,
        LocomotionPresentation,
        CameraSnapshot,
        CameraRequest,
        CameraShakeRequest,
        SimulationTick,
        SimulationRestore,
        SimulationEvaluate,
        SimulationWorldBatch,
        SimulationFinalize,
        SimulationStatePublished,
        SimulationCommit,
        SimulationFailure,
        SimulationNetworkModel,
        FootPlacementSnapshot,
        EquipmentSnapshot,
        EquipmentChange,
        EquipmentVisual,
        MotionMatchingFrame,
        ValueSampled,
        ValueSamplingLimited,
        NodeRunning,
        NodeWaiting,
        SimulationCheckpointCaptured,
        LoopIterationEntered,
        LoopIterationCompleted,
        BlackboardSnapshot,
        TraceSamplingLimited
    }

    public enum DebugValueKind
    {
        None,
        Boolean,
        Int64,
        UInt64,
        Double,
        String,
        Guid,
        Vector2,
        Vector3,
        Quaternion,
        TypeOnly,
        FixedVector2,
        FixedVector3,
        FixedActionTargetSnapshot
    }

    public readonly struct DebugValueSnapshot
    {
        public DebugValueSnapshot(DebugValueKind kind, bool boolean, long signed, ulong unsigned, double number, CanonicalTextValue text, Vector4 vector)
        {
            Kind = kind;
            Boolean = boolean;
            Signed = signed;
            Unsigned = unsigned;
            Number = number;
            Text = text.IsNull ? CanonicalTextValue.Empty : text;
            Vector = vector;
        }

        public DebugValueKind Kind { get; }
        public bool Boolean { get; }
        public long Signed { get; }
        public ulong Unsigned { get; }
        public double Number { get; }
        public CanonicalTextValue Text { get; }
        public Vector4 Vector { get; }

        public static DebugValueSnapshot Capture(object value)
        {
            if (value == null)
                return default;
            if (value is bool boolean)
                return new DebugValueSnapshot(DebugValueKind.Boolean, boolean, 0, 0, 0, string.Empty, default);
            if (value is sbyte || value is short || value is int || value is long)
                return new DebugValueSnapshot(DebugValueKind.Int64, false, Convert.ToInt64(value), 0, 0, string.Empty, default);
            if (value is byte || value is ushort || value is uint || value is ulong)
                return new DebugValueSnapshot(DebugValueKind.UInt64, false, 0, Convert.ToUInt64(value), 0, string.Empty, default);
            if (value is float || value is double || value is decimal)
                return new DebugValueSnapshot(DebugValueKind.Double, false, 0, 0, Convert.ToDouble(value), string.Empty, default);
            if (value is string text)
                return new DebugValueSnapshot(DebugValueKind.String, false, 0, 0, 0, text, default);
            if (value is Guid guid)
                return new DebugValueSnapshot(DebugValueKind.Guid, false, 0, 0, 0, guid.ToString("D"), default);
            if (value is Vector2 vector2)
                return new DebugValueSnapshot(DebugValueKind.Vector2, false, 0, 0, 0, string.Empty, new Vector4(vector2.x, vector2.y, 0, 0));
            if (value is Vector3 vector3)
                return new DebugValueSnapshot(DebugValueKind.Vector3, false, 0, 0, 0, string.Empty, new Vector4(vector3.x, vector3.y, vector3.z, 0));
            if (value is Quaternion quaternion)
                return new DebugValueSnapshot(DebugValueKind.Quaternion, false, 0, 0, 0, string.Empty, new Vector4(quaternion.x, quaternion.y, quaternion.z, quaternion.w));
            Type type = value.GetType();
            return new DebugValueSnapshot(DebugValueKind.TypeOnly, false, 0, 0, 0, type.FullName ?? type.Name, default);
        }

        public void Retain() => Text.Retain();
        public void Release() => Text.Release();

        public string DisplayValue()
        {
            switch (Kind)
            {
                case DebugValueKind.Boolean: return Boolean ? "true" : "false";
                case DebugValueKind.Int64: return Signed.ToString();
                case DebugValueKind.UInt64: return Unsigned.ToString();
                case DebugValueKind.Double: return Number.ToString("0.###");
                case DebugValueKind.String:
                case DebugValueKind.Guid:
                case DebugValueKind.TypeOnly: return Text.ToString();
                case DebugValueKind.FixedVector2:
                case DebugValueKind.Vector2: return $"({Vector.x:0.###}, {Vector.y:0.###})";
                case DebugValueKind.FixedVector3:
                case DebugValueKind.Vector3: return $"({Vector.x:0.###}, {Vector.y:0.###}, {Vector.z:0.###})";
                case DebugValueKind.Quaternion: return $"({Vector.x:0.###}, {Vector.y:0.###}, {Vector.z:0.###}, {Vector.w:0.###})";
                case DebugValueKind.FixedActionTargetSnapshot:
                    ulong targetZ = ((ulong)Number << 24) | (ulong)Vector.x;
                    ulong targetYaw = (ulong)Vector.y | ((ulong)Vector.z << 24) | ((ulong)Vector.w << 48);
                    const double fixedScale = 4294967296d;
                    return $"{Text} · ({Signed / fixedScale:0.###}, {unchecked((long)Unsigned) / fixedScale:0.###}, {unchecked((long)targetZ) / fixedScale:0.###}) · {unchecked((long)targetYaw) / fixedScale:0.###}°";
                default: return string.Empty;
            }
        }
    }

    public readonly struct RuntimeTimelinePlaybackProvenance
    {
        public RuntimeTimelinePlaybackProvenance(
            CanonicalTextValue sourceGraphAuthoringId,
            CanonicalTextValue sourceNodeAuthoringId,
            Guid sourceGraphRuntimeId,
            ulong sourceActivationGeneration,
            string stateMachineGraphAuthoringId,
            string stateId,
            Guid stateMachineGraphRuntimeId,
            ulong stateActivationGeneration,
            string sourceName,
            CanonicalTextValue sourceInvocationPath = default,
            int sourceOperationIndex = -1,
            ulong skillExecutionGeneration = 0)
        {
            SourceGraphAuthoringId = sourceGraphAuthoringId.IsNull ? CanonicalTextValue.Empty : sourceGraphAuthoringId;
            SourceNodeAuthoringId = sourceNodeAuthoringId.IsNull ? CanonicalTextValue.Empty : sourceNodeAuthoringId;
            SourceGraphRuntimeId = sourceGraphRuntimeId;
            SourceActivationGeneration = sourceActivationGeneration;
            StateMachineGraphAuthoringId = stateMachineGraphAuthoringId ?? string.Empty;
            StateId = stateId ?? string.Empty;
            StateMachineGraphRuntimeId = stateMachineGraphRuntimeId;
            StateActivationGeneration = stateActivationGeneration;
            SourceName = sourceName ?? string.Empty;
            SourceInvocationPath = sourceInvocationPath.IsNull ? CanonicalTextValue.Empty : sourceInvocationPath;
            SourceOperationIndex = sourceOperationIndex;
            SkillExecutionGeneration = skillExecutionGeneration;
        }

        public CanonicalTextValue SourceGraphAuthoringId { get; }
        public CanonicalTextValue SourceNodeAuthoringId { get; }
        public Guid SourceGraphRuntimeId { get; }
        public ulong SourceActivationGeneration { get; }
        public string StateMachineGraphAuthoringId { get; }
        public string StateId { get; }
        public Guid StateMachineGraphRuntimeId { get; }
        public ulong StateActivationGeneration { get; }
        public string SourceName { get; }
        public CanonicalTextValue SourceInvocationPath { get; }
        public int SourceOperationIndex { get; }
        public ulong SkillExecutionGeneration { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(SourceGraphAuthoringId) &&
                               !CanonicalTextValue.IsNullOrEmpty(SourceNodeAuthoringId) &&
                               SourceActivationGeneration != 0 &&
                               (SourceGraphRuntimeId != Guid.Empty || HasProgramInvocation);
        public bool HasProgramInvocation => !SourceInvocationPath.IsEmpty &&
                                            SourceOperationIndex >= 0 &&
                                            SkillExecutionGeneration != 0;
        public void Retain() { SourceGraphAuthoringId.Retain(); SourceNodeAuthoringId.Retain(); SourceInvocationPath.Retain(); }
        public void Release() { SourceInvocationPath.Release(); SourceNodeAuthoringId.Release(); SourceGraphAuthoringId.Release(); }
        public bool HasStateActivation => !string.IsNullOrEmpty(StateMachineGraphAuthoringId) &&
                                          !string.IsNullOrEmpty(StateId) &&
                                          StateMachineGraphRuntimeId != Guid.Empty &&
                                          StateActivationGeneration != 0;
    }

    public struct RuntimeFootPlacementFootTraceSnapshot
    {
        public bool IsAvailable;
        public string Side;
        public string State;
        public string RejectReason;
        public string StepSource;
        public ulong LandingEventIdentity;
        public ulong TrajectoryGeneration;
        public float LandingConfidence;
        public float TimeToLandingSeconds;
        public Vector3 RootLocalLanding;
        public Vector3 CurrentAnimatedSole;
        public Vector3 RawLanding;
        public bool QueryAvailable;
        public string QueryShape;
        public string QueryPurpose;
        public Vector3 QueryOrigin;
        public Vector3 QueryDirection;
        public float QueryRadius;
        public float QueryMaximumDistance;
        public int QueryLayerMask;
        public float QueryMinimumGroundNormalDot;
        public bool Accepted;
        public int SurfaceIdentity;
        public Vector3 LandingPoint;
        public Vector3 LandingNormal;
        public float QueryDistance;
    }

    public struct RuntimeFootPlacementTraceSnapshot
    {
        public bool IsAvailable;
        public ulong FrameSequence;
        public ulong CompletionIdentity;
        public RuntimeFootPlacementFootTraceSnapshot Left;
        public RuntimeFootPlacementFootTraceSnapshot Right;
    }

    public enum RuntimeTraceDetailKind { Text, GameplayEffectFailure, GameplayTagCounts, EquipmentChange, ControlFlow, Motion, AnimationCommand, AbilityRuleSource, CharacterControl }

    public struct RuntimeTracePayload
    {
        public CanonicalTextValue Status;
        public CanonicalTextValue Name;
        public CanonicalTextValue Detail;
        public CanonicalTextValue Cause;
        public string SourceClockId;
        public string SourceTickKind;
        public CanonicalTextValue AnimationChannelId;
        public CanonicalTextValue OwnerId;
        public CanonicalTextValue RelatedElementId;
        public string SkillId;
        public ulong InputSequence;
        public string CharacterStateHash;
        public string WorldHash;
        public ulong ActionInstanceId;
        public CanonicalTextValue CallSiteId;
        public ulong ActivationGeneration;
        public ulong SkillExecutionGeneration;
        public string RuleSourceSkillId;
        public ulong RuleSourceInstanceId;
        public ulong RuleSourceSkillExecutionGeneration;
        public ulong RuleSourceSegmentGeneration;
        public ulong GraphInvocationGeneration;
        public ulong ParentInvocationGeneration;
        public CanonicalTextValue ParentInvocationPath;
        public ulong StartTick;
        public ulong EndTick;
        public ulong Revision;
        public int ActionPhase;
        public int ActionState;
        public int ActionResult;
        public int LifecycleOperation;
        public int StackCount;
        public RuntimeTraceDetailKind DetailKind;
        public int BeforeCount;
        public float Time;
        public float SecondaryTime;
        public float NormalizedTime;
        public float Weight;
        public float FinalWeight;
        public int Priority;
        public int Cycle;
        public int LoopIteration;
        public int BlackboardStateSlot;
        public int BlackboardScope;
        public int BlackboardOwnerIndex;
        public ulong BlackboardOwnerGeneration;
        public int BlackboardLifetime;
        public bool BlackboardIsActive;
        public int TrackIndex;
        public int ClipIndex;
        public bool Flag;
        public DebugValueSnapshot Value;
        public RuntimeTimelinePlaybackProvenance TimelinePlayback;
        public RuntimeFootPlacementTraceSnapshot FootPlacement;
        public SimulationMotionTraceDetail MotionDetail;
        public EventId SourceEventId;
        [NonSerialized] public AnimationClip SourceAnimationClip;

        public void Retain()
        {
            Status.Retain();
            Name.Retain();
            Detail.Retain();
            Cause.Retain();
            AnimationChannelId.Retain();
            OwnerId.Retain();
            RelatedElementId.Retain();
            CallSiteId.Retain();
            ParentInvocationPath.Retain();
            Value.Retain();
            TimelinePlayback.Retain();
        }

        public void Release()
        {
            Status.Release();
            Name.Release();
            Detail.Release();
            Cause.Release();
            AnimationChannelId.Release();
            OwnerId.Release();
            RelatedElementId.Release();
            CallSiteId.Release();
            ParentInvocationPath.Release();
            Value.Release();
            TimelinePlayback.Release();
        }
    }

    public readonly struct RuntimeTraceEvent
    {
        public RuntimeTraceEvent(
            Guid sessionId,
            RuntimeContentRevision contentRevision,
            ulong runtimeEpoch,
            Guid executionBranchId,
            RuntimeTraceDomain domain,
            RuntimeTraceChannel channel,
            ulong position,
            double timeSeconds,
            ulong sequence,
            RuntimeInstanceKey runtimeInstance,
            RuntimeSourceElementHandle source,
            RuntimeTraceEventKind kind,
            in RuntimeTracePayload payload)
        {
            SessionId = sessionId;
            ContentRevision = contentRevision;
            RuntimeEpoch = runtimeEpoch;
            ExecutionBranchId = executionBranchId;
            Domain = domain;
            Channel = channel;
            Position = position;
            TimeSeconds = timeSeconds;
            Sequence = sequence;
            RuntimeInstance = runtimeInstance;
            Source = source;
            Kind = kind;
            m_Payload = payload;
        }

        public Guid SessionId { get; }
        public RuntimeContentRevision ContentRevision { get; }
        public ulong RuntimeEpoch { get; }
        public Guid ExecutionBranchId { get; }
        public RuntimeTraceDomain Domain { get; }
        public RuntimeTraceChannel Channel { get; }
        public ulong Position { get; }
        public double TimeSeconds { get; }
        public ulong Sequence { get; }
        public RuntimeInstanceKey RuntimeInstance { get; }
        public RuntimeSourceElementHandle Source { get; }
        public RuntimeTraceEventKind Kind { get; }
        readonly RuntimeTracePayload m_Payload;
        public RuntimeTracePayload Payload => m_Payload;
        public CanonicalTextValue GetStatus() => m_Payload.Status;
        public CanonicalTextValue GetDetail() => m_Payload.Detail;
        public int GetCycle() => m_Payload.Cycle;
        public ulong GetActionInstanceId() => m_Payload.ActionInstanceId;
        public int GetLifecycleOperation() => m_Payload.LifecycleOperation;
        public CanonicalTextValue GetCause() => m_Payload.Cause;
        public CanonicalTextValue GetParentInvocationPath() => m_Payload.ParentInvocationPath;
        public ulong GetParentInvocationGeneration() => m_Payload.ParentInvocationGeneration;
        public void Retain()
        {
            ContentRevision.Retain();
            RuntimeInstance.Retain();
            m_Payload.Retain();
        }
        public void Release()
        {
            ContentRevision.Release();
            RuntimeInstance.Release();
            m_Payload.Release();
        }
        public RuntimeTraceEvent CaptureDisplayText()
        {
            RuntimeTracePayload payload = m_Payload;
            payload.Status = payload.Status.ToString();
            payload.Name = payload.Name.ToString();
            payload.Detail = payload.Detail.ToString();
            payload.Cause = payload.Cause.ToString();
            payload.AnimationChannelId = payload.AnimationChannelId.ToString();
            payload.OwnerId = payload.OwnerId.ToString();
            payload.RelatedElementId = payload.RelatedElementId.ToString();
            payload.CallSiteId = payload.CallSiteId.ToString();
            payload.ParentInvocationPath = payload.ParentInvocationPath.ToString();
            DebugValueSnapshot value = payload.Value;
            payload.Value = new DebugValueSnapshot(value.Kind, value.Boolean, value.Signed,
                value.Unsigned, value.Number, value.Text.ToString(), value.Vector);
            RuntimeTimelinePlaybackProvenance provenance = payload.TimelinePlayback;
            payload.TimelinePlayback = new RuntimeTimelinePlaybackProvenance(provenance.SourceGraphAuthoringId.ToString(),
                provenance.SourceNodeAuthoringId.ToString(), provenance.SourceGraphRuntimeId, provenance.SourceActivationGeneration,
                provenance.StateMachineGraphAuthoringId, provenance.StateId, provenance.StateMachineGraphRuntimeId,
                provenance.StateActivationGeneration, provenance.SourceName, provenance.SourceInvocationPath.ToString(),
                provenance.SourceOperationIndex, provenance.SkillExecutionGeneration);
            RuntimeInstanceKey instance = RuntimeInstance;
            var displayInstance = new RuntimeInstanceKey(instance.Kind, instance.CharacterRuntimeId,
                instance.GraphRuntimeId, instance.StateId, instance.ActivationGeneration, instance.TimelinePlaybackId,
                instance.TreeClipCycle, instance.ActionInstanceId, instance.CallSiteId.ToString(),
                instance.InvocationGeneration, instance.SourceOperationIndex, instance.TreeClipOperationIndex);
            return new RuntimeTraceEvent(SessionId, ContentRevision, RuntimeEpoch, ExecutionBranchId,
                Domain, Channel, Position, TimeSeconds, Sequence, displayInstance, Source, Kind, in payload);
        }

        public Guid GetParentGraphRuntimeId() => m_Payload.TimelinePlayback.IsValid &&
            m_Payload.ParentInvocationPath.Equals(m_Payload.TimelinePlayback.SourceInvocationPath)
            ? m_Payload.TimelinePlayback.SourceGraphRuntimeId : RuntimeInstance.GraphRuntimeId;
    }

    public interface IRuntimeDiagnosticsContextSource
    {
        RuntimeDiagnosticsContext RuntimeDiagnostics { get; }
    }

    public interface IRuntimeDebugSourceMap
    {
        RuntimeContentRevision Revision { get; }
        IDebugSourceMap SourceMap { get; }
    }
}
