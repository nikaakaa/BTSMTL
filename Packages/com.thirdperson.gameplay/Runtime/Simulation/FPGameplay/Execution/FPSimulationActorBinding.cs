using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionWarpState = ThirdPersonSimulation.MotionWarpState<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, ThirdPersonSimulation.FPGameplay.FPActionInstanceReference>;
using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using System;
using ThirdPersonGameplay.Configuration;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPCharacterEvaluationOutput
    {
        public FPCombatResultStorage AttackResults { get; private set; }
        public FPCharacterEvaluationOutput(int invocationCount, int combatCapacity = 0, int presentationCapacity = 0)
        {
            ValueTraces = new SimulationValueTraceStorage(invocationCount);
            PrepareCombat(combatCapacity);
            Presentation.Capacity = presentationCapacity;
        }
        internal SimulationValueTraceStorage ValueTraces { get; }
#if UNITY_EDITOR
        internal long EvaluationStateElapsedStopwatchTicks;
        internal long EvaluationInputsElapsedStopwatchTicks;
        internal long EvaluationInputProjectionOwnerElapsedStopwatchTicks;
        internal long EvaluationInputProjectionValueElapsedStopwatchTicks;
        internal long EvaluationInputProjectionStampElapsedStopwatchTicks;
        internal long EvaluationTraceHeaderElapsedStopwatchTicks;
        internal long EvaluationTraceStoreElapsedStopwatchTicks;
        internal int EvaluationTraceHeaderCalls;
        internal int EvaluationTraceStoreCalls;
        internal long EvaluationInputSnapshotPrepareElapsedStopwatchTicks;
        internal long EvaluationInputSnapshotDiscardElapsedStopwatchTicks;
        internal long EvaluationInputBindingReadElapsedStopwatchTicks;
        internal long EvaluationInputBindingProjectElapsedStopwatchTicks;
        internal int EvaluationInputBindingCalls;
        internal long EvaluationInputsPrepareElapsedStopwatchTicks;
        internal long EvaluationInputsIngressElapsedStopwatchTicks;
        internal long EvaluationInputsBindingsElapsedStopwatchTicks;
        internal long EvaluationInputsBlackboardElapsedStopwatchTicks;
        internal long EvaluationControlElapsedStopwatchTicks;
        internal long EvaluationAbilitiesElapsedStopwatchTicks;
        internal long EvaluationAbilitiesTickElapsedStopwatchTicks;
        internal long EvaluationAbilitiesExistingElapsedStopwatchTicks;
        internal long EvaluationAbilitiesPendingElapsedStopwatchTicks;
        internal long EvaluationAbilitiesActiveElapsedStopwatchTicks;
        internal long EvaluationAbilitiesActiveTriggersElapsedStopwatchTicks;
        internal long EvaluationAbilitiesActiveControlElapsedStopwatchTicks;
        internal long EvaluationAbilitiesActiveOutputElapsedStopwatchTicks;
        internal int EvaluationAbilitiesActiveControlCalls;
        internal long EvaluationAbilitiesMotionElapsedStopwatchTicks;
        internal long EvaluationAbilitiesRulesElapsedStopwatchTicks;
        internal long EvaluationMotionElapsedStopwatchTicks;
        internal long EvaluationCommitElapsedStopwatchTicks;
#endif
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
        internal void BindValueTraces(FPAbilityExecutionWorkspace[] workspaces)
        {
            for (int index = 0; index < workspaces.Length; index++)
            { workspaces[index].TraceOutput = this; workspaces[index].ValueTraces = ValueTraces; workspaces[index].ValueTraceInvocation = index; }
        }
        internal void PrepareCombat(int capacity) => AttackResults = new FPCombatResultStorage(capacity);
        public SimulationActorTickResult TickResult { get; } = new SimulationActorTickResult();
        internal GameplayCommandBatchAdmission IngressAdmission { get; set; }
        internal List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;
        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        public List<SimulationTraceRecord> CharacterTrace { get; } = new List<SimulationTraceRecord>();
        internal FPGameplayEffectPendingProjection[] GameplayEffectProjections { get; private set; } =
            Array.Empty<FPGameplayEffectPendingProjection>();
        internal int GameplayEffectProjectionCount { get; set; }
        internal MotionWarpStatePageReference<MotionWarpState> MotionWarpStates { get; private set; }
        FPCharacterRuntimeState m_StatePageOwner;
        int m_RetainedFactTexts;
        int m_RetainedPresentationTexts;
        int m_RetainedProjectionTexts;
        int m_RetainedTraceTexts;

        internal void RetainOutputText()
        {
            for (int index = m_RetainedFactTexts; index < Facts.Count; index++)
                Facts[index].Retain();
            for (int index = m_RetainedPresentationTexts; index < Presentation.Count; index++)
                Presentation[index].Retain();
            for (int index = m_RetainedProjectionTexts; index < GameplayEffectProjectionCount; index++)
                GameplayEffectProjections[index].Retain();
            for (int index = m_RetainedTraceTexts; index < Trace.Count; index++)
                Trace[index].Retain();
            m_RetainedTraceTexts = Trace.Count;
            m_RetainedFactTexts = Facts.Count;
            m_RetainedPresentationTexts = Presentation.Count;
            m_RetainedProjectionTexts = GameplayEffectProjectionCount;
        }

        internal void RetainStatePages(FPCharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner = state;
        }

        internal void ReplaceStatePageOwner(FPCharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner.ReleaseStatePages();
            m_StatePageOwner = state;
        }

        internal void BeginMotionWarpState(
            MotionWarpStatePageBank<MotionWarpState> bank,
            in MotionWarpStatePageReference<MotionWarpState> source)
        {
            MotionWarpStates = bank.BeginWrite(source);
            MotionWarpStates.Retain();
        }

        public void Clear()
        {
            if (m_StatePageOwner.IsValid)
            {
                m_StatePageOwner.ReleaseStatePages();
                m_StatePageOwner = default;
            }
            if (MotionWarpStates.Generation != 0)
            {
                MotionWarpStates.Release();
                MotionWarpStates = default;
            }
            for (int index = 0; index < m_RetainedFactTexts; index++)
                Facts[index].Release();
            for (int index = 0; index < m_RetainedPresentationTexts; index++)
                Presentation[index].Release();
            for (int index = 0; index < m_RetainedProjectionTexts; index++)
                GameplayEffectProjections[index].Release();
            for (int index = 0; index < m_RetainedTraceTexts; index++)
                Trace[index].Release();
            m_RetainedTraceTexts = 0;
            m_RetainedFactTexts = 0;
            m_RetainedPresentationTexts = 0;
            m_RetainedProjectionTexts = 0;
            Facts.Clear();
            Presentation.Clear();
            Trace.Clear();
            for (int index = 0; index < CharacterTrace.Count; index++)
                CharacterTrace[index].Release();
            CharacterTrace.Clear();
            ValueTraces.Clear();
#if UNITY_EDITOR
            EvaluationStateElapsedStopwatchTicks = 0;
            EvaluationInputsElapsedStopwatchTicks = 0;
            EvaluationInputProjectionOwnerElapsedStopwatchTicks = 0;
            EvaluationInputProjectionValueElapsedStopwatchTicks = 0;
            EvaluationInputProjectionStampElapsedStopwatchTicks = 0;
            EvaluationTraceHeaderElapsedStopwatchTicks = 0;
            EvaluationTraceStoreElapsedStopwatchTicks = 0;
            EvaluationTraceHeaderCalls = 0;
            EvaluationTraceStoreCalls = 0;
            EvaluationInputSnapshotPrepareElapsedStopwatchTicks = 0;
            EvaluationInputSnapshotDiscardElapsedStopwatchTicks = 0;
            EvaluationInputBindingReadElapsedStopwatchTicks = 0;
            EvaluationInputBindingProjectElapsedStopwatchTicks = 0;
            EvaluationInputBindingCalls = 0;
            EvaluationInputsPrepareElapsedStopwatchTicks = 0;
            EvaluationInputsIngressElapsedStopwatchTicks = 0;
            EvaluationInputsBindingsElapsedStopwatchTicks = 0;
            EvaluationInputsBlackboardElapsedStopwatchTicks = 0;
            EvaluationControlElapsedStopwatchTicks = 0;
            EvaluationAbilitiesElapsedStopwatchTicks = 0;
            EvaluationAbilitiesTickElapsedStopwatchTicks = 0;
            EvaluationAbilitiesExistingElapsedStopwatchTicks = 0;
            EvaluationAbilitiesPendingElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveTriggersElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveControlElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveOutputElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveControlCalls = 0;
            EvaluationAbilitiesMotionElapsedStopwatchTicks = 0;
            EvaluationAbilitiesRulesElapsedStopwatchTicks = 0;
            EvaluationMotionElapsedStopwatchTicks = 0;
            EvaluationCommitElapsedStopwatchTicks = 0;
#endif
            m_TraceRecordCount = 0;
            Array.Clear(GameplayEffectProjections, 0, GameplayEffectProjectionCount);
            GameplayEffectProjectionCount = 0;
            IngressAdmission = default;
            CommandBatchRejections.Clear();
            AttackResults.Clear();
        }

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation)
        {
            GameplayEffectProjections = new FPGameplayEffectPendingProjection[changes];
            Facts.Capacity = facts;
            Presentation.Capacity = presentation;
        }

        internal void FinalizeGameplayEffects()
        {
            for (int index = 0; index < GameplayEffectProjectionCount; index++)
                GameplayEffectProjections[index].Project(Facts, Presentation);
        }
    }

    public sealed class SimulationActorBinding : IDisposable, IFPAbilityAvailabilityReadOwner, IFPAbilityFrameReadPort
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal byte SimulatedIncomingAttacks;
        internal readonly CombatHitResult[] SimulatedIncomingAttackResults =
            { new CombatHitResult(), new CombatHitResult() };
#endif
        readonly FPCharacterControlMotionRuntime m_ControlMotion;
        readonly MotionContributionBuffer m_MotionContributions = new MotionContributionBuffer();
        readonly FPAbilityInvocationRuntime[] m_Invocations;
        readonly Dictionary<CharacterSkillId, FPAbilityInvocationRuntime> m_InvocationByAbility;
        readonly Dictionary<CharacterSkillId, IFPAbilityActivationPort> m_ActionRuntimes;
        readonly FPGameplayEffectExecutionScratch m_EffectExecutionScratch = new FPGameplayEffectExecutionScratch();
        readonly List<AbilityTimelineAdvancePending> m_TimelineAdvances = new List<AbilityTimelineAdvancePending>();
        readonly List<AbilityTimelineStopPending> m_TimelineStops = new List<AbilityTimelineStopPending>();
        readonly Dictionary<ulong, FPAbilityInvocationRuntime> m_ActionOwnerByInstanceId;
        readonly List<AbilityTimelineLogicMotion> m_TimelineLogicMotion = new List<AbilityTimelineLogicMotion>();
        readonly FPAbilityExecutionWorkspace[] m_Workspaces;
        FPCharacterEvaluationOutput m_EvaluationOutput;
        readonly Stack<FPCharacterEvaluationOutput> m_RecycledEvaluationOutputs =
            new Stack<FPCharacterEvaluationOutput>();
        readonly FPCharacterEvaluationResult m_EvaluationResult;
        readonly FPCharacterTraceSink m_CharacterTraceSink;
        readonly FPAbilityExecutionInput m_AbilityExecutionInput = new FPAbilityExecutionInput();
        readonly FPCharacterInputRequestState m_InputRequestState = new FPCharacterInputRequestState();
        readonly FPCharacterActionRuntimeState m_ActionState;
        readonly CharacterControlRuntimeStateTransaction m_ControlState = new CharacterControlRuntimeStateTransaction();
        readonly FPCharacterEventSequenceState m_EventSequenceState = new FPCharacterEventSequenceState();
        readonly FPCharacterHandleAllocatorState m_HandleAllocatorState = new FPCharacterHandleAllocatorState();
        readonly FPCharacterGameplayEffectRuntimeState m_GameplayEffectState = new FPCharacterGameplayEffectRuntimeState();
        readonly FPCharacterEquipmentRuntimeState m_EquipmentState = new FPCharacterEquipmentRuntimeState();
        readonly FPCharacterRuntimeStateTransaction m_RuntimeState = new FPCharacterRuntimeStateTransaction();
        FPCombatQueryStorage m_CombatQueries;
        int m_CombatResultCapacity;
        readonly int m_AbilityFactCapacity;
        readonly int m_AbilityPresentationCapacity;
        int m_GameplayEffectChangeCapacity;
        int m_TraceRecordsPerTick;
        int m_FactCapacity;
        int m_OutputPresentationCapacity;
        CanonicalDeclaredIdentityReader m_DeclaredIdentities;

        public CanonicalDeclaredIdentityReader DeclaredIdentities => m_DeclaredIdentities;

        public string ResolveDeclaredIdentity(in CanonicalTextValue value) =>
            m_DeclaredIdentities.ResolvePublished(in value);

        public void PrepareDeclaredIdentities(CharacterControlModuleContract contract,
            IReadOnlyList<SimulationActorBinding> roster, SimulationSourceClockId sourceClockId,
            string sourceIdentity, string neutralSourceIdentity)
        {
            var identities = new List<string>
            {
                contract.ModuleId.Value, sourceClockId.Value, sourceIdentity, neutralSourceIdentity,
                QuantumSimulationNumericProfile.Value.Id.ToString()
            };
            for (int index = 0; index < roster.Count; index++)
                identities.Add(roster[index].ActorId.ToString());
            for (int index = 0; index < InputValues.Count; index++)
                identities.Add(InputValues[index].InputId.ToString());
            for (int index = 0; index < contract.States.Count; index++)
                identities.Add(contract.States[index].Id.Value);
            for (int index = 0; index < contract.Transitions.Count; index++)
                identities.Add(contract.Transitions[index].Id.Value);
            for (int ability = 0; ability < AbilityInstallations.Installations.Count; ability++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[ability];
                FPGameplayAbilityExecutionData data = installation.Data;
                identities.Add(installation.Identity.AbilityId.Value);
                identities.Add(installation.Identity.OperationSetVersion.Value);
                for (int entry = 0; entry < data.CatalogEntries.Count; entry++)
                {
                    ProgramCatalogEntry item = data.CatalogEntries[entry];
                    identities.Add(item.Identity);
                    if (item.Kind == ProgramCatalogEntryKind.Action)
                        identities.Add(installation.Services.RequireAdmissionProfile(item.Identity.Substring("action:".Length)).ActionId);
                    for (int field = 0; field < item.Fields.Count; field++)
                        if (item.Fields[field].Identity != null)
                            identities.Add(item.Fields[field].Identity);
                }
                for (int operation = 0; operation < data.Operations.Count; operation++)
                    identities.Add(data.Operations[operation].Text0);
            }
            if (InputRuntimeBinding != null)
                for (int input = 0; input < InputRuntimeBinding.InputRequestIds.Count; input++)
                    identities.Add(InputRuntimeBinding.InputRequestIds[input]);
            if (AbilityInstallations.RequiresEquipment)
            {
                EquipmentProgramLayout equipment = EquipmentRuntimeBinding.Layout;
                for (int index = 0; index < equipment.Items.Count; index++)
                    identities.Add(equipment.Items[index].EquipmentId.Value);
                for (int index = 0; index < equipment.Features.Count; index++)
                    identities.Add(equipment.Features[index].FeatureId.Value);
                for (int index = 0; index < equipment.Routes.Count; index++)
                    identities.Add(equipment.Routes[index].RouteId.Value);
            }
            m_DeclaredIdentities = new CanonicalDeclaredIdentityReader(identities);
        }

        internal void PrepareCombatResultCapacity(int capacity)
        {
            m_OutputPresentationCapacity = checked(m_OutputPresentationCapacity + capacity - m_CombatResultCapacity);
            m_CombatResultCapacity = capacity;
            m_EvaluationOutput.PrepareCombat(capacity);
            m_EvaluationOutput.Presentation.Capacity = m_OutputPresentationCapacity;
            foreach (FPCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
            {
                output.PrepareCombat(capacity);
                output.Presentation.Capacity = m_OutputPresentationCapacity;
            }
        }

        public void PrepareEvaluationOutputCapacity(int maximumStepCount)
        {
            int additionalPages = maximumStepCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new FPCharacterEvaluationOutput(AbilityInstallations.Installations.Count, m_CombatResultCapacity, m_OutputPresentationCapacity);
                output.PrepareTraceStorage(m_TraceRecordsPerTick);
                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
                output.PrepareGameplayEffectOutputs(m_GameplayEffectChangeCapacity, m_FactCapacity,
                    m_OutputPresentationCapacity);
                m_RecycledEvaluationOutputs.Push(output);
            }
        }

        public GameplayEffectSessionCapacity SessionCapacity { get; private set; }
        public int CommandBatchResultCapacity { get; private set; }

        public void PrepareInputRequestCapacity(int maximumCapturedRequestsPerTick, int offensiveRequestDelayTicks)
        {
            if (maximumCapturedRequestsPerTick <= 0 || offensiveRequestDelayTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCapturedRequestsPerTick));
            InputRequestPages.PreparePendingRequestCapacity(
                checked(maximumCapturedRequestsPerTick * (offensiveRequestDelayTicks + 1)));
            m_InputRequestState.PrepareTiming(offensiveRequestDelayTicks);
        }

        public void PrepareTraceStorage(int traceRecordsPerTick)
        {
            m_TraceRecordsPerTick = traceRecordsPerTick;
            m_EvaluationOutput.PrepareTraceStorage(traceRecordsPerTick);
            foreach (FPCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareTraceStorage(traceRecordsPerTick);
            for (int index = 0; index < m_Workspaces.Length; index++)
                m_Workspaces[index].Trace.Capacity = traceRecordsPerTick;
        }

        public void PrepareGameplayEffects(in GameplayEffectSessionCapacity session)
        {
            SessionCapacity = session;
            PrepareCommandBatchResults();
            InputRequestPages.PrepareCapacity(session.StatePageCount);
            ActionStatePages.PrepareCapacity(session.StatePageCount);
            RuntimeStatePages.PrepareCapacity(session.StatePageCount);
            MotionWarpPages.PrepareCapacity(session.StatePageCount);
            AbilityDirectories.PrepareCapacity(session.StatePageCount);
            TimelineSnapshotPages.PrepareCapacity(session.StatePageCount);
            if (TimelineRuntime != null)
            {
                TimelineRuntime.PrepareSnapshotStorage(in session);
                m_TimelineLogicMotion.Capacity = TimelineMotionReader.MaximumMotionContributionsPerAdvance;
            }
            int motionCapacity = checked(1 + m_TimelineAdvances.Capacity * m_TimelineLogicMotion.Capacity);
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
                motionCapacity = checked(motionCapacity + AbilityInstallations.Installations[index].Layout.Topology.MotionContributionCapacity);
            m_MotionContributions.PrepareCapacity(motionCapacity);
            foreach (AbilityStateValuePageBank<AbilityStateValue> bank in StateValuePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            CharacterBlackboardPages.PrepareCapacity(session.StatePageCount);
            foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in ExecutionStatePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            if (AbilityInstallations.RequiresEquipment)
                m_EquipmentState.Prepare(ActorId,
                    EquipmentRuntimeBinding.Layout, in session);
            FPGameplayEffectRuntimeCatalog catalog = AbilityInstallations.GameplayEffectCatalog;
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
            for (int index = 0; index < m_Invocations.Length; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                int identitySlots = 0;
                for (int slot = 0; slot < installation.Layout.StateSlots.Count; slot++)
                    if (installation.Layout.StateSlots[slot].ValueKind == ProgramStateValueKind.Identity)
                        identitySlots++;
                int stateValues = checked(identitySlots *
                    (ExecutionStatePages[installation.Identity.AbilityId].FrameCapacity + 1));
                int retainedTracePaths = checked(m_TraceRecordsPerTick *
                    SimulationTraceRecord.MaximumGeneratedPathReferences * (m_RecycledEvaluationOutputs.Count + 1));
                int retainedTextReferences = checked(stateValues * session.StatePageCount + retainedTracePaths);
                m_Invocations[index].PrepareStateExecutionText(retainedTextReferences);
            }
        }

        void PrepareCommandBatchResults()
        {
            long total = 0;
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
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
                    GameplayEffectCapacityDimension.SourceCommandResults, ActorId.Value.ToString(), total, int.MaxValue));
            CommandBatchResultCapacity = (int)total;
            m_EvaluationOutput.PrepareCommandBatchResults(CommandBatchResultCapacity);
            foreach (FPCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
        }

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation, int outputPageCount)
        {
            m_GameplayEffectChangeCapacity = changes;
            m_FactCapacity = checked(m_AbilityFactCapacity + facts);
            m_OutputPresentationCapacity = checked(m_AbilityPresentationCapacity + presentation + m_CombatResultCapacity);
            m_EvaluationOutput.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            foreach (FPCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            int additionalPages = outputPageCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new FPCharacterEvaluationOutput(AbilityInstallations.Installations.Count, m_CombatResultCapacity, m_OutputPresentationCapacity);
                output.PrepareTraceStorage(m_TraceRecordsPerTick);
                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
                output.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
                m_RecycledEvaluationOutputs.Push(output);
            }
            for (int index = 0; index < m_Workspaces.Length; index++)
                m_Workspaces[index].PrepareGameplayEffectOutputs(changes, facts, presentation);
            m_EffectExecutionScratch.Target.BindOutputWorkspaces(m_Workspaces);
        }

        public SimulationActorBinding(
            ActorId actorId,
            string worldBodyBindingId,
            CharacterControlRuntimeBinding controlRuntimeBinding,
            CharacterInputRuntimeBinding inputRuntimeBinding,
            IReadOnlyList<SimulationInputValue> inputValues,
            CharacterBodyMotionBinding bodyMotionBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectRuntimeBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GameplayAbilityExecutionDataSet<FPGameplayAbilityExecutionData> abilityData,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            IAbilityTimelineRuntime timelineRuntime,
            CharacterCombatRuntimeBinding combatBinding = null,
            GameplayConfigurationRuntimeBinding configurations = null)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Actor identity is invalid.", nameof(actorId));
            ActorId = actorId;
            CombatBinding = combatBinding;
            Configurations = configurations;
            m_CombatResultCapacity = combatBinding == null ? 0 : combatBinding.ResultCapacity;
            m_CombatQueries = new FPCombatQueryStorage(combatBinding == null ? 0 : combatBinding.AttackCapacity);
            WorldBodyBindingId = SimulationIdentity.Require(worldBodyBindingId, nameof(worldBodyBindingId));
            ControlRuntimeBinding = controlRuntimeBinding ?? throw new ArgumentNullException(nameof(controlRuntimeBinding));
            InputRuntimeBinding = inputRuntimeBinding;
            InputValues = inputValues;
            ControlMotionBindings = new FPCharacterControlMotionBindingCatalog(controlRuntimeBinding.MotionBindings);
            m_ControlMotion = new FPCharacterControlMotionRuntime(ControlMotionBindings);
            BodyMotionBinding = bodyMotionBinding ?? throw new ArgumentNullException(nameof(bodyMotionBinding));
            BodyMotionParameters = new BodyMotionParameters<FP>(
                QuantumScalarBoundary.ConvertExternal(bodyMotionBinding.GravityAcceleration, WorldBodyBindingId),
                QuantumScalarBoundary.ConvertExternal(bodyMotionBinding.MaximumFallSpeed, WorldBodyBindingId));
            AbilityInstallations = new FPGameplayAbilityExecutionInstallationSet(
                abilityData ?? throw new ArgumentNullException(nameof(abilityData)),
                inputRuntimeBinding,
                gameplayEffectRuntimeBinding,
                equipmentRuntimeBinding,
                blackboards, combatBinding, configurations);
            m_EvaluationOutput = new FPCharacterEvaluationOutput(AbilityInstallations.Installations.Count, combatBinding == null ? 0 : combatBinding.ResultCapacity);
            RuntimeStatePages = new FPCharacterRuntimeStatePageBank();
            AbilityDirectories = new FPAbilityStateDirectoryBank(AbilityInstallations.Installations.Count);
            TimelineSnapshotPages = new FPTimelineSnapshotPageBank();
            int inputRequestCapacity = InputRuntimeBinding.InputRequestIds.Count;
            InputRequestPages = new FPInputRequestPageBank(inputRequestCapacity);
            StateValuePages = new Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>>(
                AbilityInstallations.Installations.Count);
            ExecutionStatePages = new Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>>(
                AbilityInstallations.Installations.Count);
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                ExecutionStatePages.Add(installation.Identity.AbilityId,
                    new GameplayAbilityExecutionStateBank<AbilityStateValue>(installation.Identity,
                        installation.Layout.StateSlots.Count, installation.Layout.ActionInstanceCapacity,
                        installation.Layout.IsSkillExecutionStateSlot, value => value.Retain(), value => value.Release(), installation.Blackboards));
                StateValuePages.Add(installation.Identity.AbilityId,
                    new AbilityStateValuePageBank<AbilityStateValue>(installation.Identity,
                        installation.Layout.StateSlots.Count, slot => !installation.Layout.IsSkillExecutionStateSlot(slot) &&
                            AbilityInstallations.CharacterBlackboards.Slots(installation.Data.AbilityId)[slot] < 0,
                        value => value.Retain(), value => value.Release(), installation.Blackboards));
            }
            var warpLayouts = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>[AbilityInstallations.Installations.Count];
            for (int index = 0; index < warpLayouts.Length; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                warpLayouts[index] = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>(
                    installation.Identity.AbilityId, installation.Layout.MotionWarpOperationIds);
            }
            var motionWarpLayout = new MotionWarpStatePageLayout(warpLayouts);
            MotionWarpPages = new MotionWarpStatePageBank<MotionWarpState>(motionWarpLayout, value => value.Retain(), value => value.Release());
            CharacterBlackboardPages = new AbilityStateValuePageBank<AbilityStateValue>(default,
                AbilityInstallations.CharacterBlackboards.SlotCount, _ => true,
                value => value.Retain(), value => value.Release(), AbilityInstallations.CharacterBlackboards.Binding);
            m_RuntimeState.Initialize(AbilityInstallations.Installations, motionWarpLayout, AbilityInstallations.CharacterBlackboards);
            int actionCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                actionCapacity = checked(actionCapacity + AbilityInstallations.Installations[i].Layout.ActionInstanceCapacity);
            m_ActionState = new FPCharacterActionRuntimeState(actionCapacity, AbilityInstallations.Installations);
            ActionStatePages = new FPActionStatePageBank(actionCapacity);
            m_ActionOwnerByInstanceId = new Dictionary<ulong, FPAbilityInvocationRuntime>(actionCapacity);
            m_Invocations = new FPAbilityInvocationRuntime[AbilityInstallations.Installations.Count];
            m_InvocationByAbility = new Dictionary<CharacterSkillId, FPAbilityInvocationRuntime>(
                AbilityInstallations.Installations.Count);
            m_ActionRuntimes = new Dictionary<CharacterSkillId, IFPAbilityActivationPort>(
                AbilityInstallations.Installations.Count);
            m_Workspaces = new FPAbilityExecutionWorkspace[AbilityInstallations.Installations.Count];
            for (int i = 0; i < m_Workspaces.Length; i++)
            {
                m_Workspaces[i] = new FPAbilityExecutionWorkspace(
                    m_EffectExecutionScratch,
                    AbilityInstallations.Installations[i].Data,
                    AbilityInstallations.Installations[i].Layout,
                    m_TimelineAdvances,
                    m_TimelineStops);
            }
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                FPGameplayAbilityExecutionData data = AbilityInstallations.Installations[i].Data;
                if (data.NumericProfile != QuantumSimulationNumericProfile.Value)
                    throw new InvalidOperationException($"Ability '{data.AbilityId}' does not target FP.");
            }
            GameplayEffectRuntimeBinding = gameplayEffectRuntimeBinding;
            EquipmentRuntimeBinding = equipmentRuntimeBinding;
            TimelineRuntime = timelineRuntime;
            int timelineRequestCapacity = 0;
            int abilityFactCapacity = 0;
            int abilityPresentationCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                OperationExecutionTopology topology = AbilityInstallations.Installations[i].Layout.Topology;
                timelineRequestCapacity = checked(timelineRequestCapacity + topology.Operations.Count);
                abilityFactCapacity = checked(abilityFactCapacity + topology.FactRecordCapacity);
                abilityPresentationCapacity = checked(abilityPresentationCapacity + topology.PresentationRecordCapacity);
            }
            m_TimelineAdvances.Capacity = timelineRequestCapacity;
            m_TimelineStops.Capacity = timelineRequestCapacity;
            m_AbilityFactCapacity = abilityFactCapacity;
            m_AbilityPresentationCapacity = abilityPresentationCapacity;
            m_OutputPresentationCapacity = m_AbilityPresentationCapacity;
            m_FactCapacity = m_AbilityFactCapacity;
            m_EvaluationOutput.Facts.Capacity = m_FactCapacity;
            m_EvaluationOutput.Presentation.Capacity = m_AbilityPresentationCapacity;
            m_EvaluationResult = new FPCharacterEvaluationResult(
                this,
                actorId,
                timelineRuntime,
                timelineRequestCapacity);
            IFPAbilityDomainRuntimeFactory domainRuntimeFactory = new FPAbilityDomainRuntimeFactory();
            IFPAbilityExecutionServiceFactory serviceFactory = new FPAbilityExecutionServiceFactory(timelineRuntime, m_ActionRuntimes);
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                FPGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[i];
                m_Invocations[i] = new FPAbilityInvocationRuntime(
                    installation.Execution,
                    actorId,
                    AbilityInstallations,
                    domainRuntimeFactory,
                    installation.EquipmentLayout,
                    m_Workspaces[i],
                    serviceFactory);
                m_InvocationByAbility.Add(installation.Data.AbilityId, m_Invocations[i]);
            }

            m_AvailabilityActions = new ActionSnapshotView(this);
            m_AvailabilityPending = new PendingSnapshotView(this);
            ActivationRules = new FPAbilityActivationRuleRuntime(this);
            for (int i = 0; i < m_Invocations.Length; i++)
                m_Invocations[i].BindDecisionRead(ActivationRules);
            m_CharacterTraceSink = new FPCharacterTraceSink(m_EvaluationOutput.CharacterTrace);
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
                AbilityInstallations,
                combatBinding);
        }

        internal FPAbilityActivationRuleRuntime ActivationRules { get; }
        public ActorId ActorId { get; }
        public CharacterCombatRuntimeBinding CombatBinding { get; }
        public GameplayConfigurationRuntimeBinding Configurations { get; }
#if UNITY_EDITOR
        public FPCombatQueryDiagnostics CombatQueryDiagnostics { get; internal set; }
#endif
        internal FPCombatQueryStorage CombatQueries => m_CombatQueries;
        public string WorldBodyBindingId { get; }
        public CharacterControlRuntimeBinding ControlRuntimeBinding { get; }
        public CharacterInputRuntimeBinding InputRuntimeBinding { get; }
        public IReadOnlyList<SimulationInputValue> InputValues { get; }
        public int InputValueCapacity => InputValues.Count;
        internal FPCharacterControlMotionBindingCatalog ControlMotionBindings { get; }
        internal FPCharacterControlMotionRuntime ControlMotion => m_ControlMotion;
        internal MotionContributionBuffer MotionContributions => m_MotionContributions;
        public int AvailabilityIngressCapacity { get; private set; }
        internal void PrepareAvailabilityIngressCapacity(int capacity) => AvailabilityIngressCapacity = capacity;
        IFPAbilityFrameEventCapturePort m_CommittedInput;
        public void BindCommittedInput(IFPAbilityFrameEventCapturePort input) => m_CommittedInput = input;
        internal void CaptureAvailabilityFrameEvents(SimulationIngress[] ingress, int count)
        {
            if (m_CommittedInput != null)
                m_CommittedInput.CaptureFrameEvents(ActorId, new ReadOnlySpan<SimulationIngress>(ingress, 0, count));
        }
        bool IFPAbilityFrameReadPort.HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId) =>
            m_AvailabilityInput.HasActionEvent(ActorId, instanceId, eventId, targetId);

        FPCharacterRuntimeState m_AvailabilityState;
        IFPAbilityInputReadPort m_AvailabilityInput;
        FPCharacterControlRuntime m_AvailabilityControl;
        readonly ActionSnapshotView m_AvailabilityActions;
        readonly PendingSnapshotView m_AvailabilityPending;
        FPAbilityBodyFacts m_AvailabilityBody;

        internal void QueryAbilityAvailability(FPCharacterRuntimeState state,
            WorldBodyState body, IFPAbilityInputReadPort input, FPCharacterControlRuntime control,
            GameplayAbilityRulePhase phase, out GameplayAbilityAvailabilityResult result)
        {
            state.RetainStatePages();
            m_AvailabilityState = state;
            m_AvailabilityInput = input;
            m_AvailabilityControl = control;
            m_AvailabilityBody = new FPAbilityBodyFacts(ActorId, body);
            try
            {
                if (AbilityInstallations.GameplayEffectCatalog != null)
                    m_EffectExecutionScratch.Target.BeginAvailabilityRead(state.GameplayEffectState,
                        AbilityInstallations.GameplayEffectCatalog, ActorId, ((IFPAbilityFrameReadPort)this).Tick);
                for (int index = 0; index < m_Invocations.Length; index++)
                {
                    m_Invocations[index].PrepareEvaluationInputs();
                    m_Invocations[index].BindAvailabilityRead(this);
                }
                ActivationRules.QueryAvailability(phase, this, out result);
            }
            finally
            {
                for (int index = 0; index < m_Invocations.Length; index++)
                {
                    m_Invocations[index].BindAvailabilityRead(null);
                    m_Invocations[index].DiscardEvaluationInputs();
                }
                if (AbilityInstallations.GameplayEffectCatalog != null)
                    m_EffectExecutionScratch.Target.EndAvailabilityRead();
                m_AvailabilityState = default;
                m_AvailabilityInput = null;
                m_AvailabilityControl = null;
                m_AvailabilityBody = default;
                state.ReleaseStatePages();
            }
        }

        SimulationTick IFPAbilityFrameReadPort.Tick => new SimulationTick(m_AvailabilityState.LastCompletedTick);
        SimulationTick IFPAbilityAvailabilityReadOwner.Tick => ((IFPAbilityFrameReadPort)this).Tick;
        ulong IFPAbilityFrameReadPort.InputSequence => m_AvailabilityInput.ReadInputSequence(ActorId);
        FPAbilityBodyFacts IFPAbilityFrameReadPort.BodyFacts => m_AvailabilityBody;
        bool IFPAbilityAvailabilityReadOwner.HasMoveInput => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).HasMoveInput;
        CharacterLocomotionMode IFPAbilityAvailabilityReadOwner.LocomotionMode => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).LocomotionMode;
        int IFPAbilityAvailabilityReadOwner.ActionCount => m_AvailabilityState.ActionInstances.Length;
        GameplayAbilitySourceContext IFPAbilityAvailabilityReadOwner.ReadActiveSource(int index)
        {
            ref readonly FPActionInstanceState action = ref m_AvailabilityState.ActionInstances[index];
            return action.IsActive ? action.SourceContext : default;
        }
        ulong IFPAbilityAvailabilityReadOwner.ReadSourceStartTick(int index) => m_AvailabilityState.ActionInstances[index].StartTick;
        bool IFPAbilityAvailabilityReadOwner.TryReadSource(in GameplayAbilitySourceContext source, out AbilityLifecyclePhase phase)
        {
            bool matched = ((IFPAbilityFrameReadPort)this).TryReadAction(source.InstanceId, out FPActionInstanceState action) && action.SourceContext.Equals(source);
            phase = matched ? (AbilityLifecyclePhase)(byte)action.Phase : default;
            return matched;
        }
        SimulationInputValue IFPAbilityFrameReadPort.ReadInputValue(CanonicalTextValue inputId, ProgramInputValueKind kind) =>
            m_AvailabilityInput.ReadInputValue(ActorId, inputId, kind);
        SimulationInputRequestState IFPAbilityFrameReadPort.ReadInputRequest(CanonicalTextValue id)
        {
            var requests = m_AvailabilityState.InputRequests;
            for (int index = 0; index < requests.Length; index++)
                if (id.Equals(requests[index].Key)) return requests[index].Value;
            return default;
        }
        SimulationInputRequestState IFPAbilityAvailabilityReadOwner.ReadInputRequest(string id) =>
            ((IFPAbilityFrameReadPort)this).ReadInputRequest(id);
        bool IFPAbilityAvailabilityReadOwner.IsInputRequestPending(in SimulationInputRequestState input) =>
            IsInputRequestPending(m_AvailabilityState, in input);
        bool IFPAbilityAvailabilityReadOwner.HasGameplayEvent(string eventId) =>
            m_AvailabilityInput.HasGameplayEvent(ActorId, eventId);
        bool IFPAbilityAvailabilityReadOwner.EvaluateRuleCondition(CharacterSkillId skillId, in GameplayAbilitySourceContext source, OperationHandle condition) =>
            GetInvocation(skillId).EvaluateRuleCondition(in source, condition, GameplayAbilityRuleEvaluationMode.Availability);
        void IFPAbilityAvailabilityReadOwner.PreviewRule(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState input, out ActionAdmissionDecision admission) =>
            GetInvocation(ability.SkillId).PreviewAvailability(ability, rule, in source, in input, out admission);
        IReadOnlyList<FPActionInstanceState> IFPAbilityFrameReadPort.Actions => m_AvailabilityActions;
        IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> IFPAbilityFrameReadPort.Pending => m_AvailabilityPending;
        bool IFPAbilityFrameReadPort.TryReadAction(ulong instanceId, out FPActionInstanceState action)
        {
            var actions = m_AvailabilityState.ActionInstances;
            for (int index = 0; index < actions.Length; index++)
                if (actions[index].InstanceId == instanceId) { action = actions[index]; return true; }
            action = default;
            return false;
        }
        FPAbilityRuntimeState ReadAvailabilityAbility(CharacterSkillId skill)
        {
            var abilities = m_AvailabilityState.Abilities;
            for (int index = 0; index < abilities.Count; index++)
                if (abilities[index].AbilityIdentity.AbilityId == skill) return abilities[index];
            throw new InvalidOperationException("Availability skill is absent from the committed ability directory.");
        }
        AbilityStateValue IFPAbilityFrameReadPort.ReadState(CharacterSkillId skill, int slot, in GameplayAbilitySourceContext source)
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
        GameplayAbilityExecutionAggregate<AbilityStateValue> IFPAbilityFrameReadPort.ReadAbilityState(CharacterSkillId skill) =>
            ReadAvailabilityAbility(skill).AbilityExecutionState;
        EquipmentStateAggregate IFPAbilityFrameReadPort.Equipment => m_AvailabilityState.EquipmentState;

        sealed class ActionSnapshotView : IReadOnlyList<FPActionInstanceState>
        {
            readonly SimulationActorBinding m_Owner;
            internal ActionSnapshotView(SimulationActorBinding owner) { m_Owner = owner; }
            public int Count => m_Owner.m_AvailabilityState.ActionInstances.Length;
            public FPActionInstanceState this[int index] => m_Owner.m_AvailabilityState.ActionInstances[index];
            public IEnumerator<FPActionInstanceState> GetEnumerator()
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

        internal FPAbilityInvocationRuntime[] Invocations => m_Invocations;
        internal FPAbilityInvocationRuntime GetInvocation(CharacterSkillId abilityId) => m_InvocationByAbility[abilityId];
        internal bool IsInputRequestPending(FPCharacterRuntimeState state, in SimulationInputRequestState input)
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

        internal Dictionary<CharacterSkillId, IFPAbilityActivationPort> ActionRuntimes => m_ActionRuntimes;
        internal FPGameplayEffectExecutionScratch EffectExecutionScratch => m_EffectExecutionScratch;
        internal FPAbilityExecutionWorkspace[] Workspaces => m_Workspaces;
        internal List<AbilityTimelineAdvancePending> TimelineAdvances => m_TimelineAdvances;
        internal List<AbilityTimelineStopPending> TimelineStops => m_TimelineStops;
        public bool IsBlackboardLocked(CharacterSkillId ability, ulong action, ulong generation, int slot) =>
            GetInvocation(ability).IsBlackboardLocked(action, generation, slot);

        internal Dictionary<ulong, FPAbilityInvocationRuntime> ActionOwnerByInstanceId => m_ActionOwnerByInstanceId;
        internal List<AbilityTimelineLogicMotion> TimelineLogicMotion => m_TimelineLogicMotion;
        internal FPCharacterEvaluationOutput EvaluationOutput => m_EvaluationOutput;
        internal FPCharacterEvaluationResult EvaluationResult => m_EvaluationResult;
        internal FPCharacterTraceSink CharacterTraceSink => m_CharacterTraceSink;
        internal FPAbilityExecutionInput AbilityExecutionInput => m_AbilityExecutionInput;

        internal void RotateEvaluationOutput()
        {
            m_EvaluationOutput = m_RecycledEvaluationOutputs.Pop();
            m_EvaluationOutput.Clear();
        }

        internal void ReturnEvaluationOutput(FPCharacterEvaluationOutput output)
        {
            if (output == null || ReferenceEquals(output, m_EvaluationOutput))
                throw new InvalidOperationException("FP Character evaluation output ownership is invalid.");
            output.Clear();
            m_RecycledEvaluationOutputs.Push(output);
        }
        internal FPCharacterInputRequestState InputRequestState => m_InputRequestState;
        internal MotionWarpStatePageBank<MotionWarpState> MotionWarpPages { get; }
        internal FPCharacterRuntimeStatePageBank RuntimeStatePages { get; }
        internal FPAbilityStateDirectoryBank AbilityDirectories { get; }
        internal FPTimelineSnapshotPageBank TimelineSnapshotPages { get; }
        internal FPInputRequestPageBank InputRequestPages { get; }
        internal FPActionStatePageBank ActionStatePages { get; }
        internal Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>> StateValuePages { get; }
        internal AbilityStateValuePageBank<AbilityStateValue> CharacterBlackboardPages { get; }
        internal Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>> ExecutionStatePages { get; }
        internal FPCharacterActionRuntimeState ActionState => m_ActionState;
        internal CharacterControlRuntimeStateTransaction ControlState => m_ControlState;
        internal FPCharacterEventSequenceState EventSequenceState => m_EventSequenceState;
        internal FPCharacterHandleAllocatorState HandleAllocatorState => m_HandleAllocatorState;
        internal FPCharacterGameplayEffectRuntimeState GameplayEffectState => m_GameplayEffectState;
        internal FPCharacterEquipmentRuntimeState EquipmentState => m_EquipmentState;
        internal FPCharacterRuntimeStateTransaction RuntimeState => m_RuntimeState;

        internal void ClearActionRuntimes()
        {
            m_ActionRuntimes.Clear();
        }

        internal void ClearWorkspaces()
        {
            for (int i = 0; i < m_Workspaces.Length; i++)
                m_Workspaces[i].Reset();
            foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in ExecutionStatePages.Values)
                bank.ReleaseCurrent();
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int i = 0; i < SimulatedIncomingAttackResults.Length; i++)
                SimulatedIncomingAttackResults[i].ClearText();
#endif
            m_RuntimeState.Dispose();
            m_EquipmentState.Dispose();
            m_ControlState.Dispose();
            ClearWorkspaces();
            m_EvaluationResult.DiscardUnconsumed();
            m_EvaluationOutput.Clear();
            foreach (FPCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.Clear();
            m_RecycledEvaluationOutputs.Clear();
            for (int index = 0; index < m_Invocations.Length; index++)
                m_Invocations[index].DisposeStateExecutionText();
            ClearActionRuntimes();
            ClearTimelineTransfers();
            ClearTimelineMotionScratches();
            m_CombatQueries.Clear();
#if UNITY_EDITOR
            CombatQueryDiagnostics?.Dispose();
#endif
            m_ActionOwnerByInstanceId.Clear();
            if (AbilityInstallations.GameplayEffectCatalog != null)
                m_GameplayEffectState.ReleaseStorage();
        }

        public CharacterBodyMotionBinding BodyMotionBinding { get; }
        internal BodyMotionParameters<FP> BodyMotionParameters { get; }
        public CharacterGameplayEffectRuntimeBinding GameplayEffectRuntimeBinding { get; }
        public int GameplayOutputCapacity => checked(m_AbilityFactCapacity + m_AbilityPresentationCapacity + m_CombatResultCapacity +
            (AbilityInstallations.GameplayEffectCatalog == null ? 0 :
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.FactsPerTick +
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.PresentationRequestsPerTick));
        public CharacterEquipmentRuntimeBinding EquipmentRuntimeBinding { get; }
        public IAbilityTimelineRuntime TimelineRuntime { get; }
        public IAbilityTimelineLogicMotionReader TimelineMotionReader { get; }
        public FPGameplayAbilityExecutionInstallationSet AbilityInstallations { get; }
        public StableHash StateSchemaHash { get; }
        public StableHash GameplayContentHash { get; }

        static StableHash ComputeStateSchemaHash(
            CharacterControlRuntimeBinding control,
            CharacterInputRuntimeBinding input,
            CharacterGameplayEffectRuntimeBinding gameplayEffects,
            CharacterEquipmentRuntimeBinding equipment,
            FPGameplayAbilityExecutionInstallationSet abilities)
        {
            var parts = new List<string>
            {
                "fp-character-state-schema/6",
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                FPGameplayAbilityExecutionData data = abilities.Installations[i].Data;
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
            FPGameplayAbilityExecutionInstallationSet abilities,
            CharacterCombatRuntimeBinding combat)
        {
            var parts = new List<string>
            {
                "fp-simulation-actor-content/2",
                combat == null ? string.Empty : combat.ContentHash.ToString(),
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                bodyMotion.BindingHash.ToString(),
                abilities.BlackboardContentHash.ToString(),
                abilities.ConfigurationContentHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                FPGameplayAbilityExecutionData data = abilities.Installations[i].Data;
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
                : throw new ArgumentException("FP Timeline runtime must expose pending logic motion.", nameof(timelineRuntime));
        }



    }
}
