using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal sealed class Float32MotionContributionScratch
    {
        SimulationMotionContribution[] m_Values = Array.Empty<SimulationMotionContribution>();

        public SimulationMotionContribution[] Values => m_Values;
        public int Count { get; private set; }

        public void Begin()
        {
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }

        public void Append(in SimulationMotionContribution value)
        {
            if (Count == m_Values.Length)
            {
                int capacity = Math.Max(4, m_Values.Length * 2);
                var values = new SimulationMotionContribution[capacity];
                Array.Copy(m_Values, values, Count);
                m_Values = values;
            }

            m_Values[Count++] = value;
        }

        public void Clear()
        {
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }
    }

    internal sealed class Float32CharacterEvaluationOutput
    {
        internal Float32CharacterEvaluationOutput(IReadOnlyList<Float32GameplayAbilityExecutionInstallation> installations)
        { m_TraceInstallations = installations; ValueTraces = new SimulationValueTraceStorage(installations.Count); }
        readonly IReadOnlyList<Float32GameplayAbilityExecutionInstallation> m_TraceInstallations;
        internal SimulationStructuredTraceStorage StructuredTraces { get; private set; }
        internal SimulationValueTraceStorage ValueTraces { get; }
        int m_TraceRecordsPerTick;
        int m_TraceRecordCount;
        SimulationSessionCompositionException m_TraceCapacityFailure;
        internal void PrepareTraceStorage(int traceRecordsPerTick)
        {
            m_TraceRecordsPerTick = traceRecordsPerTick;
            m_TraceCapacityFailure = PipelineTransactionFailure.Create(SimulationSessionFailureStage.Step,
                traceRecordsPerTick == 0 ? "diagnostic_trace_capacity_missing" : "diagnostic_trace_capacity_exceeded",
                traceRecordsPerTick == 0 ? "Enabled diagnostics requires an authored TraceRecordsPerTick."
                    : "Enabled diagnostics exceeded the authored TraceRecordsPerTick; the Tick is rolled back.");
            Trace.Capacity = traceRecordsPerTick;
            CharacterTrace.Capacity = traceRecordsPerTick;
            if (traceRecordsPerTick != 0)
            {
                ValueTraces.PrepareCapacity(traceRecordsPerTick);
                StructuredTraces = new SimulationStructuredTraceStorage(m_TraceInstallations, traceRecordsPerTick);
            }
        }
        internal void BeginDiagnostics(bool enabled)
        {
            if (enabled && m_TraceRecordsPerTick == 0)
                throw m_TraceCapacityFailure;
        }
        internal void ReserveTraceRecord()
        {
            if (m_TraceRecordCount == m_TraceRecordsPerTick)
                throw m_TraceCapacityFailure;
            m_TraceRecordCount++;
        }
        internal void BindValueTraces(Float32AbilityExecutionWorkspace[] workspaces)
        {
            for (int index = 0; index < workspaces.Length; index++)
            { workspaces[index].TraceOutput = this; workspaces[index].ValueTraces = ValueTraces; workspaces[index].StructuredTraces = StructuredTraces; workspaces[index].ValueTraceInvocation = index; }
        }
        public SimulationActorTickResult TickResult { get; } = new SimulationActorTickResult();
        internal GameplayCommandBatchAdmission IngressAdmission { get; set; }
        internal List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;
        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        public List<SimulationTraceRecord> CharacterTrace { get; } = new List<SimulationTraceRecord>();
        internal Float32GameplayEffectPendingProjection[] GameplayEffectProjections { get; private set; } =
            Array.Empty<Float32GameplayEffectPendingProjection>();
        internal int GameplayEffectProjectionCount { get; set; }
        internal MotionWarpStatePageReference<Float32MotionWarpState> MotionWarpStates { get; private set; }
        Float32CharacterRuntimeState m_StatePageOwner;

        internal void RetainStatePages(Float32CharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner = state;
        }

        internal void ReplaceStatePageOwner(Float32CharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner.ReleaseStatePages();
            m_StatePageOwner = state;
        }

        internal void BeginMotionWarpState(
            MotionWarpStatePageBank<Float32MotionWarpState> bank,
            in MotionWarpStatePageReference<Float32MotionWarpState> source)
        {
            MotionWarpStates = bank.BeginWrite(source);
            MotionWarpStates.Retain();
        }

        public void Clear()
        {
            if (m_StatePageOwner != null)
            {
                m_StatePageOwner.ReleaseStatePages();
                m_StatePageOwner = null;
            }
            if (MotionWarpStates.Generation != 0)
            {
                MotionWarpStates.Release();
                MotionWarpStates = default;
            }
            for (int i = 0; i < Facts.Count; i++) Facts[i].Release();
            Facts.Clear();
            for (int i = 0; i < Presentation.Count; i++) Presentation[i].Release();
            Presentation.Clear();
            for (int index = 0; index < Trace.Count; index++)
                Trace[index].Release();
            Trace.Clear();
            for (int index = 0; index < CharacterTrace.Count; index++)
                CharacterTrace[index].Release();
            CharacterTrace.Clear();
            ValueTraces.Clear();
            m_TraceRecordCount = 0;
            if (m_TraceRecordsPerTick != 0) StructuredTraces.Clear();
            for (int i = 0; i < GameplayEffectProjectionCount; i++) GameplayEffectProjections[i].Release();
            Array.Clear(GameplayEffectProjections, 0, GameplayEffectProjectionCount);
            GameplayEffectProjectionCount = 0;
            IngressAdmission = default;
            CommandBatchRejections.Clear();
        }

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation)
        {
            GameplayEffectProjections = new Float32GameplayEffectPendingProjection[changes];
            Facts.Capacity = facts;
            Presentation.Capacity = presentation;
        }

        internal void FinalizeGameplayEffects()
        {
            for (int index = 0; index < GameplayEffectProjectionCount; index++)
                GameplayEffectProjections[index].Project(Facts, Presentation);
        }
    }

    public sealed class SimulationActorBinding : IDisposable, IFloat32AbilityAvailabilityReadOwner, IFloat32AbilityFrameReadPort
    {
        readonly Float32CharacterControlMotionRuntime m_ControlMotion;
        readonly Float32MotionContributionScratch m_MotionContributions = new Float32MotionContributionScratch();
        readonly Float32AbilityInvocationRuntime[] m_Invocations;
        readonly Dictionary<CharacterSkillId, Float32AbilityInvocationRuntime> m_InvocationByAbility;
        readonly Dictionary<CharacterSkillId, IFloat32AbilityActivationPort> m_ActionRuntimes;
        readonly Float32GameplayEffectExecutionScratch m_EffectExecutionScratch = new Float32GameplayEffectExecutionScratch();
        readonly List<AbilityTimelineAdvancePending> m_TimelineAdvances = new List<AbilityTimelineAdvancePending>();
        readonly List<AbilityTimelineStopPending> m_TimelineStops = new List<AbilityTimelineStopPending>();
        readonly Dictionary<ulong, Float32AbilityInvocationRuntime> m_ActionOwnerByInstanceId;
        readonly List<AbilityTimelineLogicMotion> m_TimelineLogicMotion = new List<AbilityTimelineLogicMotion>();
        readonly Float32AbilityExecutionWorkspace[] m_Workspaces;
        Float32CharacterEvaluationOutput m_EvaluationOutput;
        readonly Stack<Float32CharacterEvaluationOutput> m_RecycledEvaluationOutputs =
            new Stack<Float32CharacterEvaluationOutput>();
        readonly Float32CharacterEvaluationResult m_EvaluationResult;
        readonly int m_AbilityFactCapacity;
        readonly int m_AbilityPresentationCapacity;
        int m_GameplayEffectChangeCapacity;
        int m_TraceRecordsPerTick;
        int m_FactCapacity;
        int m_OutputPresentationCapacity;
        ActionAdmissionProfile[] m_CodecActionProfiles = Array.Empty<ActionAdmissionProfile>();
        readonly Float32CharacterTraceSink m_CharacterTraceSink;
        readonly Float32AbilityExecutionInput m_AbilityExecutionInput = new Float32AbilityExecutionInput();
        readonly Float32CharacterInputRequestState m_InputRequestState = new Float32CharacterInputRequestState();
        readonly Float32CharacterActionRuntimeState m_ActionState;
        readonly CharacterControlRuntimeStateTransaction m_ControlState = new CharacterControlRuntimeStateTransaction();
        readonly Float32CharacterEventSequenceState m_EventSequenceState = new Float32CharacterEventSequenceState();
        readonly Float32CharacterHandleAllocatorState m_HandleAllocatorState = new Float32CharacterHandleAllocatorState();
        readonly Float32CharacterGameplayEffectRuntimeState m_GameplayEffectState = new Float32CharacterGameplayEffectRuntimeState();
        readonly Float32CharacterEquipmentRuntimeState m_EquipmentState = new Float32CharacterEquipmentRuntimeState();
        readonly Float32CharacterRuntimeStateTransaction m_RuntimeState = new Float32CharacterRuntimeStateTransaction();

        public SimulationActorBinding(
            ActorId actorId,
            string worldBodyBindingId,
            CharacterControlRuntimeBinding controlRuntimeBinding,
            CharacterInputRuntimeBinding inputRuntimeBinding,
            CharacterBodyMotionBinding bodyMotionBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectRuntimeBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GameplayAbilityExecutionDataSet<Float32GameplayAbilityExecutionData> abilityData,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            IAbilityTimelineRuntime timelineRuntime,
            CharacterCombatRuntimeBinding combatBinding = null)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Actor identity is invalid.", nameof(actorId));
            ActorId = actorId;
            CombatBinding = combatBinding;
            WorldBodyBindingId = SimulationIdentity.Require(worldBodyBindingId, nameof(worldBodyBindingId));
            ControlRuntimeBinding = controlRuntimeBinding ?? throw new ArgumentNullException(nameof(controlRuntimeBinding));
            InputRuntimeBinding = inputRuntimeBinding;
            m_ControlMotion = new Float32CharacterControlMotionRuntime(controlRuntimeBinding.MotionBindings);
            BodyMotionBinding = bodyMotionBinding ?? throw new ArgumentNullException(nameof(bodyMotionBinding));
            AbilityInstallations = new Float32GameplayAbilityExecutionInstallationSet(
                abilityData ?? throw new ArgumentNullException(nameof(abilityData)),
                inputRuntimeBinding,
                gameplayEffectRuntimeBinding,
                equipmentRuntimeBinding,
                blackboards);
            m_EvaluationOutput = new Float32CharacterEvaluationOutput(AbilityInstallations.Installations);
            var warpLayouts = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>[AbilityInstallations.Installations.Count];
            for (int index = 0; index < warpLayouts.Length; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                warpLayouts[index] = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>(
                    installation.Identity.AbilityId, installation.Layout.MotionWarpOperationIds);
            }
            var motionWarpLayout = new MotionWarpStatePageLayout(warpLayouts);
            MotionWarpPages = new MotionWarpStatePageBank<Float32MotionWarpState>(motionWarpLayout,
                state => state.Retain(), state => state.Release());
            RuntimeStatePages = new Float32CharacterRuntimeStatePageBank(AbilityInstallations.Installations.Count);
            StateValuePages = new Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>>(AbilityInstallations.Installations.Count);
            ExecutionStatePages = new Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>>(AbilityInstallations.Installations.Count);
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                StateValuePages.Add(installation.Identity.AbilityId, new AbilityStateValuePageBank<AbilityStateValue>(
                    installation.Identity, installation.Layout.StateSlots.Count, slot => !installation.Layout.IsSkillExecutionStateSlot(slot) &&
                        AbilityInstallations.CharacterBlackboards.Slots(installation.Data.AbilityId)[slot] < 0,
                    value => value.Retain(), value => value.Release(), installation.Blackboards,
                    slot => installation.Layout.StateSlots[slot].ValueKind,
                    AbilityStateValue.CanonicalWrite<CanonicalWriter>, AbilityStateValue.CanonicalReadCase<CanonicalReader>));
                ExecutionStatePages.Add(installation.Identity.AbilityId, new GameplayAbilityExecutionStateBank<AbilityStateValue>(
                    installation.Identity, installation.Layout.StateSlots.Count, installation.Layout.ActionInstanceCapacity,
                    installation.Layout.IsSkillExecutionStateSlot, value => value.Retain(), value => value.Release(), installation.Blackboards,
                    slot => installation.Layout.StateSlots[slot].ValueKind,
                    AbilityStateValue.CanonicalWrite<CanonicalWriter>, AbilityStateValue.CanonicalReadCase<CanonicalReader>));
            }
            CharacterBlackboardPages = new AbilityStateValuePageBank<AbilityStateValue>(default,
                AbilityInstallations.CharacterBlackboards.SlotCount, _ => true,
                value => value.Retain(), value => value.Release(), AbilityInstallations.CharacterBlackboards.Binding,
                slot => AbilityInstallations.CharacterBlackboards.Kinds[slot],
                AbilityStateValue.CanonicalWrite<CanonicalWriter>, AbilityStateValue.CanonicalReadCase<CanonicalReader>);
            m_RuntimeState.Initialize(AbilityInstallations.Installations, motionWarpLayout, AbilityInstallations.CharacterBlackboards);
            InputRequestPages = new Float32InputRequestPageBank(InputRuntimeBinding.InputRequestIds.Count);
            int actionCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                actionCapacity = checked(actionCapacity + AbilityInstallations.Installations[i].Layout.ActionInstanceCapacity);
            ActionStatePages = new Float32ActionStatePageBank(actionCapacity);
            m_ActionState = new Float32CharacterActionRuntimeState(actionCapacity, AbilityInstallations.Installations);
            m_ActionOwnerByInstanceId = new Dictionary<ulong, Float32AbilityInvocationRuntime>(actionCapacity);
            m_Invocations = new Float32AbilityInvocationRuntime[AbilityInstallations.Installations.Count];
            m_InvocationByAbility = new Dictionary<CharacterSkillId, Float32AbilityInvocationRuntime>(
                AbilityInstallations.Installations.Count);
            m_ActionRuntimes = new Dictionary<CharacterSkillId, IFloat32AbilityActivationPort>(
                AbilityInstallations.Installations.Count);
            m_Workspaces = new Float32AbilityExecutionWorkspace[AbilityInstallations.Installations.Count];
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                Float32GameplayAbilityExecutionData data = AbilityInstallations.Installations[i].Data;
                if (data.NumericProfile != Float32SimulationNumericProfile.Value)
                    throw new InvalidOperationException($"Ability '{data.AbilityId}' does not target Float32.");
                m_Workspaces[i] = new Float32AbilityExecutionWorkspace(
                    m_EffectExecutionScratch,
                    m_TimelineAdvances,
                    m_TimelineStops,
                    new Float32GraphValueWorkspace(data),
                    AbilityInstallations.Installations[i].Layout.Topology.FactRecordCapacity,
                    AbilityInstallations.Installations[i].Layout.Topology.PresentationRecordCapacity,
                    AbilityInstallations.Installations[i].Layout.ActionInstanceCapacity);
            }
            GameplayEffectRuntimeBinding = gameplayEffectRuntimeBinding;
            EquipmentRuntimeBinding = equipmentRuntimeBinding;
            TimelineRuntime = timelineRuntime;
            int factCapacity = 0;
            int presentationCapacity = 0;
            for (int i = 0; i < m_Workspaces.Length; i++)
            {
                factCapacity = checked(factCapacity + m_Workspaces[i].Facts.Capacity);
                presentationCapacity = checked(presentationCapacity + m_Workspaces[i].Presentation.Capacity);
            }
            m_AbilityFactCapacity = factCapacity;
            m_AbilityPresentationCapacity = presentationCapacity;
            m_OutputPresentationCapacity = presentationCapacity;
            m_FactCapacity = factCapacity;
            m_EvaluationOutput.Facts.Capacity = m_FactCapacity;
            m_EvaluationOutput.Presentation.Capacity = presentationCapacity;
            int timelineRequestCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                timelineRequestCapacity = checked(timelineRequestCapacity +
                    AbilityInstallations.Installations[i].Layout.Topology.Operations.Count);
            m_TimelineAdvances.Capacity = timelineRequestCapacity;
            m_TimelineStops.Capacity = timelineRequestCapacity;
            m_EvaluationResult = new Float32CharacterEvaluationResult(this, actorId, timelineRuntime,
                timelineRequestCapacity);
            IFloat32AbilityDomainRuntimeFactory domainRuntimeFactory = new Float32AbilityDomainRuntimeFactory();
            IFloat32AbilityExecutionServiceFactory serviceFactory = new Float32AbilityExecutionServiceFactory(timelineRuntime, m_ActionRuntimes);
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[i];
                m_Invocations[i] = new Float32AbilityInvocationRuntime(
                    installation.Execution,
                    AbilityInstallations,
                    domainRuntimeFactory,
                    installation.EquipmentLayout,
                    m_Workspaces[i],
                    serviceFactory);
                m_Invocations[i].PrepareActor(actorId);
                m_InvocationByAbility.Add(installation.Data.AbilityId, m_Invocations[i]);
            }

            m_AvailabilityActions = new ActionSnapshotView(this);
            m_AvailabilityPending = new PendingSnapshotView(this);
            ActivationRules = new Float32AbilityActivationRuleRuntime(this);
            for (int i = 0; i < m_Invocations.Length; i++)
                m_Invocations[i].BindDecisionRead(ActivationRules);
            m_CharacterTraceSink = new Float32CharacterTraceSink(m_EvaluationOutput.CharacterTrace);
            TimelineMotionReader = GetMotionReader(timelineRuntime);
            StateSchemaHash = ComputeStateSchemaHash(
                controlRuntimeBinding,
                inputRuntimeBinding,
                gameplayEffectRuntimeBinding,
                equipmentRuntimeBinding,
                AbilityInstallations);
            GameplayContentHash = ComputeGameplayContentHash(
                controlRuntimeBinding,
                inputRuntimeBinding,
                bodyMotionBinding,
                gameplayEffectRuntimeBinding,
                equipmentRuntimeBinding,
                AbilityInstallations);
        }

        internal Float32AbilityActivationRuleRuntime ActivationRules { get; }
        public ActorId ActorId { get; }
        public CharacterCombatRuntimeBinding CombatBinding { get; }
        public string WorldBodyBindingId { get; }
        public CharacterControlRuntimeBinding ControlRuntimeBinding { get; }
        public CharacterInputRuntimeBinding InputRuntimeBinding { get; }
        internal Float32CharacterControlMotionRuntime ControlMotion => m_ControlMotion;
        internal Float32MotionContributionScratch MotionContributions => m_MotionContributions;
        internal int AvailabilityIngressCapacity { get; private set; }
        internal void PrepareAvailabilityIngressCapacity(int capacity) => AvailabilityIngressCapacity = capacity;
        IFloat32CommittedInputReadPort m_CommittedInput;
        internal void BindCommittedInput(IFloat32CommittedInputReadPort input) => m_CommittedInput = input;
        internal void CaptureAvailabilityFrameEvents(SimulationIngress[] ingress, int count)
        {
            if (m_CommittedInput != null)
                m_CommittedInput.CaptureFrameEvents(ActorId, new ReadOnlySpan<SimulationIngress>(ingress, 0, count));
        }
        bool IFloat32AbilityFrameReadPort.HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId) =>
            m_AvailabilityInput.HasActionEvent(ActorId, instanceId, eventId, targetId);

        Float32CharacterRuntimeState m_AvailabilityState;
        IFloat32CommittedInputReadPort m_AvailabilityInput;
        Float32CharacterControlRuntime m_AvailabilityControl;
        readonly ActionSnapshotView m_AvailabilityActions;
        readonly PendingSnapshotView m_AvailabilityPending;
        Float32AbilityBodyFacts m_AvailabilityBody;

        internal void QueryAbilityAvailability(Float32CharacterRuntimeState state,
            WorldBodyState body, IFloat32CommittedInputReadPort input, Float32CharacterControlRuntime control,
            GameplayAbilityRulePhase phase, out GameplayAbilityAvailabilityResult result)
        {
            state.RetainStatePages();
            m_AvailabilityState = state;
            m_AvailabilityInput = input;
            m_AvailabilityControl = control;
            m_AvailabilityBody = new Float32AbilityBodyFacts(ActorId, body);
            try
            {
                if (AbilityInstallations.GameplayEffectCatalog != null)
                    m_EffectExecutionScratch.Target.BeginAvailabilityRead(state.GameplayEffectState,
                        AbilityInstallations.GameplayEffectCatalog, ActorId, ((IFloat32AbilityFrameReadPort)this).Tick);
                for (int index = 0; index < m_Invocations.Length; index++)
                    m_Invocations[index].BindAvailabilityRead(this);
                ActivationRules.QueryAvailability(phase, this, out result);
            }
            finally
            {
                for (int index = 0; index < m_Invocations.Length; index++)
                    m_Invocations[index].BindAvailabilityRead(null);
                if (AbilityInstallations.GameplayEffectCatalog != null)
                    m_EffectExecutionScratch.Target.EndAvailabilityRead();
                m_AvailabilityState = default;
                m_AvailabilityInput = null;
                m_AvailabilityControl = null;
                m_AvailabilityBody = default;
                state.ReleaseStatePages();
            }
        }

        SimulationTick IFloat32AbilityFrameReadPort.Tick => new SimulationTick(m_AvailabilityState.LastCompletedTick);
        SimulationTick IFloat32AbilityAvailabilityReadOwner.Tick => ((IFloat32AbilityFrameReadPort)this).Tick;
        ulong IFloat32AbilityFrameReadPort.InputSequence => m_AvailabilityInput.ReadInputSequence(ActorId);
        Float32AbilityBodyFacts IFloat32AbilityFrameReadPort.BodyFacts => m_AvailabilityBody;
        bool IFloat32AbilityAvailabilityReadOwner.HasMoveInput => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).HasMoveInput;
        CharacterLocomotionMode IFloat32AbilityAvailabilityReadOwner.LocomotionMode => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).LocomotionMode;
        int IFloat32AbilityAvailabilityReadOwner.ActionCount => m_AvailabilityState.ActionInstances.Length;
        GameplayAbilitySourceContext IFloat32AbilityAvailabilityReadOwner.ReadActiveSource(int index)
        {
            ref readonly Float32ActionInstanceState action = ref m_AvailabilityState.ActionInstances[index];
            return action.IsActive ? action.SourceContext : default;
        }
        ulong IFloat32AbilityAvailabilityReadOwner.ReadSourceStartTick(int index) => m_AvailabilityState.ActionInstances[index].StartTick;
        bool IFloat32AbilityAvailabilityReadOwner.TryReadSource(in GameplayAbilitySourceContext source, out AbilityLifecyclePhase phase)
        {
            bool matched = ((IFloat32AbilityFrameReadPort)this).TryReadAction(source.InstanceId, out Float32ActionInstanceState action) && action.SourceContext.Equals(source);
            phase = matched ? (AbilityLifecyclePhase)(byte)action.Phase : default;
            return matched;
        }
        SimulationInputValue IFloat32AbilityFrameReadPort.ReadInputValue(CanonicalTextValue inputId, ProgramInputValueKind kind) =>
            m_AvailabilityInput.ReadInputValue(ActorId, inputId, kind);
        SimulationInputRequestState IFloat32AbilityFrameReadPort.ReadInputRequest(CanonicalTextValue id)
        {
            var requests = m_AvailabilityState.InputRequests;
            for (int index = 0; index < requests.Length; index++)
                if (id.Equals(requests[index].Key)) return requests[index].Value;
            return default;
        }
        SimulationInputRequestState IFloat32AbilityAvailabilityReadOwner.ReadInputRequest(string id) =>
            ((IFloat32AbilityFrameReadPort)this).ReadInputRequest(id);
        bool IFloat32AbilityAvailabilityReadOwner.IsInputRequestPending(in SimulationInputRequestState input) =>
            IsInputRequestPending(m_AvailabilityState, in input);
        bool IFloat32AbilityAvailabilityReadOwner.HasGameplayEvent(string eventId) =>
            m_AvailabilityInput.HasGameplayEvent(ActorId, eventId);
        bool IFloat32AbilityAvailabilityReadOwner.EvaluateRuleCondition(CharacterSkillId skillId, in GameplayAbilitySourceContext source, OperationHandle condition) =>
            GetInvocation(skillId).EvaluateRuleCondition(in source, condition, GameplayAbilityRuleEvaluationMode.Availability);
        void IFloat32AbilityAvailabilityReadOwner.PreviewRule(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState input, out ActionAdmissionDecision admission) =>
            GetInvocation(ability.SkillId).PreviewAvailability(ability, rule, in source, in input, out admission);
        IReadOnlyList<Float32ActionInstanceState> IFloat32AbilityFrameReadPort.Actions => m_AvailabilityActions;
        IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> IFloat32AbilityFrameReadPort.Pending => m_AvailabilityPending;
        bool IFloat32AbilityFrameReadPort.TryReadAction(ulong instanceId, out Float32ActionInstanceState action)
        {
            var actions = m_AvailabilityState.ActionInstances;
            for (int index = 0; index < actions.Length; index++)
                if (actions[index].InstanceId == instanceId) { action = actions[index]; return true; }
            action = default;
            return false;
        }
        Float32AbilityRuntimeState ReadAvailabilityAbility(CharacterSkillId skill)
        {
            var abilities = m_AvailabilityState.Abilities;
            for (int index = 0; index < abilities.Count; index++)
                if (abilities[index].AbilityIdentity.AbilityId == skill) return abilities[index];
            throw new InvalidOperationException("Availability skill is absent from the committed ability directory.");
        }
        AbilityStateValue IFloat32AbilityFrameReadPort.ReadState(CharacterSkillId skill, int slot, in GameplayAbilitySourceContext source)
        {
            var installation = AbilityInstallations.Require(skill);
            var ability = ReadAvailabilityAbility(skill);
            int character = installation.CharacterBlackboardSlots[slot];
            if (character >= 0)
                return m_AvailabilityState.CharacterBlackboards.TryGetValue(character, out AbilityStateValue shared)
                    ? shared : installation.DefaultStateValue(slot);
            if (installation.Layout.IsSkillExecutionStateSlot(slot) && source.IsValid && source.SkillId == skill)
            {
                var frame = ability.AbilityExecutionState.Find(source.InstanceId);
                return frame != null && frame.TryGetValue(slot, out AbilityStateValue value)
                    ? value : installation.DefaultStateValue(slot);
            }
            return ability.StateValues.TryGetValue(slot, out AbilityStateValue stored) ? stored : installation.DefaultStateValue(slot);
        }
        GameplayAbilityExecutionAggregate<AbilityStateValue> IFloat32AbilityFrameReadPort.ReadAbilityState(CharacterSkillId skill) =>
            ReadAvailabilityAbility(skill).AbilityExecutionState;
        EquipmentStateAggregate IFloat32AbilityFrameReadPort.Equipment => m_AvailabilityState.EquipmentState;

        sealed class ActionSnapshotView : IReadOnlyList<Float32ActionInstanceState>
        {
            readonly SimulationActorBinding m_Owner;
            internal ActionSnapshotView(SimulationActorBinding owner) { m_Owner = owner; }
            public int Count => m_Owner.m_AvailabilityState.ActionInstances.Length;
            public Float32ActionInstanceState this[int index] => m_Owner.m_AvailabilityState.ActionInstances[index];
            public IEnumerator<Float32ActionInstanceState> GetEnumerator()
            { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        sealed class PendingSnapshotView : IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>>
        {
            readonly SimulationActorBinding m_Owner;
            internal PendingSnapshotView(SimulationActorBinding owner) { m_Owner = owner; }
            public int Count => m_Owner.m_AvailabilityState.ActionActivationRequests.Length;
            public ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot> this[int index] => m_Owner.m_AvailabilityState.ActionActivationRequests[index];
            public IEnumerator<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> GetEnumerator()
            { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        internal Float32AbilityInvocationRuntime[] Invocations => m_Invocations;
        internal Float32AbilityInvocationRuntime GetInvocation(CharacterSkillId abilityId) => m_InvocationByAbility[abilityId];
        internal bool IsInputRequestPending(Float32CharacterRuntimeState state, in SimulationInputRequestState input)
        {
            var pending = state.ActionActivationRequests;
            for (int i = 0; i < pending.Length; i++)
                if (pending[i].InputSequence == input.Sequence && pending[i].SourceInputRequestId.Equals(input.RequestId))
                    return true;
            return false;
        }

        internal bool IsInputRequestPending(in SimulationInputRequestState input)
        {
            var pending = m_ActionState.GetActionActivationRequests();
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].InputSequence == input.Sequence &&
                    pending[i].SourceInputRequestId.Equals(input.RequestId))
                    return true;
            return false;
        }

        internal Dictionary<CharacterSkillId, IFloat32AbilityActivationPort> ActionRuntimes => m_ActionRuntimes;
        internal Float32GameplayEffectExecutionScratch EffectExecutionScratch => m_EffectExecutionScratch;
        internal Float32AbilityExecutionWorkspace[] Workspaces => m_Workspaces;
        internal List<AbilityTimelineAdvancePending> TimelineAdvances => m_TimelineAdvances;
        internal List<AbilityTimelineStopPending> TimelineStops => m_TimelineStops;
        internal Dictionary<ulong, Float32AbilityInvocationRuntime> ActionOwnerByInstanceId => m_ActionOwnerByInstanceId;
        internal List<AbilityTimelineLogicMotion> TimelineLogicMotion => m_TimelineLogicMotion;
        internal Float32CharacterEvaluationOutput EvaluationOutput => m_EvaluationOutput;
        internal Float32CharacterEvaluationResult EvaluationResult => m_EvaluationResult;
        internal Float32CharacterTraceSink CharacterTraceSink => m_CharacterTraceSink;
        internal Float32AbilityExecutionInput AbilityExecutionInput => m_AbilityExecutionInput;
        internal Float32CharacterInputRequestState InputRequestState => m_InputRequestState;
        internal Float32CharacterActionRuntimeState ActionState => m_ActionState;
        internal CharacterControlRuntimeStateTransaction ControlState => m_ControlState;
        internal Float32CharacterEventSequenceState EventSequenceState => m_EventSequenceState;
        internal Float32CharacterHandleAllocatorState HandleAllocatorState => m_HandleAllocatorState;
        internal Float32CharacterGameplayEffectRuntimeState GameplayEffectState => m_GameplayEffectState;
        internal Float32CharacterEquipmentRuntimeState EquipmentState => m_EquipmentState;
        internal Float32CharacterRuntimeStateTransaction RuntimeState => m_RuntimeState;

        internal void PrepareEvaluationOutputCapacity(int maximumStepCount)
        {
            int additionalPages = maximumStepCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new Float32CharacterEvaluationOutput(AbilityInstallations.Installations);
                output.PrepareTraceStorage(m_TraceRecordsPerTick);                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
                output.PrepareGameplayEffectOutputs(m_GameplayEffectChangeCapacity, m_FactCapacity,
                    m_OutputPresentationCapacity);
                m_RecycledEvaluationOutputs.Push(output);
            }
        }

        public GameplayEffectSessionCapacity SessionCapacity { get; private set; }
        public int CommandBatchResultCapacity { get; private set; }

        public void PrepareTraceStorage(int traceRecordsPerTick)
        {
            m_TraceRecordsPerTick = traceRecordsPerTick;
            m_EvaluationOutput.PrepareTraceStorage(traceRecordsPerTick);
            foreach (Float32CharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareTraceStorage(traceRecordsPerTick);
            for (int index = 0; index < m_Workspaces.Length; index++)
                m_Workspaces[index].Trace.Capacity = traceRecordsPerTick;
        }

        // 一轮输出与 Scratch 共享恢复状态的文本来源，不按其中的 Spec 数重复预算整份载荷。
        internal int MaximumDetachedCheckpointTextPayloads =>
            checked(m_RecycledEvaluationOutputs.Count - SessionCapacity.CandidateTicks + 2);

        public void PrepareGameplayEffects(in GameplayEffectSessionCapacity session)
        {
            SessionCapacity = session;
            PrepareActionIdentities();
            PrepareCommandBatchResults();
            InputRequestPages.PrepareCapacity(session.StatePageCount);
            RuntimeStatePages.PrepareCapacity(session.StatePageCount);
            MotionWarpPages.PrepareCapacity(session.StatePageCount);
            foreach (AbilityStateValuePageBank<AbilityStateValue> bank in StateValuePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            CharacterBlackboardPages.PrepareCapacity(session.StatePageCount);
            foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in ExecutionStatePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            if (AbilityInstallations.RequiresEquipment)
                m_EquipmentState.Prepare(ActorId,
                    EquipmentProgramLayoutCompiler.CompileRoleStateLayout(EquipmentRuntimeBinding), in session);
            ActionStatePages.PrepareCapacity(session.StatePageCount);
            Float32GameplayEffectRuntimeCatalog catalog = AbilityInstallations.GameplayEffectCatalog;
            if (catalog != null)
            {
                if (AbilityInstallations.RequiresEquipment)
                {
                    EquipmentProgramLayout equipment = m_EquipmentState.Layout;
                    for (int featureIndex = 0; featureIndex < equipment.Features.Count; featureIndex++)
                    {
                        EquipmentProgramFeature feature = equipment.Features[featureIndex];
                        for (int tag = 0; tag < feature.GrantedTags.Count; tag++)
                            if (!catalog.ContainsTag(feature.GrantedTags[tag]))
                                throw new InvalidDataException($"Equipment Feature '{feature.FeatureId}' grants unregistered Tag '{feature.GrantedTags[tag]}'.");
                        if (feature.GrantedTags.Count > catalog.ActorCapacity.TagsPerExternalSource)
                            throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                                GameplayEffectPreparationFailureKind.ContentRequirementExceedsCapacity,
                                GameplayEffectCapacityDimension.TagsPerExternalSource, feature.FeatureId.Value,
                                feature.GrantedTags.Count, catalog.ActorCapacity.TagsPerExternalSource));
                    }
                }
                GameplayEffectRuntimeCapacityLayout capacity = catalog.PrepareCapacity(in session);
                PrepareGameplayEffectOutputs(capacity.Actor.ChangesPerTick, capacity.Actor.FactsPerTick,
                    capacity.Actor.PresentationRequestsPerTick, capacity.OutputPageCount);
            }
            else
                PrepareEvaluationOutputCapacity(session.CandidateTicks);
        }

        internal void PrepareExecutionTextBudget(int maximumSessionCheckpointPayloadBytes)
        {
            GameplayEffectSessionCapacity capacity = SessionCapacity;
            if (TimelineRuntime != null)
            {
                TimelineRuntime.PrepareSnapshotStorage(in capacity);
                m_TimelineLogicMotion.Capacity = TimelineMotionReader.MaximumMotionContributionsPerAdvance;
            }
            int checkpointOwners = checked(capacity.EncodedCheckpoints + capacity.CapturePayloads + capacity.RestorePayloads);
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[i];
                int identitySlots = 0;
                for (int slot = 0; slot < installation.Layout.StateSlots.Count; slot++)
                    if (installation.Layout.StateSlots[slot].ValueKind == ProgramStateValueKind.Identity)
                        identitySlots++;
                int stateValues = checked(identitySlots *
                    (ExecutionStatePages[installation.Identity.AbilityId].FrameCapacity + 1));
                int retainedTracePaths = checked(m_TraceRecordsPerTick *
                    SimulationTraceRecord.MaximumGeneratedPathReferences * (m_RecycledEvaluationOutputs.Count + 1));
                int retainedTextReferences = checked(stateValues * (capacity.StatePageCount + checkpointOwners) + retainedTracePaths);
                m_Invocations[i].Control.PrepareExecutionText(maximumSessionCheckpointPayloadBytes, retainedTextReferences);
            }
        }

        void PrepareCommandBatchResults()
        {
            long total = 0;
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                int repliesPerEvaluation = 0;
                for (int operationIndex = 0; operationIndex < installation.Layout.Operations.Count; operationIndex++)
                {
                    SimulationOperationCode code = installation.Layout.Operations[operationIndex].Code;
                    if (code == SimulationOperationCode.GameplayEffectApply ||
                        code == SimulationOperationCode.GameplayEffectRemove ||
                        code == SimulationOperationCode.ActivateActionInstance ||
                        code == SimulationOperationCode.CommitEquipmentChange ||
                        code == SimulationOperationCode.SubmitActionLifecycle)
                        repliesPerEvaluation = 1;
                }
                long count = (long)installation.MaximumOperationEvaluations * repliesPerEvaluation +
                    installation.Data.Binding.ActivationRules.Count + 1L +
                    (installation.EquipmentLayout == null ? 0 : 1);
                if (count > int.MaxValue)
                    throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                        GameplayEffectPreparationFailureKind.StorageLimitExceeded,
                        GameplayEffectCapacityDimension.SourceCommandResults, installation.Data.AbilityId.Value,
                        count, int.MaxValue));
                m_Workspaces[index].PrepareCommandBatchResults((int)count);
                total += count;
            }
            if (total > int.MaxValue)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.StorageLimitExceeded,
                    GameplayEffectCapacityDimension.SourceCommandResults, ActorId.ToString(), total, int.MaxValue));
            CommandBatchResultCapacity = (int)total;
            m_EvaluationOutput.PrepareCommandBatchResults(CommandBatchResultCapacity);
            foreach (Float32CharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
        }

        void PrepareActionIdentities()
        {
            int count = 0;
            for (int ability = 0; ability < AbilityInstallations.Installations.Count; ability++)
            {
                IReadOnlyList<ProgramCatalogEntry> entries = AbilityInstallations.Installations[ability].Data.CatalogEntries;
                for (int entry = 0; entry < entries.Count; entry++)
                    if (entries[entry].Kind == ProgramCatalogEntryKind.Action)
                        count++;
            }
            m_CodecActionProfiles = new ActionAdmissionProfile[count];
            int index = 0;
            for (int ability = 0; ability < AbilityInstallations.Installations.Count; ability++)
            {
                Float32GameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[ability];
                IReadOnlyList<ProgramCatalogEntry> entries = installation.Data.CatalogEntries;
                for (int entry = 0; entry < entries.Count; entry++)
                {
                    if (entries[entry].Kind != ProgramCatalogEntryKind.Action)
                        continue;
                    ActionAdmissionProfile profile = installation.Services.RequireAdmissionProfile(
                        entries[entry].Identity.Substring("action:".Length));
                    m_CodecActionProfiles[index] = profile;
                    index++;
                }
            }
        }

        internal string ReadActionIdentity(CanonicalReader reader) =>
            RequireActionIdentity(DeclaredIdentityReader(reader));

        internal string RequireActionIdentity(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return actionId;
            for (int index = 0; index < m_CodecActionProfiles.Length; index++)
                if (string.Equals(actionId, m_CodecActionProfiles[index].ActionId, StringComparison.Ordinal))
                    return m_CodecActionProfiles[index].ActionId;
            throw new InvalidDataException("Character state Action identity is absent from its installed Action Admission Profiles.");
        }

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation, int outputPageCount)
        {
            m_GameplayEffectChangeCapacity = changes;
            m_FactCapacity = checked(m_AbilityFactCapacity + facts);
            m_OutputPresentationCapacity = checked(m_AbilityPresentationCapacity + presentation);
            m_EvaluationOutput.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            foreach (Float32CharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            int additionalPages = outputPageCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new Float32CharacterEvaluationOutput(AbilityInstallations.Installations);
                output.PrepareTraceStorage(m_TraceRecordsPerTick);                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
                output.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
                m_RecycledEvaluationOutputs.Push(output);
            }
            for (int index = 0; index < m_Workspaces.Length; index++)
                m_Workspaces[index].PrepareGameplayEffectOutputs(changes, facts, presentation);
            m_EffectExecutionScratch.Target.BindOutputWorkspaces(m_Workspaces);
        }

        internal void RotateEvaluationOutput()
        {
            m_EvaluationOutput = m_RecycledEvaluationOutputs.Pop();
            m_EvaluationOutput.Clear();
        }

        internal void ReturnEvaluationOutput(Float32CharacterEvaluationOutput output)
        {
            output.Clear();
            m_RecycledEvaluationOutputs.Push(output);
        }

        internal void ClearActionRuntimes()
        {
            m_ActionRuntimes.Clear();
        }

        internal void ClearWorkspaces()
        {
            for (int i = 0; i < m_Workspaces.Length; i++)
                m_Workspaces[i].Reset();
        }

        internal void ClearTimelineTransfers()
        {
            m_TimelineAdvances.Clear();
            m_TimelineStops.Clear();
        }

        internal void ClearTimelineMotionScratches()
        {
            m_TimelineLogicMotion.Clear();
        }

        public void Dispose()
        {
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                m_Invocations[i].Control.Dispose();
            m_RuntimeState.Dispose();
            m_EquipmentState.Dispose();
            ClearWorkspaces();
            m_EvaluationResult.DiscardUnconsumed();
            m_EvaluationOutput.Clear();
            foreach (Float32CharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.Clear();
            m_RecycledEvaluationOutputs.Clear();
            ClearActionRuntimes();
            ClearTimelineTransfers();
            ClearTimelineMotionScratches();
            m_ActionOwnerByInstanceId.Clear();
            foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in ExecutionStatePages.Values)
                bank.ReleaseCurrent();
            CheckpointControlReader.SetTextResolver(null);
            CheckpointControlReader.Reset(Array.Empty<byte>());
            if (AbilityInstallations.GameplayEffectCatalog != null)
                m_GameplayEffectState.ReleaseStorage();
        }

        public CharacterBodyMotionBinding BodyMotionBinding { get; }
        internal Float32CharacterRuntimeStatePageBank RuntimeStatePages { get; }
        internal Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>> StateValuePages { get; }
        internal AbilityStateValuePageBank<AbilityStateValue> CharacterBlackboardPages { get; }
        internal Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>> ExecutionStatePages { get; }
        internal CanonicalReader CheckpointControlReader { get; } = new CanonicalReader(Array.Empty<byte>());
        internal CharacterControlStateSchema CheckpointControlSchema { get; private set; }

        internal Func<CanonicalReader, string> DeclaredIdentityReader { get; private set; }
        internal int DeclaredCheckpointIdentityCount { get; private set; }

        internal void PrepareCheckpointControlContract(CharacterControlModuleContract contract)
        {
            CheckpointControlSchema = contract.StateSchema;
            var identities = new List<string> { contract.ModuleId.Value };
            for (int i = 0; i < contract.States.Count; i++)
                identities.Add(contract.States[i].Id.Value);
            for (int i = 0; i < contract.Transitions.Count; i++)
                identities.Add(contract.Transitions[i].Id.Value);
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                GameplayAbilityExecutionIdentity identity = AbilityInstallations.Installations[i].Identity;
                identities.Add(identity.AbilityId.Value);
                identities.Add(identity.OperationSetVersion.Value);
                for (int rule = 0; rule < AbilityInstallations.Installations[i].Data.Binding.ActivationRules.Count; rule++)
                    identities.Add(AbilityInstallations.Installations[i].Data.Binding.ActivationRules[rule].ActivationEntryId);
            }
            for (int index = 0; index < m_CodecActionProfiles.Length; index++)
                identities.Add(m_CodecActionProfiles[index].ActionId);
            var uniqueIdentities = new HashSet<string>(identities, StringComparer.Ordinal);
            uniqueIdentities.Remove(null);
            uniqueIdentities.Remove(string.Empty);
            DeclaredCheckpointIdentityCount = uniqueIdentities.Count;
            var published = new GameplayEffectPublishedIdentityReader(identities);
            DeclaredIdentityReader = published.ReadPublished;
        }

        internal MotionWarpStatePageBank<Float32MotionWarpState> MotionWarpPages { get; }
        internal Float32InputRequestPageBank InputRequestPages { get; }
        internal Float32ActionStatePageBank ActionStatePages { get; }
        public CharacterGameplayEffectRuntimeBinding GameplayEffectRuntimeBinding { get; }
        public int GameplayOutputCapacity => checked(m_AbilityFactCapacity + m_AbilityPresentationCapacity +
            (AbilityInstallations.GameplayEffectCatalog == null ? 0 :
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.FactsPerTick +
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.PresentationRequestsPerTick));
        public CharacterEquipmentRuntimeBinding EquipmentRuntimeBinding { get; }
        public IAbilityTimelineRuntime TimelineRuntime { get; }
        public IAbilityTimelineLogicMotionReader TimelineMotionReader { get; }
        public Float32GameplayAbilityExecutionInstallationSet AbilityInstallations { get; }
        public StableHash StateSchemaHash { get; }
        public StableHash GameplayContentHash { get; }

        static StableHash ComputeStateSchemaHash(
            CharacterControlRuntimeBinding control,
            CharacterInputRuntimeBinding input,
            CharacterGameplayEffectRuntimeBinding gameplayEffects,
            CharacterEquipmentRuntimeBinding equipment,
            Float32GameplayAbilityExecutionInstallationSet abilities)
        {
            var parts = new List<string>
            {
                "float32-character-state-schema/3",
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                Float32GameplayAbilityExecutionData data = abilities.Installations[i].Data;
                parts.Add(data.AbilityId.Value);
                parts.Add(data.StateSchemaHash.ToString());
                parts.Add(abilities.Installations[i].Blackboards.StructureHash.ToString());
                parts.Add(data.OperationSetVersion.Value);
                parts.Add(data.NumericProfile.Id.ToString());
            }
            return StableHash.Compute(parts.ToArray());
        }

        static StableHash ComputeGameplayContentHash(
            CharacterControlRuntimeBinding control,
            CharacterInputRuntimeBinding input,
            CharacterBodyMotionBinding bodyMotion,
            CharacterGameplayEffectRuntimeBinding gameplayEffects,
            CharacterEquipmentRuntimeBinding equipment,
            Float32GameplayAbilityExecutionInstallationSet abilities)
        {
            var parts = new List<string>
            {
                "float32-simulation-actor-content/3",
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                bodyMotion.BindingHash.ToString(),
                abilities.BlackboardContentHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                Float32GameplayAbilityExecutionData data = abilities.Installations[i].Data;
                parts.Add(data.AbilityId.Value);
                parts.Add(data.ContentHash.ToString());
                parts.Add(data.StateSchemaHash.ToString());
                parts.Add(data.ExecutionIdentity);
            }
            return StableHash.Compute(parts.ToArray());
        }

        static IAbilityTimelineLogicMotionReader GetMotionReader(IAbilityTimelineRuntime timelineRuntime)
        {
            if (timelineRuntime == null)
                return null;
            return timelineRuntime is IAbilityTimelineLogicMotionReader motionReader
                ? motionReader
                : throw new ArgumentException("Float32 Timeline runtime must expose pending logic motion.", nameof(timelineRuntime));
        }



    }
}
