using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    // 推进入口唯一的宿主插头：扩展调用、Timeline 派发与片段退出由同一个结构体提供。
    // Burst 宿主用纯数据结构体实现；普通宿主使用 ManagedOrchestrationHost 转调既有对象。
    public interface IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> :
        IOrchestrationExtensionBindings<TValue, TContext>,
        IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
    {
    }

    public readonly struct ManagedOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> :
        IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
    {
        readonly IOrchestrationExtensionBindings<TValue, TContext> m_Extensions;
        readonly IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> m_Timeline;
        readonly IOrchestrationTimelineClipExitDispatcher<TContext> m_ClipExit;

        public ManagedOrchestrationHost(IOrchestrationExtensionBindings<TValue, TContext> extensions,
            IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> timeline = null,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExit = null)
        {
            m_Extensions = extensions;
            m_Timeline = timeline;
            m_ClipExit = clipExit ?? timeline;
        }

        public int Count => m_Extensions.Count;

        public ref readonly OrchestrationExtensionContract Contract(int bindingIndex) =>
            ref m_Extensions.Contract(bindingIndex);

        public bool ShouldExecute(int bindingIndex, in TContext context) =>
            m_Extensions.ShouldExecute(bindingIndex, in context);

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in TContext context,
            in OrchestrationExtensionArguments<TValue> invocation) =>
            m_Extensions.InvokeReadOnly(bindingIndex, in context, in invocation);

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref TContext context,
            in OrchestrationExtensionArguments<TValue> invocation) =>
            m_Extensions.InvokeCandidate(bindingIndex, ref context, in invocation);

        public OrchestrationOperationResult Tick<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            m_Timeline.Tick(ref cursor, candidate, operationIndex, contentIdentityId, operationGeneration,
                ref playbackIdentity, inputs, tick, ref context);

        public OrchestrationStopStatus Stop<TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext, bool force, ulong tick, ref TContext context)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            m_Timeline.Stop(ref cursor, candidate, operationIndex, ref playbackIdentity, in stopContext,
                force, tick, ref context);

        public TValue ReadTime() => m_Timeline.ReadTime();

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context) =>
            m_Timeline.ReleasePlayback(candidate, operationIndex, ref playbackIdentity, in context);

        public int EnterInvocation(int playbackSlot, int invocationIndex, ref TContext context) =>
            m_Timeline.EnterInvocation(playbackSlot, invocationIndex, ref context);

        public OrchestrationTimelineInvocationFacts ReadInvocationFacts() => m_Timeline.ReadInvocationFacts();

        public void SelectExecutionFrame(int frameSlot, ref TContext context) =>
            m_Timeline.SelectExecutionFrame(frameSlot, ref context);

        public OrchestrationOperationResult ReleaseInvocation(ref TContext context) =>
            m_Timeline.ReleaseInvocation(ref context);

        public void LeaveInvocation(ref TContext context) => m_Timeline.LeaveInvocation(ref context);

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context) =>
            m_ClipExit.RequestClipExit(candidate, operationIndex, tick, ref context);

        // 常用组合的构造辅助：宿主只需写诊断与值存储两个类型实参。
        public OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
            TDiagnostics, TStorage, ManagedOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>>
            CreateCursor<TDiagnostics, TStorage>(OrchestrationCandidateState<TValue> state,
                OrchestrationProgramView<TValue> program, OrchestrationExecutionScratch<TValue> scratch, int frameSlot,
                ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
                OrchestrationIdentityOrdinalView identities, in TContext context, TDiagnostics diagnostics)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue> =>
            state.CreateCursor<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage,
                ManagedOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>>(
                program, scratch, frameSlot, characterSlots, in input, identities, in context, this, diagnostics);

        public OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext,
            TDiagnostics, TStorage, ManagedOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>>
            CreateCursor<TDiagnostics, TStorage>(OrchestrationCandidateState<TValue> state,
                OrchestrationProgramView<TValue> program, OrchestrationExecutionSourceBindingView sourceBinding,
                OrchestrationExecutionScratch<TValue> scratch, int frameSlot, OrchestrationExecutionSource<TValue> source,
                ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
                OrchestrationIdentityOrdinalView identities, in TContext context, TDiagnostics diagnostics)
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue> =>
            state.CreateCursor<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage,
                ManagedOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>>(
                program, sourceBinding, scratch, frameSlot, source, characterSlots, in input, identities, in context,
                this, diagnostics);
    }
}
