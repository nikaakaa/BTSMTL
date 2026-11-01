using System;
using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
using Dispatch = BTSMTL.Timeline.Runtime.OrchestrationTimelineDispatch<BTSMTL.Orchestration.Float32OrchestrationValue,
    BTSMTL.BurstCheck.BurstCheckContext, BTSMTL.Orchestration.OrchestrationTime, BTSMTL.Orchestration.OrchestrationTimeClock>;

namespace BTSMTL.BurstCheck
{
    // 检查专用的纯数据插头：只是 Burst 实例化的载体，不提供运行功能，也不是正式宿主。
    public unsafe struct BurstCheckMemory
    {
        public byte* Program;
        public int ProgramLength;
        public byte* StateLayout;
        public int StateLayoutLength;
        public byte* State;
        public int StateLength;
        public byte* Scratch;
        public int ScratchLength;
        public byte* TimelineContent;
        public int TimelineContentLength;
        public byte* TimelineWorkspace;
        public int TimelineWorkspaceLength;
        public int* IdentityRanks;
        public int IdentityRankCount;
        public void* SampleClips;
        public int SampleClipCount;
        public void* CurveKeys;
        public int CurveKeyCount;
        public void* SampleInputs;
        public int SampleInputCount;
        public OrchestrationTime TimelineDuration;
        public OrchestrationTimelineStateRegions<OrchestrationTimelinePlaybackState<OrchestrationTime>> TimelineRegions;
        public OrchestrationTimelinePreparedCapacity TimelineCapacity;
    }

    public struct BurstCheckContext
    {
        public int FrameSlot;
    }

    public readonly struct BurstCheckFloat32Projection : IOrchestrationTimelineTimeProjection<OrchestrationTime, Float32Scalar>
    {
        public Float32Scalar Project(OrchestrationTime time) => Float32Scalar.FromSingle(time.AsSingle);
    }

    public struct BurstCheckTimelineBindings<TValue> :
        IOrchestrationTimelineBindings<OrchestrationTime, TValue, BurstCheckContext>
        where TValue : unmanaged
    {
        public OrchestrationTimelineStart<OrchestrationTime> Start(int operationIndex, int contentIdentityId,
            ulong playbackIdentity, ulong operationGeneration, ulong tick, ReadOnlySpan<TValue> inputs,
            ref BurstCheckContext context) => new OrchestrationTimelineStart<OrchestrationTime>
        {
            ContentIdentityId = contentIdentityId,
            PlaybackIdentity = playbackIdentity,
            SourceGeneration = operationGeneration,
            StartTick = tick
        };

        public OrchestrationTimelineStep<OrchestrationTime> Step(int operationIndex,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong tick, ReadOnlySpan<TValue> inputs,
            ref BurstCheckContext context) =>
            OrchestrationTimelineStep<OrchestrationTime>.FromClockInterval(tick, OrchestrationTime.Zero,
                OrchestrationTime.FromInt32(1), OrchestrationTime.FromInt32(1), false, start.SourceIdentityId);

        public void AcceptTerminal(int operationIndex, int playbackSlot, ulong tick,
            in OrchestrationTimelinePlaybackState<OrchestrationTime> accepted,
            in OrchestrationStopContext stopContext, ref BurstCheckContext context)
        {
        }

        public int EnterInvocation(in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationTimelineInvocation<OrchestrationTime> invocation, ref BurstCheckContext context) =>
            context.FrameSlot;

        public void SelectExecutionFrame(int frameSlot, ref BurstCheckContext context) =>
            context.FrameSlot = frameSlot;

        public OrchestrationOperationResult ReleaseInvocation(in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationTimelineInvocation<OrchestrationTime> invocation, ref BurstCheckContext context) =>
            OrchestrationOperationResult.Success;

        public void LeaveInvocation(ref BurstCheckContext context)
        {
        }
    }

    public readonly struct BurstCheckSampleInvoker<TValue> : ITimelineLogicSampleInvoker<TValue, BurstCheckContext>
        where TValue : unmanaged
    {
        public OrchestrationExtensionResult Invoke(int bindingIndex, ref BurstCheckContext context,
            in OrchestrationExtensionArguments<TValue> invocation) => OrchestrationExtensionResult.Success;
    }

    public unsafe struct BurstCheckLogicSamples<TValue, TScalar, TMath, TValues, TProjection> :
        ITimelineRuntimeLogicSamples<TValue, BurstCheckContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TMath : struct, IScalarMath<TScalar>
        where TValues : struct, IOrchestrationScalarValueAccess<TValue, TScalar>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
    {
        readonly BurstCheckMemory* m_Memory;

        public BurstCheckLogicSamples(BurstCheckMemory* memory) => m_Memory = memory;

        public void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot,
            ulong playbackIdentity, OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame, ref BurstCheckContext context)
        {
            var invoker = new BurstCheckSampleInvoker<TValue>();
            int failed = TimelineRuntimeLogicSampling<TValue, TScalar, TMath, TValues, TProjection>.Invoke(
                new ReadOnlySpan<TimelineRuntimeLogicSampleClip<TScalar>>(m_Memory->SampleClips, m_Memory->SampleClipCount),
                new ReadOnlySpan<GameplayAbilityCurveKey<TScalar>>(m_Memory->CurveKeys, m_Memory->CurveKeyCount),
                m_Memory->TimelineDuration, new Span<TValue>(m_Memory->SampleInputs, m_Memory->SampleInputCount),
                ref invoker, operationIndex, operationGeneration, tick, playbackSlot, playbackIdentity, previousTime,
                previousCycle, time, cycle, stateAncestors, executionFrame, ref context, out _);
            if (failed >= 0)
                throw new InvalidOperationException("检查插头的逻辑采样失败。");
        }
    }

    public unsafe struct BurstCheckHost : IOrchestrationHost<Float32OrchestrationValue>
    {
        readonly BurstCheckMemory* m_Memory;
        BurstCheckContext m_Context;

        public BurstCheckHost(BurstCheckMemory* memory)
        {
            m_Memory = memory;
            m_Context = default;
        }

        OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock> Runtime =>
            new OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>(
                new OrchestrationTimelineContentView<OrchestrationTime>(
                    new ReadOnlySpan<byte>(m_Memory->TimelineContent, m_Memory->TimelineContentLength)),
                m_Memory->TimelineRegions, in m_Memory->TimelineCapacity,
                new OrchestrationTimelineWorkspaceView<OrchestrationTime>(
                    new Span<byte>(m_Memory->TimelineWorkspace, m_Memory->TimelineWorkspaceLength)));

        public void Read(ReadOnlySpan<byte> memory, in OrchestrationValueAddress address,
            ref Float32OrchestrationValue destination) =>
            default(OrchestrationArrayValueStorage<Float32OrchestrationValue>).Read(memory, in address, ref destination);

        public void Write(Span<byte> memory, in OrchestrationValueAddress address, in Float32OrchestrationValue value) =>
            default(OrchestrationArrayValueStorage<Float32OrchestrationValue>).Write(memory, in address, in value);

        public OrchestrationExtensionExecutionContract ExecutionContract(int bindingIndex) =>
            throw new InvalidOperationException("检查插头不提供扩展。");

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex,
            in OrchestrationExtensionArguments<Float32OrchestrationValue> invocation) => OrchestrationExtensionResult.Success;

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex,
            in OrchestrationExtensionArguments<Float32OrchestrationValue> invocation) => OrchestrationExtensionResult.Success;

        public OrchestrationOperationResult Tick<TNumerics, THost>(
            ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<Float32OrchestrationValue> inputs, ulong tick)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue>
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            var samples = new BurstCheckLogicSamples<Float32OrchestrationValue, Float32Scalar, Float32SimulationMath,
                Float32OrchestrationValueAccess, BurstCheckFloat32Projection>(m_Memory);
            return Dispatch.Tick(Runtime, ref bindings, ref samples, ref cursor, candidate, operationIndex,
                contentIdentityId, operationGeneration, ref playbackIdentity, inputs, tick, ref m_Context);
        }

        public OrchestrationStopStatus Stop<TNumerics, THost>(
            ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue>
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            return Dispatch.Stop(Runtime, ref bindings, ref cursor, candidate, operationIndex, ref playbackIdentity,
                in stopContext, force, tick, ref m_Context);
        }

        public OrchestrationTime ReadTime() => Dispatch.ReadTime(Runtime);

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity) =>
            Dispatch.ReleasePlayback(Runtime, candidate, ref playbackIdentity);

        public int EnterInvocation(int playbackSlot, int invocationIndex)
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            return Dispatch.EnterInvocation(Runtime, ref bindings, playbackSlot, invocationIndex, ref m_Context);
        }

        public OrchestrationTimelineInvocationFacts ReadInvocationFacts() => Dispatch.ReadInvocationFacts(Runtime);

        public void SelectExecutionFrame(int frameSlot) => m_Context.FrameSlot = frameSlot;

        public OrchestrationOperationResult ReleaseInvocation()
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            return Dispatch.ReleaseInvocation(Runtime, ref bindings, ref m_Context);
        }

        public void LeaveInvocation()
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            Dispatch.LeaveInvocation(Runtime, ref bindings, ref m_Context);
        }

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate, int operationIndex, ulong tick)
        {
            var bindings = new BurstCheckTimelineBindings<Float32OrchestrationValue>();
            return Dispatch.RequestClipExit(Runtime, ref bindings, candidate, ref m_Context);
        }

        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Product;

        public void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> { }

        public int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> => -1;

        public void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            int operation, int slot, bool input, in Float32OrchestrationValue value)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> { }

        public void Trace<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> { }

        public int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> => -1;

        public void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
            where THost : struct, IOrchestrationHost<Float32OrchestrationValue> => context = default;
    }
}
