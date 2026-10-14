using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ProgramValues = ThirdPersonSimulation.ProgramValueCalculation<ThirdPersonSimulation.FPGameplay.AbilityStateValue, Photon.Deterministic.FP, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath, ThirdPersonSimulation.FPGameplay.FPProgramNumericValueAccess>;
using System;
using System.IO;
using System.Collections.Generic;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPTreeClipInvokerLink
    {
        public IAbilityTreeClipInvoker Invoker;
    }

    internal sealed class FPAbilityOperationControlRuntime : IFPAbilityOperationControlRuntime, IAbilityTreeClipInvoker
    {
        readonly Dictionary<(CanonicalTextValue ClipAuthoringId, int Hook), OperationHandle> m_TreeClipEntries;
        readonly string m_AbilityId;
        readonly OperationExecutionTopology m_Topology;
        readonly int m_MaxExecutionCount;
        internal int MaximumOperationEvaluations => m_MaxExecutionCount;
        readonly FPTreeClipInvokerLink m_TreeClipLink = new FPTreeClipInvokerLink();
        FPAbilityExecutionServiceSet m_Services;
        OperationControlRuntime<FPAbilityExecutionTarget> m_Runtime;

        public FPAbilityOperationControlRuntime(
            FPGameplayAbilityExecutionData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            m_TreeClipEntries = BuildTreeClipEntries(data);
            m_AbilityId = data.AbilityId.Value;
            m_Topology = data.Topology;
            m_MaxExecutionCount = OperationControlRuntime.EvaluationCapacity(data.Operations.Count);
        }

        internal FPTreeClipInvokerLink TreeClipLink => m_TreeClipLink;

        internal void PrepareStateExecutionText(int maximumRetainedTextReferences)
        {
            int maximumGeneratedCharacters = MaximumGeneratedStatePathCharacters();
            int values = maximumGeneratedCharacters == 0 ? 0 : checked(maximumRetainedTextReferences + m_MaxExecutionCount);
            int characters = checked(values * maximumGeneratedCharacters);
            m_Runtime.PrepareStateExecutionText(new CanonicalTextStorage(values, characters), maximumGeneratedCharacters);
        }

        internal void Dispose() => m_Runtime.Dispose();

        int MaximumGeneratedStatePathCharacters()
        {
            int stateMachineCount = 0;
            for (int index = 0; index < m_Topology.Operations.Count; index++)
                if (m_Topology.Operations[index].Code == SimulationOperationCode.StateMachine)
                    stateMachineCount++;
            int handleDigits = 1;
            for (int maximum = m_Topology.Operations.Count - 1; maximum >= 10; maximum /= 10)
                handleDigits++;
            return checked(stateMachineCount * (4 + handleDigits + 7 + handleDigits + 1 + 20));
        }
        internal OperationControlCursor<FPAbilityExecutionTarget> Cursor => m_Runtime.Cursor;

        internal void Bind(in FPAbilityExecutionServiceSet services)
        {
            m_Services = services;
            m_TreeClipLink.Invoker = this;
            if (m_Runtime == null)
            {
                m_Runtime = new OperationControlRuntime<FPAbilityExecutionTarget>(
                    m_Topology,
                    m_Services.Target,
                    m_MaxExecutionCount);
            }
            else
            {
                m_Runtime.Rebind(m_Services.Target);
            }
        }

        internal void BeginEvaluation(bool diagnosticsEnabled, bool captureMotion, bool captureValues, bool captureControlFlow, bool captureBlackboard)
        {
            m_Services.BeginEvaluation(diagnosticsEnabled, captureMotion, captureValues, captureControlFlow, captureBlackboard);
            m_Runtime.BeginEvaluation();
        }
        internal void EndEvaluation()
        {
            m_Services.EndEvaluation();
        }
        public bool EvaluateRuleCondition(OperationHandle condition) =>
            m_Services.EvaluateRuleCondition(m_Runtime.Cursor, condition);

        public OperationExecutionResult Tick(OperationHandle operation) =>
            OperationControlRuntime.Execute(m_Runtime, new GraphExecutionEntry(operation, GraphExecutionContract.Resumable));
        internal OperationExecutionResult TickEdge(ProgramControlFlowEdge edge) => m_Runtime.TickEdge(edge);

        public bool InvokeTreeClip(in AbilityTreeClipInvocation invocation)
        {
            if (!m_TreeClipEntries.TryGetValue((invocation.ClipAuthoringId, (int)invocation.Hook), out OperationHandle entry))
            {
                if (invocation.Hook != AbilityTreeClipHook.OnEnable)
                    return false;
                throw new InvalidOperationException(
                    $"TreeClip '{invocation.ClipAuthoringId}' has no compiled OnEnable invocation in Ability '{m_AbilityId}'.");
            }
            AbilityTreeClipInvocation previousInvocation = m_Services.Target.ExchangeTreeClipInvocation(invocation);
            int firstTrace = m_Services.TraceRecords.Count;
            OperationExecutionResult result;
            try
            {
                result = OperationControlRuntime.Execute(m_Runtime, new GraphExecutionEntry(entry, GraphExecutionContract.Resumable));
                if ((invocation.Hook == AbilityTreeClipHook.OnDisable || invocation.Hook == AbilityTreeClipHook.OnDestroy) &&
                    !m_Services.Target.ReleaseTreeClipEffects(invocation))
                    result = OperationExecutionResult.Failure;
            }
            finally
            {
                m_Services.Target.ExchangeTreeClipInvocation(previousInvocation);
            }
            if (result == OperationExecutionResult.Failure)
                throw new InvalidOperationException(
                    $"TreeClip '{invocation.ClipAuthoringId}' {invocation.Hook} invocation failed in Ability '{m_AbilityId}'.{DescribeInvocationTrace(firstTrace)}");
            return true;
        }

        string DescribeInvocationTrace(int firstTrace)
        {
            var detail = new System.Text.StringBuilder();
            IReadOnlyList<SimulationTraceRecord> traces = m_Services.TraceRecords;
            for (int index = firstTrace; index < traces.Count; index++)
            {
                SimulationTraceRecord trace = traces[index];
                if (trace.Code != "operation_complete")
                    continue;
                OperationHandle operation = trace.Header.Activation.Source.Operation;
                detail.Append("; ").Append(operation.Value).Append(' ')
                    .Append(m_Topology.Operation(operation).CodeName).Append(" = ")
                    .Append(trace.Detail.ToString());
            }
            return detail.ToString();
        }

        static Dictionary<(CanonicalTextValue ClipAuthoringId, int Hook), OperationHandle> BuildTreeClipEntries(FPGameplayAbilityExecutionData data)
        {
            var entries = new Dictionary<(CanonicalTextValue ClipAuthoringId, int Hook), OperationHandle>();
            foreach (ProgramExecutionSource entry in data.ExecutionSources)
            {
                if (entry.TargetKind != ProgramSourceTargetKind.GraphInvocation ||
                    entry.InvocationCallerKind != ProgramInvocationCallerKind.TimelineClip ||
                    string.IsNullOrEmpty(entry.InvocationCallerClipId) ||
                    !Enum.TryParse(entry.InvocationCallerId, out AbilityTreeClipHook hook))
                {
                    continue;
                }
                if (!entries.TryAdd((entry.InvocationCallerClipId, (int)hook), new OperationHandle(entry.TargetIndex)))
                    throw new InvalidDataException(
                        $"Ability '{data.AbilityId.Value}' has duplicate TreeClip invocation '{entry.InvocationCallerClipId}/{entry.InvocationCallerId}'.");
            }
            return entries;
        }

        public bool IsActive(OperationHandle operation) => m_Runtime.IsActive(operation);
        public bool IsStopping(OperationHandle operation) => m_Runtime.IsStopping(operation);
        public OperationStopStatus RequestStop(OperationHandle operation, OperationStopContext context) =>
            m_Runtime.RequestStop(operation, context);
        public OperationStopStatus ContinueStop(OperationHandle operation) => m_Runtime.ContinueStop(operation);
        public void ForceStop(OperationHandle operation, OperationStopContext context) => m_Runtime.ForceStop(operation, context);
        public OperationRunnableStatus ReadStatus(OperationHandle operation) => m_Runtime.ReadStatus(operation);
        public ulong ReadGeneration(OperationHandle operation) => m_Runtime.ReadGeneration(operation);
    }

    internal readonly struct FPAbilityExecutionTarget :
        IOperationControlTarget<FPAbilityExecutionTarget>
    {
        readonly FPGameplayAbilityExecutionAccess m_Access;
        readonly FPStatePort m_ControlState;
        readonly FPOperationStateReset m_ResetState;
        readonly FPValueRuntime m_Values;
        readonly FPBlackboardRuntime m_Blackboard;
        readonly FPActionRuntime m_Actions;
        readonly FPGameplayEffectOperationRuntime m_GameplayEffects;
        readonly FPCombatOperationRuntime m_Combat;
        readonly FPEquipmentRuntime m_Equipment;
        readonly FPLocomotionRuntime m_Locomotion;
        readonly FPMotionWarpSubmitRuntime m_MotionWarp;
        readonly FPFactSink m_Facts;
        readonly FPPresentationSink m_Presentation;
        readonly FPTraceSink m_Trace;
        readonly IAbilityTimelineRuntime m_TimelineRuntime;
        readonly FPActionStateStore m_ActionState;
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPTreeClipInvokerLink m_TreeClipLink;
        readonly List<AbilityTimelineAdvancePending> m_TimelinePendingAdvances;
        readonly List<AbilityTimelineStopPending> m_TimelinePendingStops;

        public FPAbilityExecutionTarget(
            FPGameplayAbilityExecutionAccess access,
            FPStatePort controlState,
            FPOperationStateReset resetState,
            FPValueRuntime values,
            FPBlackboardRuntime blackboard,
            FPActionRuntime actions,
            FPGameplayEffectOperationRuntime gameplayEffects,
            FPCombatOperationRuntime combat,
            FPEquipmentRuntime equipment,
            FPLocomotionRuntime locomotion,
            FPMotionWarpSubmitRuntime motionWarp,
            FPFactSink facts,
            FPPresentationSink presentation,
            FPTraceSink trace,
            IAbilityTimelineRuntime timelineRuntime,
            FPActionStateStore actionState,
            List<AbilityTimelineAdvancePending> timelineAdvances,
            List<AbilityTimelineStopPending> timelineStops,
            FPAbilityExecutionFrame frame,
            FPTreeClipInvokerLink treeClipLink)
        {
            m_Access = access;
            m_ControlState = controlState;
            m_ResetState = resetState;
            m_Values = values;
            m_Blackboard = blackboard;
            m_Actions = actions;
            m_GameplayEffects = gameplayEffects;
            m_Combat = combat;
            m_Equipment = equipment;
            m_Locomotion = locomotion;
            m_MotionWarp = motionWarp;
            m_Facts = facts;
            m_Presentation = presentation;
            m_Trace = trace;
            m_TimelineRuntime = timelineRuntime;
            m_ActionState = actionState;
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_TimelinePendingAdvances = timelineAdvances ??
                throw new ArgumentNullException(nameof(timelineAdvances));
            m_TimelinePendingStops = timelineStops ??
                throw new ArgumentNullException(nameof(timelineStops));
            m_TreeClipLink = treeClipLink;
        }

        public bool DiagnosticsEnabled => m_Trace.Enabled;
        public bool ControlTraceEnabled => m_Trace.CaptureControlFlow;
        public int ReadInt32(int slotIndex) => m_ControlState.Get(slotIndex).Int32;
        public void WriteInt32(int slotIndex, int value) => m_ControlState.Set(slotIndex, AbilityStateValue.FromInt32(value));
        public ulong ReadUInt64(int slotIndex) => m_ControlState.Get(slotIndex).UInt64;
        public void WriteUInt64(int slotIndex, ulong value) => m_ControlState.Set(slotIndex, AbilityStateValue.FromUInt64(value));
        public CanonicalTextValue ReadIdentity(int slotIndex) => m_ControlState.Get(slotIndex).Identity;
        public void WriteIdentity(int slotIndex, CanonicalTextValue value) => m_ControlState.Set(slotIndex, AbilityStateValue.FromIdentity(value));
        public void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed) =>
            m_Trace.AddControlFlow(edge, selected, passed);

        internal AbilityTreeClipInvocation ExchangeTreeClipInvocation(in AbilityTreeClipInvocation invocation) =>
            m_Presentation.ExchangeTreeClipInvocation(invocation);

        internal bool ReleaseTreeClipEffects(in AbilityTreeClipInvocation invocation) =>
            m_GameplayEffects == null || m_GameplayEffects.ReleaseTreeClipEffects(invocation);

        public bool EvaluateCondition(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            ProgramControlFlowEdge edge) => m_Values.EvaluateCondition(cursor, edge);

        public OperationExecutionResult ExecuteLeaf(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            OperationExecutionDescriptor descriptor)
        {
            SimulationOperation operation = m_Access.Operation(descriptor.Handle);
            switch (descriptor.Code)
            {
                case SimulationOperationCode.CaptureActionTarget:
                    m_Values.CaptureActionTarget(cursor, operation);
                    return OperationExecutionResult.Success;
                case SimulationOperationCode.BlackboardSet:
                    return m_Values.SetBlackboard(cursor, operation)
                        ? OperationExecutionResult.Success
                        : OperationExecutionResult.Failure;
                case SimulationOperationCode.ActivateActionInstance:
                    return m_Actions.Activate(cursor, operation)
                        ? OperationExecutionResult.Success
                        : OperationExecutionResult.Failure;
                case SimulationOperationCode.SetActionPhase:
                    return m_Actions.SetActionPhase(cursor, operation)
                        ? OperationExecutionResult.Success
                        : OperationExecutionResult.Failure;
                case SimulationOperationCode.SubmitActionLifecycle:
                    return m_Actions.SubmitLifecycle(operation)
                        ? OperationExecutionResult.Success
                        : OperationExecutionResult.Failure;
                case SimulationOperationCode.GameplayEffectApply:
                {
                    bool applied = RequireGameplayEffects().Apply(operation, out ulong appliedHandle);
                    int slot = m_Access.RequireOperationSlot(operation.Handle, ProgramStateSemantic.GameplayEffectAppliedHandle);
                    m_ControlState.Set(slot, AbilityStateValue.FromUInt64(appliedHandle));
                    return applied ? OperationExecutionResult.Success : OperationExecutionResult.Failure;
                }
                case SimulationOperationCode.AttackQuery:
                    return m_Combat.SubmitAttack(operation)
                        ? OperationExecutionResult.Success : OperationExecutionResult.Failure;
                case SimulationOperationCode.GameplayEffectRemove:
                    using (FPValueInputLease inputs = m_Values.ReadInputs(cursor, operation))
                        return RequireGameplayEffects().Remove(operation, inputs[0].UInt64).Succeeded
                            ? OperationExecutionResult.Success : OperationExecutionResult.Failure;
                case SimulationOperationCode.RequestEquipmentChange:
                case SimulationOperationCode.BeginEquipmentChange:
                case SimulationOperationCode.CommitEquipmentChange:
                case SimulationOperationCode.CancelEquipmentChange:
                    using (FPValueInputLease inputs = m_Values.ReadInputs(cursor, operation))
                        return RequireEquipment().Execute(cursor, operation, inputs)
                            ? OperationExecutionResult.Success
                            : OperationExecutionResult.Failure;
                case SimulationOperationCode.LocomotionInputMotion:
                    return TickLocomotion(cursor, operation);
                case SimulationOperationCode.StateRootCompleted:
                case SimulationOperationCode.StateExitCause:
                case SimulationOperationCode.BlackboardGet:
                case SimulationOperationCode.InputBoolean:
                case SimulationOperationCode.InputScalar:
                case SimulationOperationCode.ActivationEntry:
                case SimulationOperationCode.TimelineTime:
                case SimulationOperationCode.InputVector2:
                case SimulationOperationCode.InputVector2Magnitude:
                case SimulationOperationCode.InputRequest:
                case SimulationOperationCode.MoveFacingAngle:
                case SimulationOperationCode.CharacterStateRead:
                case SimulationOperationCode.ActionTargetRead:
                case SimulationOperationCode.ActionContextActive:
                case SimulationOperationCode.ActionEventReceived:
                case SimulationOperationCode.AbilityActive:
                case SimulationOperationCode.LocomotionDecision:
                case SimulationOperationCode.CanActivateAction:
                case SimulationOperationCode.GameplayEffectHasTag:
                case SimulationOperationCode.GameplayEffectActive:
                case SimulationOperationCode.GameplayEffectSourceAllowsCandidate:
                case SimulationOperationCode.GameplayEffectMatchTags:
                case SimulationOperationCode.GameplayAttributeRead:
                case SimulationOperationCode.CameraBasisRead:
                case SimulationOperationCode.ConditionResult:
                case SimulationOperationCode.Compare:
                case SimulationOperationCode.And:
                case SimulationOperationCode.Or:
                case SimulationOperationCode.Not:
                case SimulationOperationCode.Constant:
                case SimulationOperationCode.ReadEquipmentIdentity:
                case SimulationOperationCode.ReadEquipmentParameter:
                    return ProgramValues.ToBoolean(m_Values.Evaluate(cursor, operation.Handle))
                        ? OperationExecutionResult.Success
                        : OperationExecutionResult.Failure;
                case SimulationOperationCode.Timeline:
                    return TickTimeline(cursor, operation);
                case SimulationOperationCode.TimelineClipExitRequest:
                    return RequestTreeClipExit(cursor, operation);
                case SimulationOperationCode.MotionWarp:
                    m_MotionWarp.Submit(cursor, operation);
                    return OperationExecutionResult.Success;
                case SimulationOperationCode.CameraStateRequest:
                case SimulationOperationCode.CameraResponse:
                case SimulationOperationCode.CameraTarget:
                case SimulationOperationCode.CameraEffectRequest:
                    return SubmitCameraRequest(operation);
                case SimulationOperationCode.Root:
                case SimulationOperationCode.Loop:
                case SimulationOperationCode.Parallel:
                case SimulationOperationCode.Sequence:
                case SimulationOperationCode.Selector:
                case SimulationOperationCode.Succeed:
                case SimulationOperationCode.SubGraph:
                case SimulationOperationCode.StateMachine:
                case SimulationOperationCode.State:
                case SimulationOperationCode.StateOnEnter:
                case SimulationOperationCode.StateOnExit:
                case SimulationOperationCode.TimelineEnter:
                    throw new InvalidOperationException(
                        $"Portable control operation '{descriptor.Code}' reached the Ability leaf dispatcher.");
                case SimulationOperationCode.StateEnter:
                case SimulationOperationCode.StateAny:
                case SimulationOperationCode.StateExit:
                case SimulationOperationCode.TimelineAnimation:
                case SimulationOperationCode.TimelineCameraState:
                case SimulationOperationCode.TimelineCameraResponse:
                    throw new InvalidOperationException(
                        $"Timeline content operation '{descriptor.Code}' cannot execute as an Ability leaf.");
                default:
                    throw new InvalidOperationException(
                        $"Ability operation '{descriptor.Handle}' code '{descriptor.Code}' has no FP owner.");
            }
        }

        OperationExecutionResult TickLocomotion(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            SimulationOperation operation)
        {
            int slot = m_Access.RequireOperationSlot(operation.Handle, ProgramStateSemantic.LocomotionMotionElapsedTicks);
            int committedTicks = checked(m_ControlState.Get(slot).Int32 + 1);
            int generationSlot = m_Access.RequireOperationSlot(
                operation.Handle,
                ProgramStateSemantic.RunnableActivationGeneration);
            m_Locomotion.Submit(
                cursor,
                operation,
                committedTicks,
                m_ControlState.Get(generationSlot).UInt64);
            var mode = (LocomotionInputMotionExecutionMode)operation.Integer0;
            if (mode == LocomotionInputMotionExecutionMode.Once)
                return OperationExecutionResult.Success;
            m_ControlState.Set(slot, AbilityStateValue.FromInt32(committedTicks));
            if (mode == LocomotionInputMotionExecutionMode.Continuous)
                return OperationExecutionResult.Running;
            if (mode != LocomotionInputMotionExecutionMode.Timed)
                throw new InvalidOperationException(
                    $"Locomotion operation '{m_Access.SourcePath(operation)}' has invalid execution mode '{operation.Integer0}'.");
            return committedTicks >= m_Locomotion.DurationTicks(operation.Handle)
                ? OperationExecutionResult.Success
                : OperationExecutionResult.Running;
        }

        OperationExecutionResult RequestTreeClipExit(OperationControlCursor<FPAbilityExecutionTarget> cursor, SimulationOperation operation)
        {
            using FPValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            if (!inputs[0].Boolean)
                return OperationExecutionResult.Success;
            if (m_TimelineRuntime == null)
                throw new InvalidOperationException($"Timeline operation '{m_Access.SourcePath(operation)}' has no Timeline runtime binding.");
            if (!m_Presentation.HasTreeClipInvocation)
                throw new InvalidOperationException($"Timeline Clip exit operation '{m_Access.SourcePath(operation)}' must execute inside a TreeClip invocation.");
            AbilityTreeClipInvocation invocation = m_Presentation.TreeClipInvocation;
            _ = m_TimelineRuntime.RequestTreeClipExit(
                invocation.TimelineRuntimeHandle,
                invocation.ClipAuthoringId,
                invocation.Cycle);
            if (m_GameplayEffects != null && !m_GameplayEffects.ReleaseTreeClipEffects(invocation))
                return OperationExecutionResult.Failure;
            return OperationExecutionResult.Success;
        }

        OperationExecutionResult TickTimeline(OperationControlCursor<FPAbilityExecutionTarget> cursor, SimulationOperation operation)
        {
            using FPValueInputLease inputs = m_Values.ReadInputs(cursor, operation);
            if (m_TimelineRuntime == null)
                throw new InvalidOperationException($"Ability Timeline operation '{m_Access.SourcePath(operation)}' has no Timeline runtime binding.");
            int slot = m_Access.RequireOperationSlot(operation.Handle, ProgramStateSemantic.TimelinePlayback);
            ulong runtimeHandle = m_ControlState.Get(slot).UInt64;
            if (runtimeHandle == 0)
            {
                if (!m_ActionState.TryGetCurrentSkillExecution(out FPActionInstanceState action) || !action.IsActive)
                    throw new InvalidOperationException($"Ability Timeline operation '{m_Access.SourcePath(operation)}' has no active Action context.");
                var actionContext = new TimelineActionContextIdentity(
                    action.ActionId,
                    action.ContextId,
                    action.InstanceId,
                    action.PredictionKey,
                    action.TargetSnapshot.TargetId,
                    action.SkillId,
                    action.SkillEntryOperation,
                    action.SkillExecutionGeneration);
                var request = new AbilityTimelineStartRequest(
                    operation.Text0,
                    new FPHandleAllocator(m_Frame).Next(),
                    (BTSMTL.Timeline.TimelinePlaybackMode)operation.Integer0,
                    actionContext,
                    CreateTimelineInvocationSource(operation),
                    action.InputSequence,
                    m_Frame.Tick,
                    inputs[2].Scalar);
                runtimeHandle = m_TimelineRuntime.Start(in request);
                if (runtimeHandle == 0)
                    throw new InvalidOperationException($"Ability Timeline operation '{m_Access.SourcePath(operation)}' did not return a runtime handle.");
                m_ControlState.Set(slot, AbilityStateValue.FromUInt64(runtimeHandle));
            }
            if (!m_ActionState.TryGetCurrentSkillExecution(out FPActionInstanceState sourceAction) || !sourceAction.IsActive)
                throw new InvalidOperationException("Timeline progress has no owning Action instance.");
            IAbilityTreeClipInvokerHost treeClipInvokerHost = m_TimelineRuntime as IAbilityTreeClipInvokerHost;
            IAbilityTreeClipInvoker previousInvoker = treeClipInvokerHost?.ExchangeTreeClipInvoker(m_TreeClipLink.Invoker);
            AbilityTimelineTickResult tick;
            try
            {
                var control = new AbilityTimelinePlaybackControl(
                    inputs[0].Scalar * m_Frame.Combat.ActionTimeScale,
                    inputs[1].Boolean || m_Frame.Combat.IsHalted(sourceAction.InstanceId, m_Frame.Tick.Value));
                tick = m_TimelineRuntime.Tick(runtimeHandle, m_Frame.Tick.Value, 1, control);
            }
            finally
            {
                treeClipInvokerHost?.ExchangeTreeClipInvoker(previousInvoker);
            }
            if (tick.Pending.IsValid)
                m_TimelinePendingAdvances.Add(tick.Pending);
            if (tick.Progress.IsValid)
            {
                SimulationEventHeader header = m_Presentation.Next(operation);
                m_Presentation.Add(new PresentationCommand(header, PresentationCommandKind.TimelineProgress,
                    m_Access.SourcePath(operation),
                    FP._0, FP._0,
                    sourceActionInstanceId: sourceAction.InstanceId,
                    timelineProgress: tick.Progress,
                    audioOwner: new AudioPlaybackOwner(header.ActorId, sourceAction.InstanceId,
                        runtimeHandle, tick.Progress.Generation)));
            }
            return tick.Status switch
            {
                AbilityTimelineRuntimeStatus.Running => OperationExecutionResult.Running,
                AbilityTimelineRuntimeStatus.Succeeded => OperationExecutionResult.Success,
                AbilityTimelineRuntimeStatus.Failed => OperationExecutionResult.Failure,
                AbilityTimelineRuntimeStatus.Cancelled => OperationExecutionResult.Failure,
                _ => throw new InvalidOperationException("Ability Timeline operation returned an invalid status.")
            };
        }

        AbilityTimelineInvocationSource CreateTimelineInvocationSource(SimulationOperation operation)
        {
            ProgramExecutionSource source = m_Access.RequireOperationSource(operation);
            ulong generation = m_Trace.ReadInvocationGeneration(operation.Handle);
            if (generation == 0)
                throw new InvalidOperationException($"Ability Timeline operation '{m_Access.SourcePath(operation)}' has no active invocation generation.");
            return new AbilityTimelineInvocationSource(
                operation.Handle.Value,
                source.GraphId,
                source.NodeId,
                m_Trace.ReadInvocationPath(operation.Handle),
                m_Access.SourcePath(operation),
                generation);
        }

        OperationExecutionResult SubmitCameraRequest(SimulationOperation operation)
        {
            if (!m_Presentation.HasTreeClipInvocation)
                throw new InvalidOperationException(
                    $"Camera operation '{m_Access.SourcePath(operation)}' must execute inside a TreeClip invocation.");
            ulong actionInstanceId = m_Presentation.TreeClipActionInstanceId;
            if (actionInstanceId == 0)
                throw new InvalidOperationException(
                    $"Camera operation '{m_Access.SourcePath(operation)}' has no active Action context.");
            AbilityTreeClipInvocation invocation = m_Presentation.TreeClipInvocation;
            PresentationCameraRequestLifecycle lifecycle = invocation.Hook == AbilityTreeClipHook.OnEnable
                ? PresentationCameraRequestLifecycle.Activate
                : PresentationCameraRequestLifecycle.Retire;
            PresentationCameraRequest request = CameraProgramRequestFactory.Build(
                operation.Code, operation.Integer1, operation.Flags, lifecycle,
                new FPCameraProgramConstantReader(m_Access.Layout, operation.Handle));
            if (request.Kind == PresentationCameraRequestKind.Effect &&
                lifecycle == PresentationCameraRequestLifecycle.Activate)
                request = request.WithActionTarget(m_ActionState.ReadTargetSnapshot(actionInstanceId).TargetId);
            SimulationEventHeader header = m_Presentation.Next(operation);
            m_Presentation.Add(new PresentationCommand(
                header,
                PresentationCommandKind.Camera,
                RequirePresentationProducer(operation),
                invocation.Time,
                FP._1,
                header.Activation.Generation,
                invocation.Cycle,
                actionInstanceId,
                cameraRequest: request));
            return OperationExecutionResult.Success;
        }

        string RequirePresentationProducer(SimulationOperation operation)
        {
            IReadOnlyList<ProgramReference> references = m_Access.References(operation.Handle, ProgramReferenceKind.Producer);
            if (references.Count != 1 || string.IsNullOrWhiteSpace(references[0].ExternalIdentity))
                throw new InvalidOperationException(
                    $"Presentation operation '{m_Access.SourcePath(operation)}' has no unique producer identity.");
            return references[0].ExternalIdentity;
        }

        ulong ReadTimelineRuntimeHandle(OperationExecutionDescriptor operation)
        {
            int slot = m_Access.RequireOperationSlot(operation.Handle, ProgramStateSemantic.TimelinePlayback);
            ulong runtimeHandle = m_ControlState.Get(slot).UInt64;
            if (runtimeHandle == 0)
                throw new InvalidOperationException($"Ability Timeline operation '{m_Access.SourcePath(m_Access.Operation(operation.Handle))}' is not started.");
            return runtimeHandle;
        }

        FPGameplayEffectOperationRuntime RequireGameplayEffects() => m_GameplayEffects ??
            throw new InvalidOperationException(
                "Ability operation requires the declared Gameplay Effect service.");

        FPEquipmentRuntime RequireEquipment() => m_Equipment ??
            throw new InvalidOperationException(
                "Ability operation requires the declared Equipment service.");

        public void PrepareSubGraph(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            OperationExecutionDescriptor operation) =>
            m_Values.PrepareSubGraph(cursor, m_Access.Operation(operation.Handle));

        public void ActivateScopes(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            OperationExecutionDescriptor operation,
            ulong generation)
        {
            m_Actions.BindCurrentSkillExecution(operation.Handle, generation);
        }

        public void ResetOperationState(OperationExecutionDescriptor operation) =>
            m_ResetState.Reset(m_Access.Operation(operation.Handle));

        public OperationStopStatus ContinueLeafStop(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            OperationExecutionDescriptor operation,
            OperationStopContext context)
        {
            if (operation.Code == SimulationOperationCode.Timeline)
            {
                var stop = StopTimeline(operation, ReadTimelineRuntimeHandle(operation));
                if (stop.Pending.IsValid)
                    m_TimelinePendingStops.Add(stop.Pending);
                return OperationStopStatus.Completed;
            }
            throw new InvalidOperationException(
                $"Ability operation '{m_Access.SourcePath(m_Access.Operation(operation.Handle))}' has no direct Timeline stop owner.");
        }

        public void ForceStopLeaf(
            OperationControlCursor<FPAbilityExecutionTarget> cursor,
            OperationExecutionDescriptor operation,
            OperationStopContext context)
        {
            if (operation.Code == SimulationOperationCode.Timeline)
            {
                var stop = StopTimeline(operation, ReadTimelineRuntimeHandle(operation));
                if (stop.Pending.IsValid)
                    m_TimelinePendingStops.Add(stop.Pending);
                return;
            }
            throw new InvalidOperationException(
                $"Ability operation '{m_Access.SourcePath(m_Access.Operation(operation.Handle))}' has no direct Timeline stop owner.");
        }

        AbilityTimelineStopResult StopTimeline(OperationExecutionDescriptor descriptor, ulong runtimeHandle)
        {
            IAbilityTreeClipInvokerHost host = m_TimelineRuntime as IAbilityTreeClipInvokerHost;
            IAbilityTreeClipInvoker previousInvoker = host?.ExchangeTreeClipInvoker(m_TreeClipLink.Invoker);
            try
            {
                AbilityTimelineStopResult stop = m_TimelineRuntime.Stop(runtimeHandle, m_Frame.Tick.Value);
                if (stop.Progress.IsValid)
                {
                    SimulationOperation operation = m_Access.Operation(descriptor.Handle);
                    SimulationEventHeader header = m_Presentation.Next(operation);
                    m_Presentation.Add(new PresentationCommand(header, PresentationCommandKind.TimelineProgress,
                        m_Access.SourcePath(operation),
                        FP._0, FP._0,
                        sourceActionInstanceId: stop.SourceActionInstanceId,
                        timelineProgress: stop.Progress,
                        audioOwner: new AudioPlaybackOwner(header.ActorId, stop.SourceActionInstanceId,
                            runtimeHandle, stop.Progress.Generation)));
                }
                return stop;
            }
            finally
            {
                host?.ExchangeTreeClipInvoker(previousInvoker);
            }
        }

        public void EmitTrace(
            OperationExecutionDescriptor operation,
            string code,
            OperationControlTraceSeverity severity,
            string detail) =>
            m_Trace.Add(
                m_Access.Operation(operation.Handle),
                code,
                severity == OperationControlTraceSeverity.Error
                    ? SimulationTraceSeverity.Error
                    : SimulationTraceSeverity.Detail,
                detail);

        public void NotifyStateLifecycle(
            OperationExecutionDescriptor machine,
            OperationHandle state,
            OperationStateLifecyclePhase phase)
        {
            SimulationOperation operation = m_Access.Operation(machine.Handle);
            SimulationEventHeader header = m_Facts.Next(operation);
            m_Facts.Add(new GameplayFact(
                header,
                GameplayFactKind.State,
                m_Access.Topology.StateIdentity(state),
                OperationTraceText.LifecyclePhase(phase),
                FP._0));
        }

        public void NotifyStateTransition(
            OperationExecutionDescriptor machine,
            OperationHandle exitingState,
            OperationHandle targetState)
        {
            m_Actions.TryAdvanceSegmentGeneration(out _);
        }
    }
    internal readonly struct FPCameraProgramConstantReader : ICameraProgramConstantReader
    {
        readonly GameplayAbilityExecutionLayout m_Layout;
        readonly OperationHandle m_Operation;

        public FPCameraProgramConstantReader(GameplayAbilityExecutionLayout layout, OperationHandle operation)
        {
            m_Layout = layout;
            m_Operation = operation;
        }

        public int ReadInt32(OperationNamedConstant field) => Require(field, ProgramConstantKind.Int32).Int32;
        public float ReadScalar(OperationNamedConstant field) => Require(field, ProgramConstantKind.Scalar).Scalar.AsFloat;

        public string ReadString(OperationNamedConstant field, bool required)
        {
            ProgramConstant constant = m_Layout.FindNamedConstant(m_Operation, field);
            if (constant == null && !required)
                return string.Empty;
            if (constant == null || constant.Kind != ProgramConstantKind.String ||
                required && string.IsNullOrWhiteSpace(constant.Text))
                throw new InvalidOperationException($"Camera operation '{m_Layout.SourcePath(m_Operation)}' string constant '{field}' is invalid.");
            return constant.Text;
        }

        ProgramConstant Require(OperationNamedConstant field, ProgramConstantKind kind)
        {
            ProgramConstant constant = m_Layout.FindNamedConstant(m_Operation, field);
            if (constant == null || constant.Kind != kind)
                throw new InvalidOperationException($"Camera operation '{m_Layout.SourcePath(m_Operation)}' constant '{field}' is missing or has kind '{constant?.Kind}'.");
            return constant;
        }
    }
}
