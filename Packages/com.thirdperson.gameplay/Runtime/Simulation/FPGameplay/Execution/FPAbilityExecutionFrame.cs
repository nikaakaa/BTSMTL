using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    internal interface IFPAbilityExecutionSavepointPort
    {
        FPAbilityExecutionSavepoint CreateSavepoint();
        FPAbilityExecutionSavepoint CreateActivationSavepoint();
        void Restore(FPAbilityExecutionSavepoint savepoint);
        void Release(FPAbilityExecutionSavepoint savepoint);
        int SavepointDepth { get; }
    }

    internal interface IFPAbilityFrameReadPort
    {
        SimulationTick Tick { get; }
        ulong InputSequence { get; }
        bool HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId);
        FPAbilityBodyFacts BodyFacts { get; }
        SimulationInputValue ReadInputValue(CanonicalTextValue inputId, ProgramInputValueKind kind);
        SimulationInputRequestState ReadInputRequest(CanonicalTextValue requestId);
        IReadOnlyList<FPActionInstanceState> Actions { get; }
        IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> Pending { get; }
        bool TryReadAction(ulong instanceId, out FPActionInstanceState action);
        AbilityStateValue ReadState(CharacterSkillId skillId, int slot, in GameplayAbilitySourceContext source);
        GameplayAbilityExecutionAggregate<AbilityStateValue> ReadAbilityState(CharacterSkillId skillId);
        EquipmentStateAggregate Equipment { get; }
    }

    internal sealed class FPAbilityExecutionInput
    {
        SimulationIngress[] m_Ingress;
        int m_IngressCount;

        public ulong Sequence { get; private set; }
        public IReadOnlyList<SimulationInputValue> Values { get; private set; }

        public FPAbilityExecutionInput()
        {
        }

        public void Begin(
            SimulationInput input,
            SimulationIngress[] ingress,
            int ingressCount)
        {
            Sequence = input.Sequence;
            m_Ingress = ingress;
            m_IngressCount = ingressCount;
            Values = input.Values;
        }

        public SimulationInputValue ReadValue(string inputId, ProgramInputValueKind kind)
        {
            int first = 0;
            int last = Values.Count - 1;
            while (first <= last)
            {
                int index = first + ((last - first) >> 1);
                SimulationInputValue value = Values[index];
                int order = CanonicalTextValue.CompareOrdinal(value.InputId, inputId);
                if (order < 0)
                    first = index + 1;
                else if (order > 0)
                    last = index - 1;
                else
                {
                    if (value.Kind != kind)
                        throw new InvalidOperationException($"Input '{inputId}' is '{value.Kind}', expected '{kind}'.");
                    return value;
                }
            }
            throw new InvalidOperationException($"Tick input does not contain required value '{inputId}'.");
        }

        public bool HasActionEvent(ActorId actorId, ulong actionInstanceId, string eventId, CanonicalTextValue actionTargetId)
        {
            for (int i = 0; i < m_IngressCount; i++)
            {
                SimulationIngress ingress = m_Ingress[i];
                if (ingress.Header.Kind == SimulationIngressKind.ActionEvent &&
                    ingress.ActionEvent.ActionInstanceId == actionInstanceId &&
                    CanonicalTextValue.Equals(ingress.ActionEvent.EventId, eventId, StringComparison.Ordinal))
                    return true;
                if (ingress.Header.Kind == SimulationIngressKind.GameplayResult && !ingress.GameplayResult.HasEffect)
                {
                    CombatHitResult hit = ingress.GameplayResult.Hit;
                    if (actorId == hit.SourceActor && actionInstanceId == hit.Attack.ActionInstanceId &&
                        hit.Disposition == CombatHitDisposition.Hit &&
                        (CanonicalTextValue.Equals(eventId, hit.SourceFeedbackEvent, StringComparison.Ordinal) ||
                         CanonicalTextValue.Equals(eventId, hit.AttackEventId, StringComparison.Ordinal)))
                        return true;
                    if (actorId == hit.TargetActor && actionInstanceId == hit.TargetActionInstanceId &&
                        (hit.Disposition != CombatHitDisposition.Parried ||
                         CanonicalTextValue.Equals(hit.SourceActor.Value, actionTargetId, StringComparison.Ordinal)) &&
                        CanonicalTextValue.Equals(eventId, hit.TargetFeedbackEvent, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        public bool HasGameplayEvent(ActorId actorId, string eventId)
        {
            for (int i = 0; i < m_IngressCount; i++)
            {
                ref readonly SimulationIngress ingress = ref m_Ingress[i];
                if (ingress.Header.Kind == SimulationIngressKind.ActionEvent &&
                    CanonicalTextValue.Equals(eventId, ingress.ActionEvent.EventId, StringComparison.Ordinal))
                    return true;
                if (ingress.Header.Kind != SimulationIngressKind.GameplayResult || ingress.GameplayResult.HasEffect)
                    continue;
                CombatHitResult hit = ingress.GameplayResult.Hit;
                if (hit.TargetActor == actorId &&
                    CanonicalTextValue.Equals(eventId, hit.TargetFeedbackEvent, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public bool TryReadActionEvent(ActorId actorId, in FPActionInstanceState action, CanonicalTextValue eventId,
            out ulong identity, out ulong sourceTick, out SimulationActionTargetSnapshot target)
        {
            for (int i = 0; i < m_IngressCount; i++)
            {
                ref readonly SimulationIngress ingress = ref m_Ingress[i];
                if (ingress.Header.Kind == SimulationIngressKind.ActionEvent &&
                    ingress.ActionEvent.ActionInstanceId == action.InstanceId &&
                    CanonicalTextValue.Equals(eventId, ingress.ActionEvent.EventId, StringComparison.Ordinal))
                {
                    identity = ingress.Header.Sequence;
                    sourceTick = ingress.Header.SourceTick;
                    target = action.TargetSnapshot;
                    return true;
                }
                if (ingress.Header.Kind != SimulationIngressKind.GameplayResult || ingress.GameplayResult.HasEffect)
                    continue;
                CombatHitResult hit = ingress.GameplayResult.Hit;
                if (hit.TargetActor == actorId && hit.TargetActionInstanceId == action.InstanceId &&
                    (hit.Disposition != CombatHitDisposition.Parried || hit.SourceActor.Value.Equals(action.TargetSnapshot.TargetId)) &&
                    CanonicalTextValue.Equals(eventId, hit.TargetFeedbackEvent, StringComparison.Ordinal))
                {
                    identity = hit.ResultId;
                    sourceTick = hit.SourceTick;
                    target = hit.SourceTargetSnapshot;
                    return true;
                }
            }
            identity = 0;
            sourceTick = 0;
            target = default;
            return false;
        }

        public void Clear()
        {
            Sequence = 0;
            m_Ingress = null;
            m_IngressCount = 0;
            Values = null;
        }
    }

    internal readonly struct FPAbilityBodyFacts
    {
        public FPAbilityBodyFacts(ActorId actorId, WorldBodyState body)
        {
            if (!actorId.IsValid || body.ActorId != actorId)
                throw new ArgumentException("FP Ability body facts identity is incomplete.", nameof(body));
            IsValid = true;
            Position = body.Position;
            Yaw = body.Yaw;
            Velocity = body.Velocity;
            VerticalVelocity = body.VerticalVelocity;
            Grounded = body.Grounded;
        }

        public bool IsValid { get; }
        public FPVector3 Position { get; }
        public FP Yaw { get; }
        public FPVector3 Velocity { get; }
        public FP VerticalVelocity { get; }
        public bool Grounded { get; }
    }

    internal interface IFPSkillExecutionStateAccess
    {
        bool TryGet(int slotIndex, out AbilityStateValue value);
        bool TrySet(int slotIndex, AbilityStateValue value);
        bool TryReset(int slotIndex);
    }

    internal readonly struct FPAbilityOutputSavepoint
    {
        public FPAbilityOutputSavepoint(int factCount, int presentationCount, int gameplayEffectCount, int gameplayEffectCueCount,
            int gameplayEffectSuccessfulChangeCount, int attackQueryCount)
        {
            FactCount = factCount;
            PresentationCount = presentationCount;
            GameplayEffectCount = gameplayEffectCount;
            GameplayEffectCueCount = gameplayEffectCueCount;
            GameplayEffectSuccessfulChangeCount = gameplayEffectSuccessfulChangeCount;
            AttackQueryCount = attackQueryCount;
        }

        public int FactCount { get; }
        public int PresentationCount { get; }
        public int GameplayEffectCount { get; }
        public int GameplayEffectCueCount { get; }
        public int GameplayEffectSuccessfulChangeCount { get; }
        public int AttackQueryCount { get; }
    }

    internal readonly struct FPAbilityInvocationContext
    {
        public FPAbilityInvocationContext(
            ActorId actorId,
            SimulationTick tick,
            FPAbilityExecutionInput input,
            FPAbilityBodyFacts bodyFacts,
            IFPSkillExecutionState skillState,
            IFPAbilityExecutionSavepointPort savepointPort,
            IFPInputRequestStatePort inputRequests,
            IFPActionRuntimeStatePort actionState,
            IFPHandleAllocatorStatePort handleAllocatorState,
            IFPEventSequenceStatePort eventSequenceState,
            IFPGameplayEffectStatePort gameplayEffectState,
            IFPEquipmentStatePort equipmentState,
            FPCombatQueryStorage combatQueries,
            FPCombatEvaluation combat)
        {
            ActorId = actorId;
            Tick = tick;
            Input = input;
            BodyFacts = bodyFacts;
            SkillState = skillState;
            SavepointPort = savepointPort;
            InputRequests = inputRequests;
            ActionState = actionState;
            HandleAllocatorState = handleAllocatorState;
            EventSequenceState = eventSequenceState;
            GameplayEffectState = gameplayEffectState;
            EquipmentState = equipmentState;
            CombatQueries = combatQueries;
            Combat = combat;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public FPAbilityExecutionInput Input { get; }
        public FPAbilityBodyFacts BodyFacts { get; }
        public IFPSkillExecutionState SkillState { get; }
        public IFPAbilityExecutionSavepointPort SavepointPort { get; }
        public IFPInputRequestStatePort InputRequests { get; }
        public IFPActionRuntimeStatePort ActionState { get; }
        public IFPHandleAllocatorStatePort HandleAllocatorState { get; }
        public IFPEventSequenceStatePort EventSequenceState { get; }
        public IFPGameplayEffectStatePort GameplayEffectState { get; }
        public IFPEquipmentStatePort EquipmentState { get; }
        internal FPCombatQueryStorage CombatQueries { get; }
        internal FPCombatEvaluation Combat { get; }
    }

    internal sealed class FPAbilityExecutionFrame
    {
        readonly List<GameplayFact> m_Facts;
        readonly List<PresentationCommand> m_Presentation;
        readonly List<SimulationTraceRecord> m_Trace;
        readonly FPAbilityExecutionWorkspace m_Workspace;
        FPAbilityBodyFacts m_BodyFacts;
        IFPGameplayEffectStatePort m_GameplayEffectState;
        IFPEquipmentStatePort m_EquipmentState;
        IFPSkillExecutionStateAccess m_SkillExecutionStateAccess;
        ulong m_ActionTraceInstanceId;
        string m_ActionTraceSkillId = string.Empty;
        OperationHandle m_ActionTraceEntryOperation = OperationHandle.Invalid;
        AbilityTreeClipInvocation m_TreeClipInvocation;

        public FPAbilityExecutionFrame(
            FPGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            FPGameplayAbilityExecutionServices services,
            ActorId actorId,
            FPTraceSink trace,
            FPAbilityExecutionWorkspace workspace)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            ActorId = actorId;
            workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            m_Facts = workspace.Facts;
            m_Presentation = workspace.Presentation;
            m_Trace = workspace.Trace;
            m_Workspace = workspace;
            EventSequence = new FPEventSequence(this);
            Facts = new FPFactSink(this, EventSequence);
            Presentation = new FPPresentationSink(this, EventSequence);
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            Trace.Bind(this);
        }

        internal IFPAbilityFrameReadPort AvailabilityRead { get; private set; }
#if UNITY_EDITOR
        internal FPCharacterEvaluationOutput TraceOutput => m_Workspace.TraceOutput;
#endif
        internal IFPAbilityFrameReadPort ExchangeAvailabilityRead(IFPAbilityFrameReadPort read)
        {
            IFPAbilityFrameReadPort previous = AvailabilityRead;
            AvailabilityRead = read;
            return previous;
        }
        internal SimulationInputValue ReadInputValue(string id, ProgramInputValueKind kind) =>
            AvailabilityRead == null ? Input.ReadValue(id, kind) : AvailabilityRead.ReadInputValue(id, kind);
        internal SimulationInputRequestState ReadInputRequest(CanonicalTextValue id) =>
            AvailabilityRead == null ? InputRequests.GetInputRequest(id) : AvailabilityRead.ReadInputRequest(id);
        internal bool HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId) =>
            AvailabilityRead == null ? Input.HasActionEvent(ActorId, instanceId, eventId, targetId) : AvailabilityRead.HasActionEvent(instanceId, eventId, targetId);
        internal ulong InputSequence => AvailabilityRead == null ? Input.Sequence : AvailabilityRead.InputSequence;
        internal IReadOnlyList<FPActionInstanceState> ReadActionInstances() =>
            AvailabilityRead == null ? ActionState.GetActionInstances() : AvailabilityRead.Actions;
        internal IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> ReadPendingRequests() =>
            AvailabilityRead == null ? ActionState.GetActionActivationRequests() : AvailabilityRead.Pending;
        internal bool TryReadAction(ulong instanceId, out FPActionInstanceState action) =>
            AvailabilityRead == null ? ActionState.TryGetActionInstance(instanceId, out action) : AvailabilityRead.TryReadAction(instanceId, out action);
        internal GameplayAbilityExecutionAggregate<AbilityStateValue> ReadAbilityState() =>
            AvailabilityRead == null ? SkillState.GetAbilityExecutionState() : AvailabilityRead.ReadAbilityState(Data.AbilityId);
        internal EquipmentStateAggregate ReadEquipmentState() =>
            AvailabilityRead == null ? EquipmentState.GetEquipmentState() : AvailabilityRead.Equipment;

        internal IGameplayAbilityDecisionReadPort DecisionRead { get; set; }
        internal GameplayAbilitySourceContext SelectedSource;
        internal GameplayAbilityRuleEvaluationMode RuleEvaluationMode { get; set; }

        internal void Begin(in FPAbilityInvocationContext context)
        {
            if (!context.ActorId.IsValid || !context.Tick.IsValid)
                throw new ArgumentException("FP Ability invocation identity is incomplete.");
            if (context.ActorId != ActorId)
                throw new InvalidOperationException("FP Ability invocation actor does not match its frame.");
            if (context.Input == null)
                throw new ArgumentNullException(nameof(context.Input));
            if (context.SkillState == null)
                throw new ArgumentNullException(nameof(context.SkillState));
            if (context.SavepointPort == null)
                throw new ArgumentNullException(nameof(context.SavepointPort));
            if (context.InputRequests == null)
                throw new ArgumentNullException(nameof(context.InputRequests));
            if (context.ActionState == null)
                throw new ArgumentNullException(nameof(context.ActionState));
            if (context.HandleAllocatorState == null)
                throw new ArgumentNullException(nameof(context.HandleAllocatorState));
            if (context.EventSequenceState == null)
                throw new ArgumentNullException(nameof(context.EventSequenceState));

            m_Tick = context.Tick;
            Input = context.Input;
            m_BodyFacts = context.BodyFacts;
            SkillState = context.SkillState;
            SavepointPort = context.SavepointPort;
            InputRequests = context.InputRequests;
            ActionState = context.ActionState;
            HandleAllocatorState = context.HandleAllocatorState;
            EventSequenceState = context.EventSequenceState;
            m_GameplayEffectState = context.GameplayEffectState;
            m_EquipmentState = context.EquipmentState;
            CombatQueries = context.CombatQueries;
            Combat = context.Combat;
            Trace.Bind(this);
        }

        internal FPValuePortInputs ValuePorts { get; set; }
        public FPGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        internal FPGameplayAbilityExecutionServices Services { get; }
        public GameplayAbilityExecutionIdentity Identity => Services.Identity;
        public SimulationNumericProfile NumericProfile => Data.NumericProfile;
        public ActorId ActorId { get; }
        SimulationTick m_Tick;
        public SimulationTick Tick => AvailabilityRead == null ? m_Tick : AvailabilityRead.Tick;
        public FPAbilityExecutionInput Input { get; private set; }
        public FPAbilityBodyFacts BodyFacts => AvailabilityRead != null ? AvailabilityRead.BodyFacts : m_BodyFacts.IsValid
            ? m_BodyFacts
            : throw new InvalidOperationException("FP Ability invocation has no Body Facts service.");
        internal IFPSkillExecutionState SkillState { get; private set; }
        internal IFPAbilityExecutionSavepointPort SavepointPort { get; private set; }
        internal IFPInputRequestStatePort InputRequests { get; private set; }
        internal IFPActionRuntimeStatePort ActionState { get; private set; }
        internal IFPHandleAllocatorStatePort HandleAllocatorState { get; private set; }
        internal IFPEventSequenceStatePort EventSequenceState { get; private set; }
        internal IFPGameplayEffectStatePort GameplayEffectState => m_GameplayEffectState ??
            throw new InvalidOperationException("FP Ability invocation has no Gameplay Effect state service.");
        internal IFPEquipmentStatePort EquipmentState => m_EquipmentState ??
            throw new InvalidOperationException("FP Ability invocation has no Equipment state service.");
        internal FPEventSequence EventSequence { get; }
        internal FPFactSink Facts { get; }
        internal FPPresentationSink Presentation { get; }
        internal FPCombatQueryStorage CombatQueries { get; private set; }
        internal FPCombatEvaluation Combat { get; private set; }
        internal FPTraceSink Trace { get; }
        internal IReadOnlyList<SimulationTraceRecord> TraceRecords => m_Trace;

        internal bool HasTreeClipInvocation => m_TreeClipInvocation.ActionInstanceId != 0;

        internal ulong TreeClipActionInstanceId => m_TreeClipInvocation.ActionInstanceId;

        internal ref readonly AbilityTreeClipInvocation TreeClipInvocation
        {
            get
            {
                if (!HasTreeClipInvocation)
                    throw new InvalidOperationException("Camera presentation requires an active TreeClip invocation.");
                return ref m_TreeClipInvocation;
            }
        }

        internal AbilityTreeClipInvocation ExchangeTreeClipInvocation(in AbilityTreeClipInvocation invocation)
        {
            AbilityTreeClipInvocation previous = m_TreeClipInvocation;
            m_TreeClipInvocation = invocation;
            return previous;
        }

        internal FPStatePort CreateStatePort(string owner, FPStateAccessPolicy policy) =>
            new FPStatePort(this, owner, policy);

        internal FPOperationStateReset CreateOperationStateReset() =>
            new FPOperationStateReset(this);

        internal ulong CurrentSkillTraceGeneration
        {
            get
            {
                if (!m_ActionTraceEntryOperation.IsValid)
                    return 0;
                int slot = Layout.FindOperationStateSlot(
                    m_ActionTraceEntryOperation,
                    ProgramStateSemantic.RunnableActivationGeneration);
                if (slot < 0)
                    throw new InvalidOperationException("Skill trace entry has no activation generation state.");
                return ReadState(slot).UInt64;
            }
        }

        internal ActionTraceContextScope PushActionTraceContext(
            ulong actionInstanceId,
            CharacterSkillId skillId,
            OperationHandle entryOperation)
        {
            ulong previousInstanceId = m_ActionTraceInstanceId;
            string previousSkillId = m_ActionTraceSkillId;
            OperationHandle previousEntryOperation = m_ActionTraceEntryOperation;
            m_ActionTraceInstanceId = actionInstanceId;
            m_ActionTraceSkillId = skillId.IsValid ? skillId.Value : string.Empty;
            m_ActionTraceEntryOperation = entryOperation;
            return new ActionTraceContextScope(
                this,
                previousInstanceId,
                previousSkillId,
                previousEntryOperation);
        }

        internal void BindSkillExecutionStateAccess(IFPSkillExecutionStateAccess access)
        {
            if (access == null)
                throw new ArgumentNullException(nameof(access));
            if (m_SkillExecutionStateAccess != null && !ReferenceEquals(m_SkillExecutionStateAccess, access))
                throw new InvalidOperationException("FP ability frame is already bound to another Skill execution state owner.");
            m_SkillExecutionStateAccess = access;
        }

        internal bool TryGetSkillExecutionState(int slotIndex, out AbilityStateValue value) =>
            m_SkillExecutionStateAccess.TryGet(slotIndex, out value);

        internal bool TrySetSkillExecutionState(int slotIndex, AbilityStateValue value) =>
            m_SkillExecutionStateAccess.TrySet(slotIndex, value);

        internal bool TryResetSkillExecutionState(int slotIndex) =>
            m_SkillExecutionStateAccess.TryReset(slotIndex);

        internal ulong CurrentActionTraceInstanceId => m_ActionTraceInstanceId;
        internal string CurrentActionTraceSkillId => m_ActionTraceSkillId;
        internal bool HasActionTraceContext => m_ActionTraceInstanceId != 0;

        internal FPAbilityOutputSavepoint CreateOutputSavepoint() =>
            new FPAbilityOutputSavepoint(m_Facts.Count, m_Presentation.Count, m_Workspace.GameplayEffectProjectionCount,
                m_Workspace.GameplayEffectCueCount, m_Workspace.GameplayEffectSuccessfulChangeCount, CombatQueries.Count);

        internal void RestoreOutput(FPAbilityOutputSavepoint savepoint)
        {
            m_Facts.RemoveRange(savepoint.FactCount, m_Facts.Count - savepoint.FactCount);
            m_Presentation.RemoveRange(savepoint.PresentationCount, m_Presentation.Count - savepoint.PresentationCount);
            m_Workspace.TruncateGameplayEffectProjections(savepoint.GameplayEffectCount);
            m_Workspace.GameplayEffectCueCount = savepoint.GameplayEffectCueCount;
            m_Workspace.GameplayEffectSuccessfulChangeCount = savepoint.GameplayEffectSuccessfulChangeCount;
            CombatQueries.Truncate(savepoint.AttackQueryCount);
        }

        internal void RecordCommandBatchRejection(in GameplayCommandBatchAdmission rejection) =>
            m_Workspace.CommandBatchRejections.Add(rejection);

        internal void RecordGameplayEffectChange(in PortableEffectRuntimeChange change, ActivationId source, CanonicalTextValue cause)
        {
            SimulationEventHeader factHeader = Facts.Next(source);
            int factIndex = m_Facts.Count;
            m_Facts.Add(default);
            SimulationEventHeader presentationHeader = default;
            int presentationIndex = -1;
            string producerId = null;
            FP sampleTime = default;
            if (change.Kind == PortableEffectChangeKind.Cue)
            {
                producerId = Services.RequireGameplayCueProducer(change.Definition.Id, change.CueId).Identity;
                presentationHeader = Presentation.Next(source);
                presentationIndex = m_Presentation.Count;
                sampleTime = Presentation.HasTreeClipInvocation ? Presentation.TreeClipInvocation.Time : FP._0;
                m_Presentation.Add(default);
            }
            var projection = new FPGameplayEffectPendingProjection(
                change, factHeader, factIndex, presentationHeader, presentationIndex, producerId, sampleTime, cause);
            projection.Retain();
            m_Workspace.GameplayEffectProjections[m_Workspace.GameplayEffectProjectionCount] = projection;
            m_Workspace.GameplayEffectProjectionCount++;
            if (change.IsSuccessfulOutput)
                m_Workspace.GameplayEffectSuccessfulChangeCount++;
            if (change.Kind == PortableEffectChangeKind.Cue)
                m_Workspace.GameplayEffectCueCount++;
        }

        internal void ResetState(int slotIndex)
        {
            if (TryResetSkillExecutionState(slotIndex))
                return;
            SkillState.Reset(slotIndex);
        }

        internal AbilityStateValue ReadState(int slotIndex) =>
            AvailabilityRead != null
                ? AvailabilityRead.ReadState(Data.AbilityId, slotIndex, in SelectedSource)
                : TryGetSkillExecutionState(slotIndex, out AbilityStateValue value) ? value : SkillState.Get(slotIndex);

        internal void End()
        {
            Trace.End();
            m_ActionTraceInstanceId = 0;
            m_ActionTraceSkillId = string.Empty;
            m_ActionTraceEntryOperation = OperationHandle.Invalid;
        }

        internal void AddFact(GameplayFact value) => m_Facts.Add(value);
        internal void AddPresentation(PresentationCommand value) => m_Presentation.Add(value);
        internal SimulationValueTrace CaptureValueTrace(string portId,
            ProgramValuePortDirection direction, in AbilityStateValue value) =>
            m_Workspace.ValueTraces.Capture(m_Workspace.ValueTraceInvocation, portId, direction, in value);

        internal void AddTrace(in SimulationTraceRecord value)
        {
#if UNITY_EDITOR
            long traceStoreStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            m_Workspace.TraceOutput.ReserveTraceRecord();
            value.Retain();
            m_Trace.Add(value);
#if UNITY_EDITOR
            m_Workspace.TraceOutput.EvaluationTraceStoreElapsedStopwatchTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - traceStoreStarted;
            m_Workspace.TraceOutput.EvaluationTraceStoreCalls++;
#endif
        }

        internal struct ActionTraceContextScope : IDisposable
        {
            readonly FPAbilityExecutionFrame m_Owner;
            readonly ulong m_PreviousInstanceId;
            readonly string m_PreviousSkillId;
            readonly OperationHandle m_PreviousEntryOperation;
            bool m_Disposed;

            public ActionTraceContextScope(
                FPAbilityExecutionFrame owner,
                ulong previousInstanceId,
                string previousSkillId,
                OperationHandle previousEntryOperation)
            {
                m_Owner = owner;
                m_PreviousInstanceId = previousInstanceId;
                m_PreviousSkillId = previousSkillId;
                m_PreviousEntryOperation = previousEntryOperation;
                m_Disposed = false;
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_Owner.m_ActionTraceInstanceId = m_PreviousInstanceId;
                m_Owner.m_ActionTraceSkillId = m_PreviousSkillId;
                m_Owner.m_ActionTraceEntryOperation = m_PreviousEntryOperation;
            }
        }
    }

    internal readonly struct FPOperationStateReset
    {
        readonly FPAbilityExecutionFrame m_Frame;

        public FPOperationStateReset(FPAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void Reset(SimulationOperation operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            for (int i = 0; i < operation.StateSlots.Count; i++)
            {
                int slotIndex = operation.StateSlots[i];
                if (slotIndex < 0 || slotIndex >= m_Frame.Layout.StateSlots.Count)
                    throw new InvalidOperationException($"Operation '{operation.Handle}' owns invalid state slot '{slotIndex}'.");
                ProgramStateSlot slot = m_Frame.Layout.StateSlots[slotIndex];
                if (slot.Semantic == ProgramStateSemantic.RunnableActivationGeneration)
                    continue;
                m_Frame.ResetState(slotIndex);
            }
        }
    }

    internal readonly struct FPStatePort
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPStateAccessPolicy m_Policy;
        readonly string m_Owner;

        public FPStatePort(
            FPAbilityExecutionFrame frame,
            string owner,
            FPStateAccessPolicy policy)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Owner = SimulationIdentity.Require(owner, nameof(owner));
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public AbilityStateValue Get(int slotIndex)
        {
            Require(slotIndex);
            return m_Frame.ReadState(slotIndex);
        }

        public void Set(int slotIndex, AbilityStateValue value)
        {
            Require(slotIndex);
            if (m_Frame.TrySetSkillExecutionState(slotIndex, value))
                return;
            m_Frame.SkillState.Set(slotIndex, value);
        }

        public void Reset(int slotIndex)
        {
            Require(slotIndex);
            if (m_Frame.TryResetSkillExecutionState(slotIndex))
                return;
            m_Frame.SkillState.Reset(slotIndex);
        }

        void Require(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= m_Frame.Layout.StateSlots.Count)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            ProgramStateSemantic semantic = m_Frame.Layout.StateSlots[slotIndex].Semantic;
            if (!m_Policy.Allows(semantic))
            {
                throw new InvalidOperationException(
                    $"State port '{m_Owner}' cannot access '{semantic}' slot '{slotIndex}'.");
            }
        }
    }

    internal readonly struct FPEventSequence
    {
        readonly FPAbilityExecutionFrame m_Frame;

        public FPEventSequence(FPAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public ActivationId Activation(SimulationOperation operation)
        {
            int generationSlot = m_Frame.Layout.FindOperationStateSlot(
                operation.Handle,
                ProgramStateSemantic.RunnableActivationGeneration);
            if (generationSlot < 0)
                throw new InvalidOperationException(
                    $"Operation '{SourcePath(operation)}' has no activation generation state.");
            ulong generation = m_Frame.ReadState(generationSlot).UInt64;
            return new ActivationId(
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)),
                generation);
        }

        public SimulationEventHeader Next(SimulationOperation operation, string channel) =>
            Next(Activation(operation), channel);

        public SimulationEventHeader Next(ActivationId activation, string channel) =>
            Create(activation, channel,
                activation.Source.IsSkillOperation && m_Frame.HasTreeClipInvocation ? m_Frame.TreeClipInvocation : null);

        public SimulationEventHeader Next(SimulationExecutionSource source, ulong generation, string channel)
            => Create(new ActivationId(source, generation), channel, null);

        SimulationEventHeader Create(
            ActivationId activation,
            string channel,
            AbilityTreeClipInvocation? treeClipInvocation)
        {
            ulong sequence = m_Frame.EventSequenceState.NextEventSequence();
            EventId eventId = treeClipInvocation.HasValue
                ? EventIdBuilder.CreateTreeClip(
                    new GameplayContentHash(m_Frame.Identity.ContentHash),
                    m_Frame.ActorId,
                    activation,
                    m_Frame.Tick,
                    sequence,
                    channel,
                    treeClipInvocation.Value)
                : EventIdBuilder.Create(
                    new GameplayContentHash(m_Frame.Identity.ContentHash),
                    m_Frame.ActorId,
                    activation,
                    m_Frame.Tick,
                    sequence,
                    channel);
            return new SimulationEventHeader(
                m_Frame.NumericProfile,
                eventId,
                m_Frame.ActorId,
                m_Frame.Tick,
                activation,
                sequence,
                channel,
                treeClipInvocation);
        }

        public string SourcePath(SimulationOperation operation)
        {
            return m_Frame.Services.SourcePath(operation.Handle);
        }
    }

    internal readonly struct FPFactSink
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPEventSequence m_Sequence;

        public FPFactSink(FPAbilityExecutionFrame frame, FPEventSequence sequence)
        {
            m_Frame = frame;
            m_Sequence = sequence;
        }

        public SimulationEventHeader Next(SimulationOperation operation) => m_Sequence.Next(operation, "Gameplay");
        public ActivationId Activation(SimulationOperation operation) => m_Sequence.Activation(operation);
        public SimulationEventHeader Next(ActivationId activation) => m_Sequence.Next(activation, "Gameplay");
        public SimulationEventHeader Next(SimulationExecutionSource source, ulong generation = 1) => m_Sequence.Next(source, generation, "Gameplay");
        public void Add(GameplayFact value) => m_Frame.AddFact(value);
    }

    internal readonly struct FPPresentationSink
    {
        readonly FPAbilityExecutionFrame m_Frame;
        readonly FPEventSequence m_Sequence;

        public FPPresentationSink(FPAbilityExecutionFrame frame, FPEventSequence sequence)
        {
            m_Frame = frame;
            m_Sequence = sequence;
        }

        public SimulationEventHeader Next(SimulationOperation operation) =>
            m_Sequence.Next(operation, "Presentation");
        public SimulationEventHeader Next(ActivationId activation) => m_Sequence.Next(activation, "Presentation");
        public SimulationEventHeader Next(SimulationExecutionSource source, ulong generation = 1) => m_Sequence.Next(source, generation, "Presentation");
        public void Add(PresentationCommand value) => m_Frame.AddPresentation(value);

        internal bool HasTreeClipInvocation => m_Frame.HasTreeClipInvocation;
        internal ulong TreeClipActionInstanceId => m_Frame.TreeClipActionInstanceId;
        internal ref readonly AbilityTreeClipInvocation TreeClipInvocation => ref m_Frame.TreeClipInvocation;

        internal AbilityTreeClipInvocation ExchangeTreeClipInvocation(in AbilityTreeClipInvocation invocation)
            => m_Frame.ExchangeTreeClipInvocation(invocation);
    }

    internal struct FPDiagnosticSequence
    {
        readonly FPAbilityExecutionFrame m_Frame;
        ulong m_Sequence;

        public FPDiagnosticSequence(FPAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Sequence = 0;
        }

        public void Reset()
        {
            m_Sequence = 0;
        }

        public SimulationTraceHeader Next(SimulationOperation operation)
        {
#if UNITY_EDITOR
            long headerStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            int generationSlot = m_Frame.Layout.FindOperationStateSlot(
                operation.Handle,
                ProgramStateSemantic.RunnableActivationGeneration);
            ulong generation = generationSlot < 0 ? 1UL : m_Frame.ReadState(generationSlot).UInt64;
            ref readonly SimulationExecutionSource source = ref m_Frame.Trace.OperationSource(operation.Handle);
            SimulationTraceHeader header = Complete(source, generation);
#if UNITY_EDITOR
            m_Frame.TraceOutput.EvaluationTraceHeaderElapsedStopwatchTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - headerStarted;
            m_Frame.TraceOutput.EvaluationTraceHeaderCalls++;
#endif
            return header;
        }

        public SimulationTraceHeader Next(SimulationExecutionSource source, ulong generation = 1)
        {
#if UNITY_EDITOR
            long headerStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            SimulationTraceHeader header = Complete(source, generation);
#if UNITY_EDITOR
            m_Frame.TraceOutput.EvaluationTraceHeaderElapsedStopwatchTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - headerStarted;
            m_Frame.TraceOutput.EvaluationTraceHeaderCalls++;
#endif
            return header;
        }

        SimulationTraceHeader Complete(SimulationExecutionSource source, ulong generation)
        {
            ulong sequence = checked(++m_Sequence);
            if (generation == 0)
                generation = 1;
            var activation = new ActivationId(source, generation);
            AbilityTreeClipInvocation? treeClipInvocation = m_Frame.HasTreeClipInvocation
                ? m_Frame.TreeClipInvocation
                : null;
            return new SimulationTraceHeader(
                m_Frame.ActorId,
                m_Frame.Tick,
                activation,
                sequence,
                treeClipInvocation);
        }
    }

    internal sealed class FPTraceSink
    {
        readonly HashSet<(int Operation, string Port, ProgramValuePortDirection Direction)> m_ValuePorts = new();
        readonly HashSet<string> m_EdgeIds = new(StringComparer.Ordinal);
        readonly GameplayAbilityGraphInvocationLayout m_Invocations;
        readonly SimulationExecutionSource[] m_OperationSources;
        readonly Dictionary<(int Target, string Port), ProgramControlFlowEdge> m_ValueEdges = new();
        int m_ValueSampleCount;
        FPAbilityExecutionFrame m_Frame;
        FPDiagnosticSequence m_Sequence;
        bool m_Enabled;

        public FPTraceSink(
            FPGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout)
        {
            m_OperationSources = new SimulationExecutionSource[data.Operations.Count];
            for (int index = 0; index < m_OperationSources.Length; index++)
            {
                var operation = new OperationHandle(index);
                m_OperationSources[index] = SimulationExecutionSource.FromSkillOperation(
                    operation, layout.SourcePath(operation));
            }
            m_Invocations = new GameplayAbilityGraphInvocationLayout(data.ExecutionSources, layout.Operations.Count,
                owner => layout.FindOperationStateSlot(owner, ProgramStateSemantic.RunnableActivationGeneration));
            foreach (ProgramExecutionSource source in data.ExecutionSources)
            {
                if (source.TargetKind == ProgramSourceTargetKind.OperationPort && source.ValuePortDirection != ProgramValuePortDirection.None)
                    m_ValuePorts.Add((source.TargetIndex, source.CompiledPortId, source.ValuePortDirection));
                if (source.TargetKind == ProgramSourceTargetKind.Reference && !CanonicalTextValue.IsNullOrEmpty(source.EdgeId))
                {
                    ProgramControlFlowEdge edge = data.ControlFlow[source.TargetIndex];
                    m_EdgeIds.Add(edge.Identity);
                    if (edge.Kind == ProgramControlFlowKind.Value)
                        m_ValueEdges.Add((edge.Target.Value, edge.TargetPort), edge);
                }
            }
        }

        internal ref readonly SimulationExecutionSource OperationSource(OperationHandle operation) =>
            ref m_OperationSources[operation.Value];

        internal void Bind(FPAbilityExecutionFrame frame)
        {
            m_Frame = frame;
            m_Sequence = new FPDiagnosticSequence(frame);
            m_Enabled = false;
            CaptureMotion = false;
            CaptureValues = false;
            CaptureControlFlow = false;
            CaptureBlackboard = false;
            m_ValueSampleCount = 0;
        }

        public void Begin(bool enabled, bool captureMotion, bool captureValues, bool captureControlFlow, bool captureBlackboard)
        {
            m_Enabled = enabled;
            CaptureMotion = captureMotion;
            CaptureValues = enabled && captureValues;
            CaptureControlFlow = enabled && captureControlFlow;
            CaptureBlackboard = enabled && captureBlackboard;
        }

        public bool Enabled => m_Enabled;
        public bool CaptureMotion { get; private set; }
        public bool CaptureValues { get; private set; }
        public bool CaptureBlackboard { get; private set; }
        public bool CaptureControlFlow { get; private set; }

        public void AddValueEdge(SimulationOperation target, string port)
        {
            if (CaptureControlFlow && m_ValueEdges.TryGetValue((target.Handle.Value, port), out ProgramControlFlowEdge edge))
                AddControlFlow(edge, true, true);
        }

        public void AddControlFlow(ProgramControlFlowEdge edge, bool selected, bool passed)
        {
            if (!CaptureControlFlow || !m_EdgeIds.Contains(edge.Identity))
                return;
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(m_Frame.Layout.Operations[edge.Source.Value]), SimulationTraceSeverity.Detail, "Ability.Flow",
                selected ? "edge_selected" : "edge_evaluated", string.Empty,
                m_Frame.CurrentActionTraceInstanceId, m_Frame.CurrentActionTraceSkillId, m_Frame.CurrentSkillTraceGeneration,
                controlFlow: edge, controlFlowSelected: selected, controlFlowPassed: passed,
                graphInvocationGeneration: InvocationGeneration(edge.Source),
                parentInvocationGeneration: ParentGeneration(edge.Source),
                ruleSource: in m_Frame.SelectedSource));
        }

        public void End()
        {
            m_Enabled = false;
            CaptureMotion = false;
            CaptureValues = false;
            CaptureControlFlow = false;
            CaptureBlackboard = false;
            m_Frame = null;
            m_Sequence = default;
        }

        public void AddValue(SimulationOperation operation, string portId, ProgramValuePortDirection direction, in AbilityStateValue value)
        {
            if (!CaptureValues || CanonicalTextValue.IsNullOrEmpty(portId) || !m_ValuePorts.Contains((operation.Handle.Value, portId, direction)))
                return;
            if (m_ValueSampleCount >= SimulationValueTraceLimits.MaxSamplesPerEvaluation)
            {
                if (m_ValueSampleCount == SimulationValueTraceLimits.MaxSamplesPerEvaluation)
                {
                    m_ValueSampleCount++;
                    Add(operation, "value_sampling_limit", SimulationTraceSeverity.Detail,
                        "本次求值的端口采样达到上限，后续值未采集。");
                }
                return;
            }
            m_ValueSampleCount++;
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(operation), SimulationTraceSeverity.Detail, "Ability.Value", "value_sampled", string.Empty,
                m_Frame.CurrentActionTraceInstanceId, m_Frame.CurrentActionTraceSkillId, m_Frame.CurrentSkillTraceGeneration,
                m_Frame.CaptureValueTrace(portId, direction, in value),
                graphInvocationGeneration: InvocationGeneration(operation.Handle),
                parentInvocationGeneration: ParentGeneration(operation.Handle),
                ruleSource: in m_Frame.SelectedSource));
        }

        public void AddBlackboard(SimulationOperation operation, int stateSlot, AbilityStateValue value, BlackboardOwnerToken owner, ProgramBlackboardLifetime lifetime)
        {
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(operation), SimulationTraceSeverity.Information, "Ability.Blackboard", "blackboard_written", string.Empty,
                m_Frame.CurrentActionTraceInstanceId, m_Frame.Data.AbilityId.Value, m_Frame.CurrentSkillTraceGeneration,
                graphInvocationGeneration: InvocationGeneration(operation.Handle),
                parentInvocationGeneration: ParentGeneration(operation.Handle),
                blackboardTrace: new SimulationBlackboardTrace(stateSlot, value, owner, lifetime)));
        }

        public void AddBlackboardCommand(SimulationExecutionSource source, ulong sequence, int stateSlot,
            AbilityStateValue value, BlackboardOwnerToken owner, ProgramBlackboardLifetime lifetime)
        {
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(source, sequence), SimulationTraceSeverity.Information, "Ability.Blackboard", "blackboard_command_applied", string.Empty,
                m_Frame.CurrentActionTraceInstanceId, m_Frame.Data.AbilityId.Value, m_Frame.CurrentSkillTraceGeneration,
                blackboardTrace: new SimulationBlackboardTrace(stateSlot, value, owner, lifetime)));
        }

        public void Add(SimulationOperation operation, string code, SimulationTraceSeverity severity, string detail)
        {
            if (!m_Enabled)
                return;
            SimulationTraceHeader header = m_Sequence.Next(operation);
            m_Frame.AddTrace(new SimulationTraceRecord(
                header,
                severity,
                "Ability.Operation",
                code,
                detail,
                m_Frame.CurrentActionTraceInstanceId,
                m_Frame.CurrentActionTraceSkillId,
                m_Frame.CurrentSkillTraceGeneration,
                graphInvocationGeneration: InvocationGeneration(operation.Handle),
                parentInvocationGeneration: ParentGeneration(operation.Handle),
                loopIteration: operation.Code == SimulationOperationCode.Loop
                    ? m_Frame.ReadState(m_Frame.Layout.FindOperationStateSlot(operation.Handle, ProgramStateSemantic.RunnableChildCursor)).Int32
                    : 0));
        }

        public void Add(
            SimulationExecutionSource source,
            string code,
            SimulationTraceSeverity severity,
            string detail,
            ulong generation = 0,
            ulong parentInvocationGeneration = 0,
            in SimulationMotionTraceDetail motionDetail = default)
        {
            if (!m_Enabled)
                return;
            SimulationTraceHeader header = m_Sequence.Next(source, generation);
            ulong graphInvocationGeneration = source.IsSkillOperation
                ? generation != 0
                    ? generation
                    : m_Frame.HasActionTraceContext
                        ? InvocationGeneration(source.Operation)
                        : 0
                : 0;
            ulong resolvedParentInvocationGeneration = source.IsSkillOperation
                ? parentInvocationGeneration != 0
                    ? parentInvocationGeneration
                    : m_Frame.HasActionTraceContext
                        ? ParentGeneration(source.Operation)
                        : 0
                : 0;
            m_Frame.AddTrace(new SimulationTraceRecord(
                header,
                severity,
                "Ability.Operation",
                code,
                detail,
                m_Frame.CurrentActionTraceInstanceId,
                m_Frame.CurrentActionTraceSkillId,
                m_Frame.CurrentSkillTraceGeneration,
                graphInvocationGeneration: graphInvocationGeneration,
                parentInvocationGeneration: resolvedParentInvocationGeneration,
                motionDetail: in motionDetail));
        }

        public void AddMotionWarp(in AbilityTimelineLogicMotionWarp warp, string code)
        {
            if (!CaptureMotion)
                return;
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(warp.NodeSource, warp.PlaybackGeneration),
                SimulationTraceSeverity.Detail,
                "Ability.MotionWarp",
                code,
                string.Empty,
                warp.ActionInstanceId,
                warp.AbilityId.Value,
                warp.SkillExecutionGeneration,
                graphInvocationGeneration: warp.GraphInvocationGeneration,
                parentInvocationGeneration: warp.ParentInvocationGeneration,
                timelineOperation: warp.Source.Operation,
                timelinePlaybackGeneration: warp.GraphInvocation.PlaybackGeneration,
                timelineTime: warp.CurrentTime.AsFloat,
                timelineCycle: warp.Cycle));
        }

        SimulationValueTrace? CaptureAdmissionEffect(ulong handle, SimulationActionResultKind result)
        {
            if (handle == 0)
                return null;
            AbilityStateValue value = AbilityStateValue.FromUInt64(handle);
            return m_Frame.CaptureValueTrace(
                result == SimulationActionResultKind.Rejected ? "GE.BlockingHandle" : "GE.OpportunityHandle",
                ProgramValuePortDirection.None, in value);
        }

        public void AddActionResult(
            SimulationOperation operation,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason,
            in GameplayAbilitySourceContext ruleSource = default,
            ulong effectHandle = 0)
        {
            if (!m_Enabled)
                return;
            SimulationTraceHeader header = m_Sequence.Next(operation);
            m_Frame.AddTrace(new SimulationTraceRecord(
                header,
                SimulationTraceSeverity.Information,
                "Ability.Action",
                "action_result",
                reason,
                actionInstanceId,
                skillId.Value,
                m_Frame.CurrentSkillTraceGeneration,
                graphInvocationGeneration: InvocationGeneration(operation.Handle),
                parentInvocationGeneration: ParentGeneration(operation.Handle),
                actionId: actionId,
                inputSequence: inputSequence,
                actionResult: result,
                valueTrace: CaptureAdmissionEffect(effectHandle, result),
                ruleSource: in ruleSource));
        }

        public void AddActionResult(
            SimulationExecutionSource source,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason,
            ulong generation = 0,
            ulong parentInvocationGeneration = 0,
            in GameplayAbilitySourceContext ruleSource = default,
            ulong effectHandle = 0)
        {
            if (!m_Enabled)
                return;
            SimulationTraceHeader header = m_Sequence.Next(source, generation);
            ulong graphInvocationGeneration = source.IsSkillOperation
                ? generation != 0
                    ? generation
                    : m_Frame.HasActionTraceContext
                        ? InvocationGeneration(source.Operation)
                        : 0
                : 0;
            ulong resolvedParentInvocationGeneration = source.IsSkillOperation
                ? parentInvocationGeneration != 0
                    ? parentInvocationGeneration
                    : m_Frame.HasActionTraceContext
                        ? ParentGeneration(source.Operation)
                        : 0
                : 0;
            m_Frame.AddTrace(new SimulationTraceRecord(
                header,
                SimulationTraceSeverity.Information,
                "Ability.Action",
                "action_result",
                reason,
                actionInstanceId,
                skillId.Value,
                m_Frame.CurrentSkillTraceGeneration,
                graphInvocationGeneration: graphInvocationGeneration,
                parentInvocationGeneration: resolvedParentInvocationGeneration,
                actionId: actionId,
                inputSequence: inputSequence,
                actionResult: result,
                valueTrace: CaptureAdmissionEffect(effectHandle, result),
                ruleSource: in ruleSource));
        }

        public void Add(TimelineTraceOutput output)
        {
            if (!m_Enabled)
                return;
            SimulationTraceSeverity severity = output.Severity switch
            {
                TimelineTraceSeverity.Detail => SimulationTraceSeverity.Detail,
                TimelineTraceSeverity.Information => SimulationTraceSeverity.Information,
                TimelineTraceSeverity.Warning => SimulationTraceSeverity.Warning,
                TimelineTraceSeverity.Error => SimulationTraceSeverity.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(output))
            };
            ulong actionInstanceId = output.ActionContext.IsValid
                ? output.ActionContext.InstanceId
                : m_Frame.CurrentActionTraceInstanceId;
            string skillId = output.ActionContext.HasSkillExecution
                ? output.ActionContext.SkillId.Value
                : m_Frame.CurrentActionTraceSkillId;
            ulong skillExecutionGeneration = output.ActionContext.HasSkillExecution
                ? output.ActionContext.SkillExecutionGeneration
                : m_Frame.CurrentSkillTraceGeneration;
            SimulationOperation operation = m_Frame.Layout.Operations[output.Operation.Value];
            m_Frame.AddTrace(new SimulationTraceRecord(
                m_Sequence.Next(operation),
                severity,
                "Ability.Timeline",
                output.Code,
                output.Detail,
                actionInstanceId,
                skillId,
                skillExecutionGeneration,
                graphInvocationGeneration: InvocationGeneration(output.Operation),
                parentInvocationGeneration: ParentGeneration(output.Operation),
                timelineOperation: output.TimelineOperation,
                timelinePlaybackGeneration: output.PlaybackGeneration,
                timelineTime: output.Time,
                timelineCycle: output.Cycle));
        }

        internal ulong ReadInvocationGeneration(OperationHandle operation) => InvocationGeneration(operation);

        internal CanonicalTextValue ReadInvocationPath(OperationHandle operation) =>
            m_Frame.Presentation.HasTreeClipInvocation
                ? m_Invocations.InvocationPath(operation, m_Frame.Presentation.TreeClipInvocation)
                : m_Invocations.InvocationPath(operation);


        internal ulong ReadParentInvocationGeneration(OperationHandle operation) => ParentGeneration(operation);

        ulong InvocationGeneration(OperationHandle operation)
        {
            if (!m_Frame.HasActionTraceContext)
                return 0;
            int slot = m_Frame.Presentation.HasTreeClipInvocation
                ? m_Invocations.GenerationSlot(operation, m_Frame.Presentation.TreeClipInvocation)
                : m_Invocations.GenerationSlot(operation);
            return slot >= 0 ? m_Frame.ReadState(slot).UInt64 : 0;
        }

        ulong ParentGeneration(OperationHandle operation)
        {
            if (!m_Frame.HasActionTraceContext)
                return 0;
            int slot = m_Frame.Presentation.HasTreeClipInvocation
                ? m_Invocations.ParentGenerationSlot(operation, m_Frame.Presentation.TreeClipInvocation)
                : m_Invocations.ParentGenerationSlot(operation);
            return slot >= 0 ? m_Frame.ReadState(slot).UInt64 : 0;
        }
    }
}
