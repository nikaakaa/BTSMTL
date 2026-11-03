using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationTraceObjectIdentity : IEquatable<OrchestrationTraceObjectIdentity>
    {
        public OrchestrationTraceObjectIdentity(ObjectHandle handle, int slot, int kind, int definitionIndex,
            int programIndex, OrchestrationTraceAvailability availability = OrchestrationTraceAvailability.Available)
        {
            Handle = handle;
            Slot = slot;
            Kind = kind;
            DefinitionIndex = definitionIndex;
            ProgramIndex = programIndex;
            Availability = availability;
        }

        public ObjectHandle Handle { get; }
        public int Slot { get; }
        public int Kind { get; }
        public int DefinitionIndex { get; }
        public int ProgramIndex { get; }
        public OrchestrationTraceAvailability Availability { get; }
        public bool HasValue => Availability == OrchestrationTraceAvailability.Available && !Handle.IsEmpty;
        public static OrchestrationTraceObjectIdentity NotApplicable =>
            new OrchestrationTraceObjectIdentity(default, 0, 0, 0, 0, OrchestrationTraceAvailability.NotApplicable);

        public bool Equals(OrchestrationTraceObjectIdentity other) =>
            Handle.Equals(other.Handle) && Slot == other.Slot && Kind == other.Kind &&
            DefinitionIndex == other.DefinitionIndex && ProgramIndex == other.ProgramIndex && Availability == other.Availability;

        public override bool Equals(object obj) => obj is OrchestrationTraceObjectIdentity other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Handle.GetHashCode();
                hash = hash * 31 + Slot;
                hash = hash * 31 + Kind;
                hash = hash * 31 + DefinitionIndex;
                hash = hash * 31 + ProgramIndex;
                return hash * 31 + (byte)Availability;
            }
        }
    }

    public enum OrchestrationTraceKind : byte
    {
        Enter = 1,
        Running = 2,
        Complete = 3,
        StopRequested = 4,
        Stopped = 5,
        ForceStopped = 6,
        Waiting = 7,
        LoopIterationEnter = 8,
        LoopIterationComplete = 9,
        Edge = 10,
        Blackboard = 11
    }

    public enum OrchestrationTraceAvailability : byte
    {
        Unavailable,
        NotApplicable,
        Available
    }

    [ValueStorage]
    public partial struct OrchestrationTraceLoopScope
    {
        public int OperationIndex;
        public int InvocationSourceIndex;
        public int ParentInvocationSourceIndex;
        public int Iteration;
        public ulong Generation;
        public OrchestrationTraceAvailability Activation;
        public ulong ActionInstanceId;
        public ulong ExecutionGeneration;
        public ulong InvocationGeneration;
        public ulong ParentInvocationGeneration;
        public ulong PlaybackIdentity;
        public bool HasValue => Iteration > 0;

        public bool Equals(in OrchestrationTraceLoopScope other) =>
            OperationIndex == other.OperationIndex && InvocationSourceIndex == other.InvocationSourceIndex &&
            ParentInvocationSourceIndex == other.ParentInvocationSourceIndex && Iteration == other.Iteration &&
            Generation == other.Generation && Activation == other.Activation && ActionInstanceId == other.ActionInstanceId &&
            ExecutionGeneration == other.ExecutionGeneration && InvocationGeneration == other.InvocationGeneration &&
            ParentInvocationGeneration == other.ParentInvocationGeneration && PlaybackIdentity == other.PlaybackIdentity;
    }

    public struct OrchestrationTraceEvent<TValue> where TValue : unmanaged
    {
        public OrchestrationTraceKind Kind;
        public int OperationIndex;
        public int EdgeIndex;
        public ulong Generation;
        public int StateFrameIndex;
        public int ContextIndex;
        public OrchestrationOperationResult Result;
        public OrchestrationWaitReason WaitReason;
        public uint StopCause;
        public OrchestrationStopMode StopMode;
        public bool Selected;
        public bool Passed;
        public int DetailIndex;
        public TValue Value;
        public OrchestrationBlackboardMetadata Blackboard;
        public OrchestrationBlackboardLifetime BlackboardLifetime;
        public OrchestrationBlackboardWriteStamp BlackboardWriteStamp;
    }

    public struct OrchestrationStateTraceFrame
    {
        public int ParentIndex;
        public int StateOperationIndex;
        public ulong Generation;
        public int ContextIndex;
    }

    // 当前执行或关闭入口的 scope，不追溯节点首次激活时的 hook。
    public struct OrchestrationTraceContext
    {
        public OrchestrationTraceObjectIdentity ExecutionObject;
        public Guid CharacterRuntimeId;
        public int OperationIndex;
        public ulong NodeGeneration;
        public OrchestrationTraceAvailability NodeActivation;
        public ulong ObservationSequence;
        public OrchestrationTraceAvailability LoopScopeAvailability;
        public OrchestrationTraceLoopScope LoopScope;
        public int SourceIndex;
        public int InvocationSourceIndex;
        public int ParentInvocationSourceIndex;
        public int InvocationOperationIndex;
        public int ParentInvocationOperationIndex;
        public int ExecutionFrameOwnerOperationIndex;
        public ulong ActionInstanceId;
        public ulong SkillExecutionGeneration;
        public ulong GraphInvocationGeneration;
        public ulong ParentInvocationGeneration;
        public OrchestrationTimelineStart<OrchestrationTime> TimelineStart;
        public int ClipOrdinal;
        public int Cycle;
        public bool Marker;
    }

    public readonly struct OrchestrationTraceBatch
    {
        public OrchestrationTraceBatch(OrchestrationRange events, OrchestrationRange stateFrames,
            OrchestrationRange contexts)
        {
            Events = events;
            StateFrames = stateFrames;
            Contexts = contexts;
        }

        public OrchestrationRange Events { get; }
        public OrchestrationRange StateFrames { get; }
        public OrchestrationRange Contexts { get; }
    }
}
