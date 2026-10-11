using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    internal interface IFixedAbilityExecutionSavepointPort
    {
        FixedAbilityExecutionSavepoint CreateSavepoint();
        FixedAbilityExecutionSavepoint CreateActivationSavepoint();
        void Restore(FixedAbilityExecutionSavepoint savepoint);
        void Release(FixedAbilityExecutionSavepoint savepoint);
        int SavepointDepth { get; }
    }

    internal interface IFixedAbilityFrameReadPort
    {
        SimulationTick Tick { get; }
        ulong InputSequence { get; }
        bool HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId);
        FixedAbilityBodyFacts BodyFacts { get; }
        SimulationInputValue ReadInputValue(CanonicalTextValue inputId, ProgramInputValueKind kind);
        SimulationInputRequestState ReadInputRequest(CanonicalTextValue requestId);
        IReadOnlyList<FixedActionInstanceState> Actions { get; }
        IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> Pending { get; }
        bool TryReadAction(ulong instanceId, out FixedActionInstanceState action);
        AbilityStateValue ReadState(CharacterSkillId skillId, int slot, in GameplayAbilitySourceContext source);
        GameplayAbilityExecutionAggregate<AbilityStateValue> ReadAbilityState(CharacterSkillId skillId);
        EquipmentStateAggregate Equipment { get; }
    }

    internal sealed class FixedAbilityExecutionInput
    {
        SimulationIngress[] m_Ingress;
        int m_IngressCount;

        public ulong Sequence { get; private set; }
        public IReadOnlyList<SimulationInputValue> Values { get; private set; }

        public FixedAbilityExecutionInput()
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

        public void Clear()
        {
            Sequence = 0;
            m_Ingress = null;
            m_IngressCount = 0;
            Values = null;
        }
    }

    internal readonly struct FixedAbilityBodyFacts
    {
        public FixedAbilityBodyFacts(ActorId actorId, WorldBodyState body)
        {
            if (!actorId.IsValid || body.ActorId != actorId)
                throw new ArgumentException("Fixed Ability body facts identity is incomplete.", nameof(body));
            IsValid = true;
            Position = body.Position;
            Yaw = body.Yaw;
            Velocity = body.Velocity;
            VerticalVelocity = body.VerticalVelocity;
            Grounded = body.Grounded;
        }

        public bool IsValid { get; }
        public FixedVector3 Position { get; }
        public FixedYaw Yaw { get; }
        public FixedVector3 Velocity { get; }
        public FixedScalar VerticalVelocity { get; }
        public bool Grounded { get; }
    }

    internal interface IFixedSkillExecutionStateAccess
    {
        bool TryGet(int slotIndex, out AbilityStateValue value);
        bool TrySet(int slotIndex, AbilityStateValue value);
        bool TryReset(int slotIndex);
    }

    internal readonly struct FixedAbilityOutputSavepoint
    {
        public FixedAbilityOutputSavepoint(int factCount, int presentationCount, int gameplayEffectCount, int gameplayEffectCueCount,
            int gameplayEffectSuccessfulChangeCount)
        {
            FactCount = factCount;
            PresentationCount = presentationCount;
            GameplayEffectCount = gameplayEffectCount;
            GameplayEffectCueCount = gameplayEffectCueCount;
            GameplayEffectSuccessfulChangeCount = gameplayEffectSuccessfulChangeCount;
        }

        public int FactCount { get; }
        public int PresentationCount { get; }
        public int GameplayEffectCount { get; }
        public int GameplayEffectCueCount { get; }
        public int GameplayEffectSuccessfulChangeCount { get; }
    }

    internal readonly struct FixedAbilityInvocationContext
    {
        public FixedAbilityInvocationContext(
            ActorId actorId,
            SimulationTick tick,
            FixedAbilityExecutionInput input,
            FixedAbilityBodyFacts bodyFacts,
            IFixedSkillExecutionState skillState,
            IFixedAbilityExecutionSavepointPort savepointPort,
            IFixedInputRequestStatePort inputRequests,
            IFixedActionRuntimeStatePort actionState,
            IFixedHandleAllocatorStatePort handleAllocatorState,
            IFixedEventSequenceStatePort eventSequenceState,
            IFixedGameplayEffectStatePort gameplayEffectState,
            IFixedEquipmentStatePort equipmentState,
            FixedCombatQueryStorage combatQueries,
            FixedCombatRuntime combat)
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
        public FixedAbilityExecutionInput Input { get; }
        public FixedAbilityBodyFacts BodyFacts { get; }
        public IFixedSkillExecutionState SkillState { get; }
        public IFixedAbilityExecutionSavepointPort SavepointPort { get; }
        public IFixedInputRequestStatePort InputRequests { get; }
        public IFixedActionRuntimeStatePort ActionState { get; }
        public IFixedHandleAllocatorStatePort HandleAllocatorState { get; }
        public IFixedEventSequenceStatePort EventSequenceState { get; }
        public IFixedGameplayEffectStatePort GameplayEffectState { get; }
        public IFixedEquipmentStatePort EquipmentState { get; }
        internal FixedCombatQueryStorage CombatQueries { get; }
        internal FixedCombatRuntime Combat { get; }
    }

    internal sealed class FixedAbilityExecutionFrame
    {
        readonly List<GameplayFact> m_Facts;
        readonly List<PresentationCommand> m_Presentation;
        readonly List<SimulationTraceRecord> m_Trace;
        readonly FixedAbilityExecutionWorkspace m_Workspace;
        FixedAbilityBodyFacts m_BodyFacts;
        IFixedGameplayEffectStatePort m_GameplayEffectState;
        IFixedEquipmentStatePort m_EquipmentState;
        IFixedSkillExecutionStateAccess m_SkillExecutionStateAccess;
        ulong m_ActionTraceInstanceId;
        string m_ActionTraceSkillId = string.Empty;
        OperationHandle m_ActionTraceEntryOperation = OperationHandle.Invalid;
        AbilityTreeClipInvocation m_TreeClipInvocation;

        public FixedAbilityExecutionFrame(
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout,
            FixedGameplayAbilityExecutionServices services,
            ActorId actorId,
            FixedTraceSink trace,
            FixedAbilityExecutionWorkspace workspace)
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
            EventSequence = new FixedEventSequence(this);
            Facts = new FixedFactSink(this, EventSequence);
            Presentation = new FixedPresentationSink(this, EventSequence);
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            Trace.Bind(this);
        }

        internal IFixedAbilityFrameReadPort AvailabilityRead { get; private set; }
        internal IFixedAbilityFrameReadPort ExchangeAvailabilityRead(IFixedAbilityFrameReadPort read)
        {
            IFixedAbilityFrameReadPort previous = AvailabilityRead;
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
        internal IReadOnlyList<FixedActionInstanceState> ReadActionInstances() =>
            AvailabilityRead == null ? ActionState.GetActionInstances() : AvailabilityRead.Actions;
        internal IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> ReadPendingRequests() =>
            AvailabilityRead == null ? ActionState.GetActionActivationRequests() : AvailabilityRead.Pending;
        internal bool TryReadAction(ulong instanceId, out FixedActionInstanceState action) =>
            AvailabilityRead == null ? ActionState.TryGetActionInstance(instanceId, out action) : AvailabilityRead.TryReadAction(instanceId, out action);
        internal GameplayAbilityExecutionAggregate<AbilityStateValue> ReadAbilityState() =>
            AvailabilityRead == null ? SkillState.GetAbilityExecutionState() : AvailabilityRead.ReadAbilityState(Data.AbilityId);
        internal EquipmentStateAggregate ReadEquipmentState() =>
            AvailabilityRead == null ? EquipmentState.GetEquipmentState() : AvailabilityRead.Equipment;

        internal IGameplayAbilityDecisionReadPort DecisionRead { get; set; }
        internal GameplayAbilitySourceContext SelectedSource;
        internal GameplayAbilityRuleEvaluationMode RuleEvaluationMode { get; set; }

        internal void Begin(in FixedAbilityInvocationContext context)
        {
            if (!context.ActorId.IsValid || !context.Tick.IsValid)
                throw new ArgumentException("Fixed Ability invocation identity is incomplete.");
            if (context.ActorId != ActorId)
                throw new InvalidOperationException("Fixed Ability invocation actor does not match its frame.");
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

        internal FixedValuePortInputs ValuePorts { get; set; }
        public FixedGameplayAbilityExecutionData Data { get; }
        public GameplayAbilityExecutionLayout Layout { get; }
        internal FixedGameplayAbilityExecutionServices Services { get; }
        public GameplayAbilityExecutionIdentity Identity => Services.Identity;
        public SimulationNumericProfile NumericProfile => Data.NumericProfile;
        public ActorId ActorId { get; }
        SimulationTick m_Tick;
        public SimulationTick Tick => AvailabilityRead == null ? m_Tick : AvailabilityRead.Tick;
        public FixedAbilityExecutionInput Input { get; private set; }
        public FixedAbilityBodyFacts BodyFacts => AvailabilityRead != null ? AvailabilityRead.BodyFacts : m_BodyFacts.IsValid
            ? m_BodyFacts
            : throw new InvalidOperationException("Fixed Ability invocation has no Body Facts service.");
        internal IFixedSkillExecutionState SkillState { get; private set; }
        internal IFixedAbilityExecutionSavepointPort SavepointPort { get; private set; }
        internal IFixedInputRequestStatePort InputRequests { get; private set; }
        internal IFixedActionRuntimeStatePort ActionState { get; private set; }
        internal IFixedHandleAllocatorStatePort HandleAllocatorState { get; private set; }
        internal IFixedEventSequenceStatePort EventSequenceState { get; private set; }
        internal IFixedGameplayEffectStatePort GameplayEffectState => m_GameplayEffectState ??
            throw new InvalidOperationException("Fixed Ability invocation has no Gameplay Effect state service.");
        internal IFixedEquipmentStatePort EquipmentState => m_EquipmentState ??
            throw new InvalidOperationException("Fixed Ability invocation has no Equipment state service.");
        internal FixedEventSequence EventSequence { get; }
        internal FixedFactSink Facts { get; }
        internal FixedPresentationSink Presentation { get; }
        internal FixedCombatQueryStorage CombatQueries { get; private set; }
        internal FixedCombatRuntime Combat { get; private set; }
        internal FixedTraceSink Trace { get; }

        internal bool HasTreeClipInvocation => m_TreeClipInvocation.ActionInstanceId != 0;

        internal ulong TreeClipActionInstanceId => m_TreeClipInvocation.ActionInstanceId;

        internal AbilityTreeClipInvocation TreeClipInvocation => HasTreeClipInvocation
            ? m_TreeClipInvocation
            : throw new InvalidOperationException("Camera presentation requires an active TreeClip invocation.");

        internal AbilityTreeClipInvocation ExchangeTreeClipInvocation(in AbilityTreeClipInvocation invocation)
        {
            AbilityTreeClipInvocation previous = m_TreeClipInvocation;
            m_TreeClipInvocation = invocation;
            return previous;
        }

        internal FixedStatePort CreateStatePort(string owner, FixedStateAccessPolicy policy) =>
            new FixedStatePort(this, owner, policy);

        internal FixedOperationStateReset CreateOperationStateReset() =>
            new FixedOperationStateReset(this);

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

        internal void BindSkillExecutionStateAccess(IFixedSkillExecutionStateAccess access)
        {
            if (access == null)
                throw new ArgumentNullException(nameof(access));
            if (m_SkillExecutionStateAccess != null && !ReferenceEquals(m_SkillExecutionStateAccess, access))
                throw new InvalidOperationException("Fixed ability frame is already bound to another Skill execution state owner.");
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

        internal FixedAbilityOutputSavepoint CreateOutputSavepoint() =>
            new FixedAbilityOutputSavepoint(m_Facts.Count, m_Presentation.Count, m_Workspace.GameplayEffectProjectionCount,
                m_Workspace.GameplayEffectCueCount, m_Workspace.GameplayEffectSuccessfulChangeCount);

        internal void RestoreOutput(FixedAbilityOutputSavepoint savepoint)
        {
            m_Facts.RemoveRange(savepoint.FactCount, m_Facts.Count - savepoint.FactCount);
            m_Presentation.RemoveRange(savepoint.PresentationCount, m_Presentation.Count - savepoint.PresentationCount);
            m_Workspace.TruncateGameplayEffectProjections(savepoint.GameplayEffectCount);
            m_Workspace.GameplayEffectCueCount = savepoint.GameplayEffectCueCount;
            m_Workspace.GameplayEffectSuccessfulChangeCount = savepoint.GameplayEffectSuccessfulChangeCount;
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
            FixedScalar sampleTime = default;
            if (change.Kind == PortableEffectChangeKind.Cue)
            {
                producerId = Services.RequireGameplayCueProducer(change.Definition.Id, change.CueId).Identity;
                presentationHeader = Presentation.Next(source);
                presentationIndex = m_Presentation.Count;
                sampleTime = Presentation.HasTreeClipInvocation ? Presentation.TreeClipInvocation.Time : FixedScalar.Zero;
                m_Presentation.Add(default);
            }
            var projection = new FixedGameplayEffectPendingProjection(
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
            m_Workspace.TraceOutput.ReserveTraceRecord();
            value.Retain();
            m_Trace.Add(value);
        }

        internal struct ActionTraceContextScope : IDisposable
        {
            readonly FixedAbilityExecutionFrame m_Owner;
            readonly ulong m_PreviousInstanceId;
            readonly string m_PreviousSkillId;
            readonly OperationHandle m_PreviousEntryOperation;
            bool m_Disposed;

            public ActionTraceContextScope(
                FixedAbilityExecutionFrame owner,
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

    internal readonly struct FixedOperationStateReset
    {
        readonly FixedAbilityExecutionFrame m_Frame;

        public FixedOperationStateReset(FixedAbilityExecutionFrame frame)
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

    internal readonly struct FixedStatePort
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedStateAccessPolicy m_Policy;
        readonly string m_Owner;

        public FixedStatePort(
            FixedAbilityExecutionFrame frame,
            string owner,
            FixedStateAccessPolicy policy)
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

    internal readonly struct FixedEventSequence
    {
        readonly FixedAbilityExecutionFrame m_Frame;

        public FixedEventSequence(FixedAbilityExecutionFrame frame)
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

    internal readonly struct FixedFactSink
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedEventSequence m_Sequence;

        public FixedFactSink(FixedAbilityExecutionFrame frame, FixedEventSequence sequence)
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

    internal readonly struct FixedPresentationSink
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        readonly FixedEventSequence m_Sequence;

        public FixedPresentationSink(FixedAbilityExecutionFrame frame, FixedEventSequence sequence)
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
        internal AbilityTreeClipInvocation TreeClipInvocation => m_Frame.TreeClipInvocation;

        internal AbilityTreeClipInvocation ExchangeTreeClipInvocation(in AbilityTreeClipInvocation invocation)
            => m_Frame.ExchangeTreeClipInvocation(invocation);
    }

    internal struct FixedDiagnosticSequence
    {
        readonly FixedAbilityExecutionFrame m_Frame;
        ulong m_Sequence;

        public FixedDiagnosticSequence(FixedAbilityExecutionFrame frame)
        {
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Sequence = 0;
        }

        public void Reset()
        {
            m_Sequence = 0;
        }

        public SimulationEventHeader Next(SimulationOperation operation)
        {
            int generationSlot = m_Frame.Layout.FindOperationStateSlot(
                operation.Handle,
                ProgramStateSemantic.RunnableActivationGeneration);
            ulong generation = generationSlot < 0 ? 1UL : m_Frame.ReadState(generationSlot).UInt64;
            return Next(
                SimulationExecutionSource.FromSkillOperation(
                    operation.Handle,
                    m_Frame.Services.SourcePath(operation.Handle)),
                generation);
        }

        public SimulationEventHeader Next(SimulationExecutionSource source, ulong generation = 1)
        {
            ulong sequence = checked(++m_Sequence);
            if (generation == 0)
                generation = 1;
            var activation = new ActivationId(source, generation);
            AbilityTreeClipInvocation? treeClipInvocation = m_Frame.HasTreeClipInvocation
                ? m_Frame.TreeClipInvocation
                : null;
            GameplayContentHash contentHash = new GameplayContentHash(m_Frame.Identity.ContentHash);
            EventId eventId = treeClipInvocation.HasValue
                ? EventIdBuilder.CreateTreeClip(contentHash, m_Frame.ActorId, activation, m_Frame.Tick,
                    sequence, "Trace", treeClipInvocation.Value)
                : EventIdBuilder.Create(contentHash, m_Frame.ActorId, activation, m_Frame.Tick, sequence, "Trace");
            return new SimulationEventHeader(
                m_Frame.NumericProfile,
                eventId,
                m_Frame.ActorId,
                m_Frame.Tick,
                activation,
                sequence,
                "Trace",
                treeClipInvocation);
        }
    }

    internal sealed class FixedTraceSink
    {
        readonly HashSet<(int Operation, string Port, ProgramValuePortDirection Direction)> m_ValuePorts = new();
        readonly HashSet<string> m_EdgeIds = new(StringComparer.Ordinal);
        readonly GameplayAbilityGraphInvocationLayout m_Invocations;
        readonly Dictionary<(int Target, string Port), ProgramControlFlowEdge> m_ValueEdges = new();
        int m_ValueSampleCount;
        FixedAbilityExecutionFrame m_Frame;
        FixedDiagnosticSequence m_Sequence;
        bool m_Enabled;

        public FixedTraceSink(
            FixedGameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout)
        {
            m_Invocations = new GameplayAbilityGraphInvocationLayout(data.SourceMap, layout.Operations.Count,
                owner => layout.FindOperationStateSlot(owner, ProgramStateSemantic.RunnableActivationGeneration));
            foreach (ProgramSourceMapEntry source in data.SourceMap)
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

        internal void Bind(FixedAbilityExecutionFrame frame)
        {
            m_Frame = frame;
            m_Sequence = new FixedDiagnosticSequence(frame);
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
            SimulationEventHeader header = m_Sequence.Next(operation);
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
            SimulationEventHeader header = m_Sequence.Next(source, generation);
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
                timelineTime: warp.CurrentTime.ToSingle(),
                timelineCycle: warp.Cycle));
        }

        public void AddActionResult(
            SimulationOperation operation,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason,
            in GameplayAbilitySourceContext ruleSource = default)
        {
            if (!m_Enabled)
                return;
            SimulationEventHeader header = m_Sequence.Next(operation);
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
            in GameplayAbilitySourceContext ruleSource = default)
        {
            if (!m_Enabled)
                return;
            SimulationEventHeader header = m_Sequence.Next(source, generation);
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
