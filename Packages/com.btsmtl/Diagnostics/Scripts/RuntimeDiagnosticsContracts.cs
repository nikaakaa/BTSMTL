using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#else
using System.Numerics;
#endif

namespace BTSMTL.Diagnostics
{
    public readonly struct RuntimeContentRevision : IEquatable<RuntimeContentRevision>
    {
        public RuntimeContentRevision(string runtimeId, string sourceRevision, string contentHash)
        {
            RuntimeId = runtimeId ?? string.Empty;
            SourceRevision = sourceRevision;
            ContentHash = contentHash;
        }

        public string RuntimeId { get; }
        public string SourceRevision { get; }
        public string ContentHash { get; }
        public bool IsValid => !string.IsNullOrEmpty(RuntimeId) && !string.IsNullOrEmpty(SourceRevision) && !string.IsNullOrEmpty(ContentHash);

        public bool Equals(RuntimeContentRevision other)
        {
            return string.Equals(RuntimeId, other.RuntimeId, StringComparison.Ordinal) &&
                   string.Equals(SourceRevision, other.SourceRevision, StringComparison.Ordinal) &&
                   string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is RuntimeContentRevision other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RuntimeId?.GetHashCode() ?? 0;
                hash = hash * 31 + (SourceRevision?.GetHashCode() ?? 0);
                hash = hash * 31 + (ContentHash?.GetHashCode() ?? 0);
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
        SkillExecution,
        HostSubject
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
            string callSiteId = "",
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
            CallSiteId = callSiteId ?? string.Empty;
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
        public string CallSiteId { get; }
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
        public static RuntimeInstanceKey SkillExecution(Guid characterId, Guid actionRuntimeId, string skillId, ulong instanceId, string callSiteId, ulong generation, ulong invocationGeneration = 0) =>
            new RuntimeInstanceKey(RuntimeInstanceKind.SkillExecution, characterId, actionRuntimeId, skillId, generation, 0, -1, instanceId, callSiteId, invocationGeneration);
        // 宿主业务对象：subjectKind 为宿主常量，subjectId 为该对象在宿主内的稳定编号。
        public static RuntimeInstanceKey HostSubject(Guid characterId, string subjectKind, ulong subjectId, ulong actionInstanceId = 0) =>
            new RuntimeInstanceKey(RuntimeInstanceKind.HostSubject, characterId, Guid.Empty, subjectKind, subjectId, 0, -1, actionInstanceId);

        public bool Equals(RuntimeInstanceKey other)
        {
            return Kind == other.Kind && CharacterRuntimeId.Equals(other.CharacterRuntimeId) && GraphRuntimeId.Equals(other.GraphRuntimeId) &&
                   ActivationGeneration == other.ActivationGeneration && TimelinePlaybackId == other.TimelinePlaybackId && TreeClipCycle == other.TreeClipCycle &&
                   ActionInstanceId == other.ActionInstanceId &&
                   InvocationGeneration == other.InvocationGeneration &&
                   SourceOperationIndex == other.SourceOperationIndex &&
                   TreeClipOperationIndex == other.TreeClipOperationIndex &&
                   string.Equals(StateId, other.StateId, StringComparison.Ordinal) &&
                   string.Equals(CallSiteId, other.CallSiteId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is RuntimeInstanceKey other && Equals(other);

        public override int GetHashCode() => m_HashCode;
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
                case RuntimeInstanceKind.SkillExecution:
                    return $"Skill:{GraphRuntimeId:N}/{StateId}/{ActionInstanceId}/{CallSiteId}/{ActivationGeneration}/{InvocationGeneration}";
                case RuntimeInstanceKind.HostSubject:
                    return $"{StateId}:{ActivationGeneration}/{ActionInstanceId}";
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
        Values = 1 << 10,
        // 宿主经 RuntimeHostTraceChannels 登记的全部通道位。
        Host = 0x7FFF0000,
        All = Graph | StateMachine | Timeline | Blackboard | Values | Host
    }

    // 宿主通道以稳定编号占用 Host 位段；编号写入预览事件文件，只追加不复用。
    public static class RuntimeHostTraceChannels
    {
        public const int FirstBit = 16;
        public const int Capacity = 15;
        static readonly string[] s_Names = new string[Capacity];

        public static RuntimeTraceChannel Register(int hostId, string name)
        {
            if ((uint)hostId >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(hostId));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("宿主诊断通道必须有名称。", nameof(name));
            lock (s_Names)
            {
                string existing = s_Names[hostId];
                if (existing != null && existing != name)
                    throw new InvalidOperationException($"宿主诊断通道 {hostId} 已登记为 {existing}。");
                s_Names[hostId] = name;
            }
            return (RuntimeTraceChannel)(1 << (FirstBit + hostId));
        }

        public static string Name(RuntimeTraceChannel channel)
        {
            int bits = (int)(channel & RuntimeTraceChannel.Host);
            if (bits == 0 || (bits & (bits - 1)) != 0)
                throw new ArgumentException("只能查询单个宿主通道。", nameof(channel));
            int hostId = 0;
            while ((bits >> (FirstBit + hostId)) != 1)
                hostId++;
            return s_Names[hostId] ?? $"Host#{hostId}";
        }
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
        SimulationTick,
        SimulationRestore,
        SimulationEvaluate,
        SimulationWorldBatch,
        SimulationFinalize,
        SimulationStatePublished,
        SimulationCommit,
        SimulationFailure,
        SimulationNetworkModel,
        ValueSampled,
        ValueSamplingLimited,
        NodeRunning,
        NodeWaiting,
        SimulationCheckpointCaptured,
        LoopIterationEntered,
        LoopIterationCompleted,
        BlackboardSnapshot,
        TraceSamplingLimited,
        CaptureStateSnapshot
    }

    // 宿主事件以稳定编号登记；编号进入预览事件文件，宿主不得复用或改义。
    public static class RuntimeHostTraceKinds
    {
        public const int First = 1 << 16;
        public const int Capacity = 256;
        static readonly string[] s_Names = new string[Capacity];
        static readonly RuntimeDiagnosticsCaptureDetail[] s_Details = new RuntimeDiagnosticsCaptureDetail[Capacity];

        public static RuntimeTraceEventKind Register(int hostId, string name, RuntimeDiagnosticsCaptureDetail detail)
        {
            if ((uint)hostId >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(hostId));
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("宿主诊断事件必须有名称。", nameof(name));
            if (detail == RuntimeDiagnosticsCaptureDetail.None)
                throw new ArgumentOutOfRangeException(nameof(detail));
            lock (s_Names)
            {
                string existing = s_Names[hostId];
                if (existing != null && (existing != name || s_Details[hostId] != detail))
                    throw new InvalidOperationException($"宿主诊断事件 {hostId} 已登记为 {existing}。");
                s_Names[hostId] = name;
                s_Details[hostId] = detail;
            }
            return (RuntimeTraceEventKind)(First + hostId);
        }

        public static bool IsHost(RuntimeTraceEventKind kind) => (uint)((int)kind - First) < Capacity;

        // 读取已保存文件时宿主类型可能尚未初始化，此时只显示编号。
        public static string Name(RuntimeTraceEventKind kind) =>
            s_Names[(int)kind - First] ?? $"Host#{(int)kind - First}";

        public static RuntimeDiagnosticsCaptureDetail CaptureDetail(RuntimeTraceEventKind kind)
        {
            RuntimeDiagnosticsCaptureDetail detail = s_Details[(int)kind - First];
            return detail != RuntimeDiagnosticsCaptureDetail.None
                ? detail
                : throw new InvalidOperationException($"宿主诊断事件 {(int)kind - First} 未登记。");
        }
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
        FPVector2,
        FPVector3,
        FPActionTargetSnapshot,
        FPScalar,
        FPYaw,
        FPQuaternion
    }

    public readonly struct DebugValueSnapshot
    {
        public DebugValueSnapshot(DebugValueKind kind, bool boolean, long signed, ulong unsigned, double number, string text, Vector4 vector)
        {
            Kind = kind;
            Boolean = boolean;
            Signed = signed;
            Unsigned = unsigned;
            Number = number;
            Text = text ?? string.Empty;
            Vector = vector;
        }

        public DebugValueKind Kind { get; }
        public bool Boolean { get; }
        public long Signed { get; }
        public ulong Unsigned { get; }
        public double Number { get; }
        public string Text { get; }
        public Vector4 Vector { get; }

        public static DebugValueSnapshot FromFPScalar(long raw) =>
            new DebugValueSnapshot(DebugValueKind.FPScalar, false, raw, 0, 0, string.Empty, default);

        public static DebugValueSnapshot FromFPYaw(long raw) =>
            new DebugValueSnapshot(DebugValueKind.FPYaw, false, raw, 0, 0, string.Empty, default);

        public static DebugValueSnapshot FromFPVector2(long x, long y) =>
            new DebugValueSnapshot(DebugValueKind.FPVector2, false, x, unchecked((ulong)y), 0, string.Empty, default);

        public static DebugValueSnapshot FromFPVector3(long x, long y, long z) =>
            PackFixedPoint(DebugValueKind.FPVector3, x, y, z, 0, string.Empty);

        public static DebugValueSnapshot FromFPQuaternion(long x, long y, long z, long w) =>
            PackFixedPoint(DebugValueKind.FPQuaternion, x, y, z, w, string.Empty);

        public static DebugValueSnapshot FromFPSpatialIdentity(string identity, long x, long y, long z, long yaw) =>
            PackFixedPoint(DebugValueKind.FPActionTargetSnapshot, x, y, z, yaw, identity);

        static DebugValueSnapshot PackFixedPoint(DebugValueKind kind, long x, long y, long z, long w, string text)
        {
            ulong rawZ = unchecked((ulong)z);
            ulong rawW = unchecked((ulong)w);
            const ulong mask = (1UL << 24) - 1;
            // float 只保存 24 位片段，double 只保存 40 位片段，完整原值不会经过浮点舍入。
            return new DebugValueSnapshot(kind, false, x, unchecked((ulong)y), rawZ >> 24, text,
                new Vector4(rawZ & mask, rawW & mask, (rawW >> 24) & mask, rawW >> 48));
        }

        public long ReadFPScalar() => Signed;
        public long ReadFPYaw() => Signed;

        public void ReadFPVector2(out long x, out long y)
        {
            x = Signed;
            y = unchecked((long)Unsigned);
        }

        public void ReadFPVector3(out long x, out long y, out long z)
        {
            ReadFPVector2(out x, out y);
            z = unchecked((long)(((ulong)Number << 24) | (ulong)Vector[0]));
        }

        public void ReadFPQuaternion(out long x, out long y, out long z, out long w)
        {
            ReadFPVector3(out x, out y, out z);
            w = unchecked((long)((ulong)Vector[1] | ((ulong)Vector[2] << 24) | ((ulong)Vector[3] << 48)));
        }

        public void ReadFPSpatialIdentity(out string identity, out long x, out long y, out long z, out long yaw)
        {
            identity = Text;
            ReadFPQuaternion(out x, out y, out z, out yaw);
        }

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
                return new DebugValueSnapshot(DebugValueKind.Vector2, false, 0, 0, 0, string.Empty, new Vector4(vector2[0], vector2[1], 0, 0));
            if (value is Vector3 vector3)
                return new DebugValueSnapshot(DebugValueKind.Vector3, false, 0, 0, 0, string.Empty, new Vector4(vector3[0], vector3[1], vector3[2], 0));
            if (value is Quaternion quaternion)
#if UNITY_5_3_OR_NEWER
                return new DebugValueSnapshot(DebugValueKind.Quaternion, false, 0, 0, 0, string.Empty, new Vector4(quaternion.x, quaternion.y, quaternion.z, quaternion.w));
#else
                return new DebugValueSnapshot(DebugValueKind.Quaternion, false, 0, 0, 0, string.Empty, new Vector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W));
#endif
            Type type = value.GetType();
            return new DebugValueSnapshot(DebugValueKind.TypeOnly, false, 0, 0, 0, type.FullName ?? type.Name, default);
        }

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
                case DebugValueKind.TypeOnly: return Text;
                case DebugValueKind.Vector2: return $"({Vector[0]:0.###}, {Vector[1]:0.###})";
                case DebugValueKind.Vector3: return $"({Vector[0]:0.###}, {Vector[1]:0.###}, {Vector[2]:0.###})";
                case DebugValueKind.Quaternion: return $"({Vector[0]:0.###}, {Vector[1]:0.###}, {Vector[2]:0.###}, {Vector[3]:0.###})";
                case DebugValueKind.FPScalar: return FormatFixedPoint(Signed);
                case DebugValueKind.FPYaw: return FormatFixedPoint(Signed) + "°";
                case DebugValueKind.FPVector2:
                    ReadFPVector2(out long x2, out long y2);
                    return $"({FormatFixedPoint(x2)}, {FormatFixedPoint(y2)})";
                case DebugValueKind.FPVector3:
                    ReadFPVector3(out long x3, out long y3, out long z3);
                    return $"({FormatFixedPoint(x3)}, {FormatFixedPoint(y3)}, {FormatFixedPoint(z3)})";
                case DebugValueKind.FPQuaternion:
                    ReadFPQuaternion(out long x4, out long y4, out long z4, out long w4);
                    return $"({FormatFixedPoint(x4)}, {FormatFixedPoint(y4)}, {FormatFixedPoint(z4)}, {FormatFixedPoint(w4)})";
                case DebugValueKind.FPActionTargetSnapshot:
                    ReadFPSpatialIdentity(out string identity, out long targetX, out long targetY, out long targetZ, out long targetYaw);
                    return $"{identity} · ({FormatFixedPoint(targetX)}, {FormatFixedPoint(targetY)}, {FormatFixedPoint(targetZ)}) · {FormatFixedPoint(targetYaw)}°";
                default: return string.Empty;
            }
        }

        static string FormatFixedPoint(long raw)
        {
            ulong magnitude = raw < 0 ? unchecked(0UL - (ulong)raw) : (ulong)raw;
            string sign = raw < 0 ? "-" : string.Empty;
            string whole = (magnitude >> 16).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ulong fraction = (magnitude & 65535) * 152587890625UL;
            return fraction == 0 ? sign + whole : sign + whole + "." +
                fraction.ToString("D16", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
        }
    }

    public readonly struct RuntimeTimelinePlaybackProvenance
    {
        public RuntimeTimelinePlaybackProvenance(
            string sourceGraphAuthoringId,
            string sourceNodeAuthoringId,
            Guid sourceGraphRuntimeId,
            ulong sourceActivationGeneration,
            string stateMachineGraphAuthoringId,
            string stateId,
            Guid stateMachineGraphRuntimeId,
            ulong stateActivationGeneration,
            string sourceName,
            string sourceInvocationPath = "",
            int sourceOperationIndex = -1,
            ulong skillExecutionGeneration = 0,
            ulong callerPlaybackId = 0,
            string callerClipAuthoringId = "")
        {
            CallerPlaybackId = callerPlaybackId;
            CallerClipAuthoringId = callerClipAuthoringId ?? string.Empty;
            SourceGraphAuthoringId = sourceGraphAuthoringId ?? string.Empty;
            SourceNodeAuthoringId = sourceNodeAuthoringId ?? string.Empty;
            SourceGraphRuntimeId = sourceGraphRuntimeId;
            SourceActivationGeneration = sourceActivationGeneration;
            StateMachineGraphAuthoringId = stateMachineGraphAuthoringId ?? string.Empty;
            StateId = stateId ?? string.Empty;
            StateMachineGraphRuntimeId = stateMachineGraphRuntimeId;
            StateActivationGeneration = stateActivationGeneration;
            SourceName = sourceName ?? string.Empty;
            SourceInvocationPath = sourceInvocationPath ?? string.Empty;
            SourceOperationIndex = sourceOperationIndex;
            SkillExecutionGeneration = skillExecutionGeneration;
        }

        public string SourceGraphAuthoringId { get; }
        public string SourceNodeAuthoringId { get; }
        public Guid SourceGraphRuntimeId { get; }
        public ulong SourceActivationGeneration { get; }
        public string StateMachineGraphAuthoringId { get; }
        public string StateId { get; }
        public Guid StateMachineGraphRuntimeId { get; }
        public ulong StateActivationGeneration { get; }
        public string SourceName { get; }
        public string SourceInvocationPath { get; }
        public int SourceOperationIndex { get; }
        public ulong SkillExecutionGeneration { get; }
        public ulong CallerPlaybackId { get; }
        public string CallerClipAuthoringId { get; }
        public bool HasCaller => CallerPlaybackId != 0 &&
                                 !string.IsNullOrEmpty(CallerClipAuthoringId);
        public bool IsValid => !string.IsNullOrEmpty(SourceGraphAuthoringId) &&
                               !string.IsNullOrEmpty(SourceNodeAuthoringId) &&
                               SourceActivationGeneration != 0 &&
                               (SourceGraphRuntimeId != Guid.Empty || HasProgramInvocation);
        public bool HasProgramInvocation => !string.IsNullOrEmpty(SourceInvocationPath) &&
                                            SourceOperationIndex >= 0 &&
                                            SkillExecutionGeneration != 0;
        public bool HasStateActivation => !string.IsNullOrEmpty(StateMachineGraphAuthoringId) &&
                                          !string.IsNullOrEmpty(StateId) &&
                                          StateMachineGraphRuntimeId != Guid.Empty &&
                                          StateActivationGeneration != 0;
    }

    public struct RuntimeTracePayload
    {
        public string Status;
        public string Name;
        public string Detail;
        public string Cause;
        public string SourceClockId;
        public string SourceTickKind;
        public string OwnerId;
        public string RelatedElementId;
        public ulong InputSequence;
        public ulong ActionInstanceId;
        public string CallSiteId;
        public ulong ActivationGeneration;
        public ulong SkillExecutionGeneration;
        public ulong GraphInvocationGeneration;
        public ulong ParentInvocationGeneration;
        public string ParentInvocationPath;
        public ulong StartTick;
        public ulong EndTick;
        public ulong Revision;
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
        public OrchestrationBlackboardWriteStamp BlackboardWriteStamp;
        public bool BlackboardIsActive;
        public int TrackIndex;
        public int ClipIndex;
        public bool Flag;
        public DebugValueSnapshot Value;
        public RuntimeTimelinePlaybackProvenance TimelinePlayback;
        public EventId SourceEventId;
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
            : this(sessionId, contentRevision, runtimeEpoch, executionBranchId, domain, channel,
                position, timeSeconds, sequence, runtimeInstance, source, kind, in payload,
                payload.SourceClockId, payload.SourceTickKind)
        {
        }

        internal RuntimeTraceEvent(
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
            in RuntimeTracePayload payload,
            string sourceClockId,
            string sourceTickKind)
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
            m_Payload.SourceClockId = sourceClockId;
            m_Payload.SourceTickKind = sourceTickKind;
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
        internal static ref readonly RuntimeTracePayload ReadPayload(in RuntimeTraceEvent traceEvent) => ref traceEvent.m_Payload;
        public string GetStatus() => m_Payload.Status;
        public string GetDetail() => m_Payload.Detail;
        public int GetCycle() => m_Payload.Cycle;
        public ulong GetActionInstanceId() => m_Payload.ActionInstanceId;
        public string GetCause() => m_Payload.Cause;
        public string GetParentInvocationPath() => m_Payload.ParentInvocationPath;
        public ulong GetParentInvocationGeneration() => m_Payload.ParentInvocationGeneration;
        public Guid GetParentGraphRuntimeId() => m_Payload.TimelinePlayback.IsValid &&
            string.Equals(m_Payload.ParentInvocationPath, m_Payload.TimelinePlayback.SourceInvocationPath, StringComparison.Ordinal)
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
