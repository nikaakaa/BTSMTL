using System;
using BTSMTL.Orchestration;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;

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

    public unsafe struct BurstCheckHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TProjection> :
        IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, BurstCheckContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
    {
        readonly BurstCheckMemory* m_Memory;

        public BurstCheckHost(BurstCheckMemory* memory) => m_Memory = memory;

        OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock> Runtime =>
            new OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>(
                new OrchestrationTimelineContentView<OrchestrationTime>(
                    new ReadOnlySpan<byte>(m_Memory->TimelineContent, m_Memory->TimelineContentLength)),
                m_Memory->TimelineRegions, in m_Memory->TimelineCapacity,
                new OrchestrationTimelineWorkspaceView<OrchestrationTime>(
                    new Span<byte>(m_Memory->TimelineWorkspace, m_Memory->TimelineWorkspaceLength)));

        public int Count => 0;

        public OrchestrationExtensionExecutionContract ExecutionContract(int bindingIndex) =>
            throw new InvalidOperationException("检查插头不提供扩展。");

        public bool ShouldExecute(int bindingIndex, in BurstCheckContext context) => false;

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in BurstCheckContext context,
            in OrchestrationExtensionArguments<TValue> invocation) => OrchestrationExtensionResult.Success;

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref BurstCheckContext context,
            in OrchestrationExtensionArguments<TValue> invocation) => OrchestrationExtensionResult.Success;

        public OrchestrationOperationResult Tick<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref BurstCheckContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, BurstCheckContext>
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            var samples = new BurstCheckLogicSamples<TValue, TScalar, TMath, TValues, TProjection>(m_Memory);
            return OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.Tick(Runtime, ref bindings, ref samples, ref cursor, candidate, operationIndex,
                contentIdentityId, operationGeneration, ref playbackIdentity, inputs, tick, ref context);
        }

        public OrchestrationStopStatus Stop<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref BurstCheckContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, BurstCheckContext>
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            return OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.Stop(Runtime, ref bindings, ref cursor, candidate, operationIndex, ref playbackIdentity,
                in stopContext, force, tick, ref context);
        }

        public TValue ReadTime() => OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.ReadTime(Runtime);

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in BurstCheckContext context) =>
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.ReleasePlayback(Runtime, candidate, ref playbackIdentity);

        public int EnterInvocation(int playbackSlot, int invocationIndex, ref BurstCheckContext context)
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            return OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.EnterInvocation(Runtime, ref bindings, playbackSlot, invocationIndex, ref context);
        }

        public OrchestrationTimelineInvocationFacts ReadInvocationFacts() => OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.ReadInvocationFacts(Runtime);

        public void SelectExecutionFrame(int frameSlot, ref BurstCheckContext context) =>
            context.FrameSlot = frameSlot;

        public OrchestrationOperationResult ReleaseInvocation(ref BurstCheckContext context)
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            return OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.ReleaseInvocation(Runtime, ref bindings, ref context);
        }

        public void LeaveInvocation(ref BurstCheckContext context)
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.LeaveInvocation(Runtime, ref bindings, ref context);
        }

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref BurstCheckContext context)
        {
            var bindings = new BurstCheckTimelineBindings<TValue>();
            return OrchestrationTimelineDispatch<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationTime, OrchestrationTimeClock, TProjection>.RequestClipExit(Runtime, ref bindings, candidate, ref context);
        }
    }
}
