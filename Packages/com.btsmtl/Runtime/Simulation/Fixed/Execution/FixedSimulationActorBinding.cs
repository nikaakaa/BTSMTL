using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedMotionContributionScratch
    {
        SimulationMotionContribution[] m_Values = Array.Empty<SimulationMotionContribution>();

        public SimulationMotionContribution[] Values => m_Values;
        public int Count { get; private set; }

        public void Begin() => Clear();

        public void Append(in SimulationMotionContribution value)
        {
            if (Count == m_Values.Length)
            {
                int capacity = Math.Max(4, m_Values.Length * 2);
                var values = new SimulationMotionContribution[capacity];
                Array.Copy(m_Values, values, Count);
                m_Values = values;
            }

            value.Retain();
            m_Values[Count++] = value;
        }

        public void Clear()
        {
            for (int index = 0; index < Count; index++)
                m_Values[index].Release();
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }
    }

    internal sealed class FixedCharacterEvaluationOutput
    {
        public FixedCombatResultStorage AttackResults { get; private set; }
        public FixedCharacterEvaluationOutput(int invocationCount, int combatCapacity = 0, int presentationCapacity = 0)
        {
            ValueTraces = new SimulationValueTraceStorage(invocationCount);
            PrepareCombat(combatCapacity);
            Presentation.Capacity = presentationCapacity;
        }
        internal SimulationValueTraceStorage ValueTraces { get; }
#if UNITY_EDITOR
        internal long EvaluationStateElapsedStopwatchTicks;
        internal long EvaluationInputsElapsedStopwatchTicks;
        internal long EvaluationControlElapsedStopwatchTicks;
        internal long EvaluationAbilitiesElapsedStopwatchTicks;
        internal long EvaluationAbilitiesTickElapsedStopwatchTicks;
        internal long EvaluationAbilitiesExistingElapsedStopwatchTicks;
        internal long EvaluationAbilitiesPendingElapsedStopwatchTicks;
        internal long EvaluationAbilitiesActiveElapsedStopwatchTicks;
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
        internal void BindValueTraces(FixedAbilityExecutionWorkspace[] workspaces)
        {
            for (int index = 0; index < workspaces.Length; index++)
            { workspaces[index].TraceOutput = this; workspaces[index].ValueTraces = ValueTraces; workspaces[index].ValueTraceInvocation = index; }
        }
        internal void PrepareCombat(int capacity) => AttackResults = new FixedCombatResultStorage(capacity);
        public SimulationActorTickResult TickResult { get; } = new SimulationActorTickResult();
        internal GameplayCommandBatchAdmission IngressAdmission { get; set; }
        internal List<GameplayCommandBatchAdmission> CommandBatchRejections { get; } = new List<GameplayCommandBatchAdmission>();
        internal void PrepareCommandBatchResults(int capacity) => CommandBatchRejections.Capacity = capacity;
        public List<GameplayFact> Facts { get; } = new List<GameplayFact>();
        public List<PresentationCommand> Presentation { get; } = new List<PresentationCommand>();
        public List<SimulationTraceRecord> Trace { get; } = new List<SimulationTraceRecord>();
        public List<SimulationTraceRecord> CharacterTrace { get; } = new List<SimulationTraceRecord>();
        internal FixedGameplayEffectPendingProjection[] GameplayEffectProjections { get; private set; } =
            Array.Empty<FixedGameplayEffectPendingProjection>();
        internal int GameplayEffectProjectionCount { get; set; }
        internal MotionWarpStatePageReference<FixedMotionWarpState> MotionWarpStates { get; private set; }
        FixedCharacterRuntimeState m_StatePageOwner;
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

        internal void RetainStatePages(FixedCharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner = state;
        }

        internal void ReplaceStatePageOwner(FixedCharacterRuntimeState state)
        {
            state.RetainStatePages();
            m_StatePageOwner.ReleaseStatePages();
            m_StatePageOwner = state;
        }

        internal void BeginMotionWarpState(
            MotionWarpStatePageBank<FixedMotionWarpState> bank,
            in MotionWarpStatePageReference<FixedMotionWarpState> source)
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
            EvaluationControlElapsedStopwatchTicks = 0;
            EvaluationAbilitiesElapsedStopwatchTicks = 0;
            EvaluationAbilitiesTickElapsedStopwatchTicks = 0;
            EvaluationAbilitiesExistingElapsedStopwatchTicks = 0;
            EvaluationAbilitiesPendingElapsedStopwatchTicks = 0;
            EvaluationAbilitiesActiveElapsedStopwatchTicks = 0;
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
            GameplayEffectProjections = new FixedGameplayEffectPendingProjection[changes];
            Facts.Capacity = facts;
            Presentation.Capacity = presentation;
        }

        internal void FinalizeGameplayEffects()
        {
            for (int index = 0; index < GameplayEffectProjectionCount; index++)
                GameplayEffectProjections[index].Project(Facts, Presentation);
        }
    }

    public sealed class SimulationActorBinding : IDisposable, IFixedAbilityAvailabilityReadOwner, IFixedAbilityFrameReadPort
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal byte SimulatedIncomingAttacks;
        internal readonly CombatHitResult[] SimulatedIncomingAttackResults =
            { new CombatHitResult(), new CombatHitResult() };
#endif
        readonly FixedCharacterControlMotionRuntime m_ControlMotion;
        readonly FixedMotionContributionScratch m_MotionContributions = new FixedMotionContributionScratch();
        readonly FixedAbilityInvocationRuntime[] m_Invocations;
        readonly Dictionary<CharacterSkillId, FixedAbilityInvocationRuntime> m_InvocationByAbility;
        readonly Dictionary<CharacterSkillId, IFixedAbilityActivationPort> m_ActionRuntimes;
        readonly FixedGameplayEffectExecutionScratch m_EffectExecutionScratch = new FixedGameplayEffectExecutionScratch();
        readonly List<AbilityTimelineAdvancePending> m_TimelineAdvances = new List<AbilityTimelineAdvancePending>();
        readonly List<AbilityTimelineStopPending> m_TimelineStops = new List<AbilityTimelineStopPending>();
        readonly Dictionary<ulong, FixedAbilityInvocationRuntime> m_ActionOwnerByInstanceId;
        readonly List<AbilityTimelineLogicMotion> m_TimelineLogicMotion = new List<AbilityTimelineLogicMotion>();
        readonly FixedAbilityExecutionWorkspace[] m_Workspaces;
        FixedCharacterEvaluationOutput m_EvaluationOutput;
        readonly Stack<FixedCharacterEvaluationOutput> m_RecycledEvaluationOutputs =
            new Stack<FixedCharacterEvaluationOutput>();
        readonly FixedCharacterEvaluationResult m_EvaluationResult;
        readonly FixedCharacterTraceSink m_CharacterTraceSink;
        readonly FixedAbilityExecutionInput m_AbilityExecutionInput = new FixedAbilityExecutionInput();
        readonly FixedCharacterInputRequestState m_InputRequestState = new FixedCharacterInputRequestState();
        readonly FixedCharacterActionRuntimeState m_ActionState;
        readonly CharacterControlRuntimeStateTransaction m_ControlState = new CharacterControlRuntimeStateTransaction();
        readonly FixedCharacterEventSequenceState m_EventSequenceState = new FixedCharacterEventSequenceState();
        readonly FixedCharacterHandleAllocatorState m_HandleAllocatorState = new FixedCharacterHandleAllocatorState();
        readonly FixedCharacterGameplayEffectRuntimeState m_GameplayEffectState = new FixedCharacterGameplayEffectRuntimeState();
        readonly FixedCharacterEquipmentRuntimeState m_EquipmentState = new FixedCharacterEquipmentRuntimeState();
        readonly FixedCharacterRuntimeStateTransaction m_RuntimeState = new FixedCharacterRuntimeStateTransaction();
        FixedCombatQueryStorage m_CombatQueries;
        int m_CombatResultCapacity;
        readonly int m_PresentationCapacity;
        int m_GameplayEffectChangeCapacity;
        int m_TraceRecordsPerTick;
        int m_FactCapacity;
        int m_OutputPresentationCapacity;
        internal CanonicalReader CheckpointControlReader { get; } = new CanonicalReader(Array.Empty<byte>());
        internal CharacterControlStateSchema CheckpointControlSchema { get; private set; }
        internal Func<CanonicalReader, string> DeclaredIdentityReader { get; private set; }
        internal int DeclaredCheckpointIdentityCount { get; private set; }

        internal void PrepareCheckpointControlSchema(CharacterControlModuleContract contract)
        {
            CheckpointControlSchema = contract.StateSchema;
            var identities = new List<string> { contract.ModuleId.Value };
            for (int index = 0; index < contract.States.Count; index++)
                identities.Add(contract.States[index].Id.Value);
            for (int index = 0; index < contract.Transitions.Count; index++)
                identities.Add(contract.Transitions[index].Id.Value);
            for (int ability = 0; ability < AbilityInstallations.Installations.Count; ability++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[ability];
                FixedGameplayAbilityExecutionData data = installation.Data;
                identities.Add(installation.Identity.AbilityId.Value);
                identities.Add(installation.Identity.OperationSetVersion.Value);
                for (int entry = 0; entry < data.CatalogEntries.Count; entry++)
                {
                    ProgramCatalogEntry item = data.CatalogEntries[entry];
                    identities.Add(item.Identity);
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
                EquipmentProgramLayout equipment = EquipmentProgramLayoutCompiler.CompileRoleStateLayout(EquipmentRuntimeBinding);
                for (int index = 0; index < equipment.Items.Count; index++)
                    identities.Add(equipment.Items[index].EquipmentId.Value);
                for (int index = 0; index < equipment.Features.Count; index++)
                    identities.Add(equipment.Features[index].FeatureId.Value);
                for (int index = 0; index < equipment.Routes.Count; index++)
                    identities.Add(equipment.Routes[index].RouteId.Value);
            }
            for (int index = 0; index < m_CodecActionProfiles.Length; index++)
                identities.Add(m_CodecActionProfiles[index].ActionId);
            var uniqueIdentities = new HashSet<string>(identities, StringComparer.Ordinal);
            uniqueIdentities.Remove(null);
            uniqueIdentities.Remove(string.Empty);
            DeclaredCheckpointIdentityCount = uniqueIdentities.Count;
            var reader = new GameplayEffectPublishedIdentityReader(identities);
            DeclaredIdentityReader = reader.ReadPublished;
        }

        ActionAdmissionProfile[] m_CodecActionProfiles = Array.Empty<ActionAdmissionProfile>();
        byte[][] m_CodecActionIdentities = Array.Empty<byte[]>();

        internal void PrepareCombatResultCapacity(int capacity)
        {
            m_CombatResultCapacity = capacity;
            m_EvaluationOutput.PrepareCombat(capacity);
            foreach (FixedCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareCombat(capacity);
        }

        internal void PrepareEvaluationOutputCapacity(int maximumStepCount)
        {
            int additionalPages = maximumStepCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new FixedCharacterEvaluationOutput(AbilityInstallations.Installations.Count, m_CombatResultCapacity, m_OutputPresentationCapacity);
                output.PrepareTraceStorage(m_TraceRecordsPerTick);
                output.PrepareCommandBatchResults(CommandBatchResultCapacity);
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
            foreach (FixedCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareTraceStorage(traceRecordsPerTick);
            for (int index = 0; index < m_Workspaces.Length; index++)
                m_Workspaces[index].Trace.Capacity = traceRecordsPerTick;
        }

        internal int MaximumDetachedCheckpointTextReferences
        {
            get
            {
                FixedGameplayEffectRuntimeCatalog catalog = AbilityInstallations.GameplayEffectCatalog;
                int gameplay = catalog == null ? 0 : catalog.CapacityLayout.MaximumRetainedTextReferences;
                int outputs = checked((m_RecycledEvaluationOutputs.Count + 1) *
                    (m_FactCapacity * GameplayFact.MaximumTextReferences +
                     m_OutputPresentationCapacity * PresentationCommand.MaximumTextReferences +
                     m_GameplayEffectChangeCapacity * FixedGameplayEffectPendingProjection.MaximumTextReferences +
                     m_TraceRecordsPerTick * SimulationTraceRecord.MaximumTextReferences));
                int workspace = checked(m_Workspaces.Length * m_GameplayEffectChangeCapacity *
                    FixedGameplayEffectPendingProjection.MaximumTextReferences);
                return checked(gameplay + outputs + workspace + m_TraceRecordsPerTick * SimulationTraceRecord.MaximumTextReferences);
            }
        }

        internal int MaximumDetachedCheckpointTextPayloads
        {
            get
            {
                FixedGameplayEffectRuntimeCatalog catalog = AbilityInstallations.GameplayEffectCatalog;
                if (catalog == null)
                    return 0;
                GameplayEffectRuntimeCapacityLayout layout = catalog.CapacityLayout;
                int stateObjects = checked((layout.Actor.ActiveEffects + layout.PredictionRecordWidth) * layout.StatePageCount);
                int detachedSpecs = layout.SpecCount - stateObjects;
                int detachedActive = layout.ActiveObjectCount - stateObjects;
                int detachedPredictions = layout.PredictionRecordCount -
                    checked(layout.PredictionRecordWidth * layout.StatePageCount);
                return checked(detachedSpecs + detachedActive + 2 * detachedPredictions);
            }
        }

        public void PrepareGameplayEffects(in GameplayEffectSessionCapacity session, int maximumSessionCheckpointPayloadBytes)
        {
            SessionCapacity = session;
            PrepareActionIdentities();
            PrepareCommandBatchResults();
            InputRequestPages.PrepareCapacity(session.StatePageCount);
            ActionStatePages.PrepareCapacity(session.StatePageCount);
            RuntimeStatePages.PrepareCapacity(session.StatePageCount);
            MotionWarpPages.PrepareCapacity(session.StatePageCount);
            AbilityDirectories.PrepareCapacity(session.StatePageCount);
            TimelineSnapshotPages.PrepareCapacity(session.StatePageCount);
            if (TimelineRuntime != null)
            {
                TimelineRuntime.PrepareSnapshotStorage(in session, maximumSessionCheckpointPayloadBytes);
                m_TimelineLogicMotion.Capacity = TimelineMotionReader.MaximumMotionContributionsPerAdvance;
            }
            foreach (AbilityStateValuePageBank<AbilityStateValue> bank in StateValuePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in ExecutionStatePages.Values)
                bank.PrepareCapacity(session.StatePageCount);
            if (AbilityInstallations.RequiresEquipment)
                m_EquipmentState.Prepare(ActorId,
                    EquipmentProgramLayoutCompiler.CompileRoleStateLayout(EquipmentRuntimeBinding), in session);
            FixedGameplayEffectRuntimeCatalog catalog = AbilityInstallations.GameplayEffectCatalog;
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
            int checkpointOwners = checked(session.EncodedCheckpoints + session.CapturePayloads + session.RestorePayloads);
            for (int index = 0; index < m_Invocations.Length; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                int identitySlots = 0;
                for (int slot = 0; slot < installation.Layout.StateSlots.Count; slot++)
                    if (installation.Layout.StateSlots[slot].ValueKind == ProgramStateValueKind.Identity)
                        identitySlots++;
                int stateValues = checked(identitySlots *
                    (ExecutionStatePages[installation.Identity.AbilityId].FrameCapacity + 1));
                int retainedTracePaths = checked(m_TraceRecordsPerTick *
                    SimulationTraceRecord.MaximumGeneratedPathReferences * (m_RecycledEvaluationOutputs.Count + 1));
                int retainedTextReferences = checked(stateValues * (session.StatePageCount + checkpointOwners) + retainedTracePaths);
                m_Invocations[index].PrepareStateExecutionText(maximumSessionCheckpointPayloadBytes, retainedTextReferences);
            }
        }

        void PrepareCommandBatchResults()
        {
            long total = 0;
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
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
                long count = (long)installation.Control.MaximumOperationEvaluations * repliesPerEvaluation +
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
            foreach (FixedCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
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
            m_CodecActionIdentities = new byte[count][];
            int index = 0;
            for (int ability = 0; ability < AbilityInstallations.Installations.Count; ability++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[ability];
                IReadOnlyList<ProgramCatalogEntry> entries = installation.Data.CatalogEntries;
                for (int entry = 0; entry < entries.Count; entry++)
                {
                    if (entries[entry].Kind != ProgramCatalogEntryKind.Action)
                        continue;
                    ActionAdmissionProfile profile = installation.Services.RequireAdmissionProfile(
                        entries[entry].Identity.Substring("action:".Length));
                    m_CodecActionProfiles[index] = profile;
                    m_CodecActionIdentities[index] = new byte[CanonicalTextEncoding.ByteCount(profile.ActionId.AsSpan())];
                    CanonicalTextEncoding.Encode(profile.ActionId.AsSpan(), m_CodecActionIdentities[index]);
                    index++;
                }
            }
        }

        internal string ReadActionIdentity(CanonicalReader reader)
        {
            ReadOnlySpan<byte> encoded = reader.ReadTextSegment().AsSpan();
            for (int index = 0; index < m_CodecActionProfiles.Length; index++)
                if (encoded.SequenceEqual(m_CodecActionIdentities[index]))
                    return m_CodecActionProfiles[index].ActionId;
            throw new InvalidDataException("Character state Action identity is absent from its installed Action Admission Profiles.");
        }

        internal void PrepareGameplayEffectOutputs(int changes, int facts, int presentation, int outputPageCount)
        {
            m_GameplayEffectChangeCapacity = changes;
            m_FactCapacity = checked(m_PresentationCapacity + facts);
            m_OutputPresentationCapacity = checked(m_PresentationCapacity + presentation);
            m_EvaluationOutput.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            foreach (FixedCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.PrepareGameplayEffectOutputs(changes, m_FactCapacity, m_OutputPresentationCapacity);
            int additionalPages = outputPageCount - m_RecycledEvaluationOutputs.Count;
            for (int index = 0; index < additionalPages; index++)
            {
                var output = new FixedCharacterEvaluationOutput(AbilityInstallations.Installations.Count, m_CombatResultCapacity, m_OutputPresentationCapacity);
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
            CharacterBodyMotionBinding bodyMotionBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectRuntimeBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GameplayAbilityExecutionDataSet<FixedGameplayAbilityExecutionData> abilityData,
            IAbilityTimelineRuntime timelineRuntime,
            CharacterCombatRuntimeBinding combatBinding = null)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Actor identity is invalid.", nameof(actorId));
            ActorId = actorId;
            CombatBinding = combatBinding;
            m_CombatResultCapacity = combatBinding == null ? 0 : combatBinding.ResultCapacity;
            m_CombatQueries = new FixedCombatQueryStorage(combatBinding == null ? 0 : combatBinding.AttackCapacity);
            WorldBodyBindingId = SimulationIdentity.Require(worldBodyBindingId, nameof(worldBodyBindingId));
            ControlRuntimeBinding = controlRuntimeBinding ?? throw new ArgumentNullException(nameof(controlRuntimeBinding));
            InputRuntimeBinding = inputRuntimeBinding;
            ControlMotionBindings = new FixedCharacterControlMotionBindingCatalog(controlRuntimeBinding.MotionBindings);
            m_ControlMotion = new FixedCharacterControlMotionRuntime(ControlMotionBindings);
            BodyMotionBinding = bodyMotionBinding ?? throw new ArgumentNullException(nameof(bodyMotionBinding));
            AbilityInstallations = new FixedGameplayAbilityExecutionInstallationSet(
                abilityData ?? throw new ArgumentNullException(nameof(abilityData)),
                inputRuntimeBinding,
                gameplayEffectRuntimeBinding,
                equipmentRuntimeBinding, actorId);
            m_EvaluationOutput = new FixedCharacterEvaluationOutput(AbilityInstallations.Installations.Count, combatBinding == null ? 0 : combatBinding.ResultCapacity);
            RuntimeStatePages = new FixedCharacterRuntimeStatePageBank();
            AbilityDirectories = new FixedAbilityStateDirectoryBank(AbilityInstallations.Installations.Count);
            TimelineSnapshotPages = new FixedTimelineSnapshotPageBank();
            int inputRequestCapacity = InputRuntimeBinding.InputRequestIds.Count;
            InputRequestPages = new FixedInputRequestPageBank(inputRequestCapacity);
            StateValuePages = new Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>>(
                AbilityInstallations.Installations.Count);
            ExecutionStatePages = new Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>>(
                AbilityInstallations.Installations.Count);
            for (int index = 0; index < AbilityInstallations.Installations.Count; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                ExecutionStatePages.Add(installation.Identity.AbilityId,
                    new GameplayAbilityExecutionStateBank<AbilityStateValue>(installation.Identity,
                        installation.Layout.StateSlots.Count, installation.Layout.ActionInstanceCapacity,
                        _ => true, value => value.Retain(), value => value.Release()));
                StateValuePages.Add(installation.Identity.AbilityId,
                    new AbilityStateValuePageBank<AbilityStateValue>(installation.Identity,
                        installation.Layout.StateSlots.Count, _ => true, value => value.Retain(), value => value.Release()));
            }
            var warpLayouts = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>[AbilityInstallations.Installations.Count];
            for (int index = 0; index < warpLayouts.Length; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[index];
                warpLayouts[index] = new KeyValuePair<CharacterSkillId, IReadOnlyList<int>>(
                    installation.Identity.AbilityId, installation.Layout.MotionWarpOperationIds);
            }
            var motionWarpLayout = new MotionWarpStatePageLayout(warpLayouts);
            MotionWarpPages = new MotionWarpStatePageBank<FixedMotionWarpState>(motionWarpLayout, value => value.Retain(), value => value.Release());
            m_RuntimeState.Initialize(AbilityInstallations.Installations, motionWarpLayout);
            int actionCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                actionCapacity = checked(actionCapacity + AbilityInstallations.Installations[i].Layout.ActionInstanceCapacity);
            m_ActionState = new FixedCharacterActionRuntimeState(actionCapacity, AbilityInstallations.Installations);
            ActionStatePages = new FixedActionStatePageBank(actionCapacity);
            m_ActionOwnerByInstanceId = new Dictionary<ulong, FixedAbilityInvocationRuntime>(actionCapacity);
            m_Invocations = new FixedAbilityInvocationRuntime[AbilityInstallations.Installations.Count];
            m_InvocationByAbility = new Dictionary<CharacterSkillId, FixedAbilityInvocationRuntime>(
                AbilityInstallations.Installations.Count);
            m_ActionRuntimes = new Dictionary<CharacterSkillId, IFixedAbilityActivationPort>(
                AbilityInstallations.Installations.Count);
            m_Workspaces = new FixedAbilityExecutionWorkspace[AbilityInstallations.Installations.Count];
            for (int i = 0; i < m_Workspaces.Length; i++)
            {
                m_Workspaces[i] = new FixedAbilityExecutionWorkspace(
                    m_EffectExecutionScratch,
                    AbilityInstallations.Installations[i].Data,
                    AbilityInstallations.Installations[i].Layout,
                    m_TimelineAdvances,
                    m_TimelineStops);
            }
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                FixedGameplayAbilityExecutionData data = AbilityInstallations.Installations[i].Data;
                if (data.NumericProfile != FixedSimulationNumericProfile.Value)
                    throw new InvalidOperationException($"Ability '{data.AbilityId}' does not target Fixed.");
            }
            GameplayEffectRuntimeBinding = gameplayEffectRuntimeBinding;
            EquipmentRuntimeBinding = equipmentRuntimeBinding;
            TimelineRuntime = timelineRuntime;
            int timelineRequestCapacity = 0;
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
                timelineRequestCapacity = checked(timelineRequestCapacity + AbilityInstallations.Installations[i].Data.Operations.Count);
            m_TimelineAdvances.Capacity = timelineRequestCapacity;
            m_TimelineStops.Capacity = timelineRequestCapacity;
            m_PresentationCapacity = checked(timelineRequestCapacity * 2);
            m_OutputPresentationCapacity = m_PresentationCapacity;
            m_FactCapacity = m_PresentationCapacity;
            m_EvaluationOutput.Facts.Capacity = m_FactCapacity;
            m_EvaluationOutput.Presentation.Capacity = m_PresentationCapacity;
            m_EvaluationResult = new FixedCharacterEvaluationResult(
                this,
                actorId,
                timelineRuntime,
                timelineRequestCapacity);
            IFixedAbilityDomainRuntimeFactory domainRuntimeFactory = new FixedAbilityDomainRuntimeFactory();
            IFixedAbilityExecutionServiceFactory serviceFactory = new FixedAbilityExecutionServiceFactory(timelineRuntime, m_ActionRuntimes);
            for (int i = 0; i < AbilityInstallations.Installations.Count; i++)
            {
                FixedGameplayAbilityExecutionInstallation installation = AbilityInstallations.Installations[i];
                m_Invocations[i] = new FixedAbilityInvocationRuntime(
                    installation.Execution,
                    actorId,
                    AbilityInstallations,
                    domainRuntimeFactory,
                    installation.EquipmentLayout,
                    m_Workspaces[i],
                    installation.Control,
                    serviceFactory);
                m_InvocationByAbility.Add(installation.Data.AbilityId, m_Invocations[i]);
            }

            m_AvailabilityActions = new ActionSnapshotView(this);
            m_AvailabilityPending = new PendingSnapshotView(this);
            ActivationRules = new FixedAbilityActivationRuleRuntime(this);
            m_CharacterTraceSink = new FixedCharacterTraceSink(m_EvaluationOutput.CharacterTrace);
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

        internal FixedAbilityActivationRuleRuntime ActivationRules { get; }
        public ActorId ActorId { get; }
        public CharacterCombatRuntimeBinding CombatBinding { get; }
#if UNITY_EDITOR
        public FixedCombatQueryDiagnostics CombatQueryDiagnostics { get; internal set; }
#endif
        internal FixedCombatQueryStorage CombatQueries => m_CombatQueries;
        public string WorldBodyBindingId { get; }
        public CharacterControlRuntimeBinding ControlRuntimeBinding { get; }
        public CharacterInputRuntimeBinding InputRuntimeBinding { get; }
        internal FixedCharacterControlMotionBindingCatalog ControlMotionBindings { get; }
        internal FixedCharacterControlMotionRuntime ControlMotion => m_ControlMotion;
        internal FixedMotionContributionScratch MotionContributions => m_MotionContributions;
        internal int AvailabilityIngressCapacity { get; private set; }
        internal void PrepareAvailabilityIngressCapacity(int capacity) => AvailabilityIngressCapacity = capacity;
        IFixedCommittedInputReadPort m_CommittedInput;
        internal void BindCommittedInput(IFixedCommittedInputReadPort input) => m_CommittedInput = input;
        internal void CaptureAvailabilityFrameEvents(SimulationIngress[] ingress, int count)
        {
            if (m_CommittedInput != null)
                m_CommittedInput.CaptureFrameEvents(ActorId, new ReadOnlySpan<SimulationIngress>(ingress, 0, count));
        }
        bool IFixedAbilityFrameReadPort.HasActionEvent(ulong instanceId, string eventId, CanonicalTextValue targetId) =>
            m_AvailabilityInput.HasActionEvent(ActorId, instanceId, eventId, targetId);

        FixedCharacterRuntimeState m_AvailabilityState;
        IFixedCommittedInputReadPort m_AvailabilityInput;
        FixedCharacterControlRuntime m_AvailabilityControl;
        readonly ActionSnapshotView m_AvailabilityActions;
        readonly PendingSnapshotView m_AvailabilityPending;
        FixedAbilityBodyFacts m_AvailabilityBody;

        internal void QueryAbilityAvailability(FixedCharacterRuntimeState state,
            WorldBodyState body, IFixedCommittedInputReadPort input, FixedCharacterControlRuntime control,
            GameplayAbilityRulePhase phase, out GameplayAbilityAvailabilityResult result)
        {
            state.RetainStatePages();
            m_AvailabilityState = state;
            m_AvailabilityInput = input;
            m_AvailabilityControl = control;
            m_AvailabilityBody = new FixedAbilityBodyFacts(ActorId, body);
            try
            {
                if (AbilityInstallations.GameplayEffectCatalog != null)
                    m_EffectExecutionScratch.Target.BeginAvailabilityRead(state.GameplayEffectState,
                        AbilityInstallations.GameplayEffectCatalog, ActorId, ((IFixedAbilityFrameReadPort)this).Tick);
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

        SimulationTick IFixedAbilityFrameReadPort.Tick => new SimulationTick(m_AvailabilityState.LastCompletedTick);
        SimulationTick IFixedAbilityAvailabilityReadOwner.Tick => ((IFixedAbilityFrameReadPort)this).Tick;
        ulong IFixedAbilityFrameReadPort.InputSequence => m_AvailabilityInput.ReadInputSequence(ActorId);
        FixedAbilityBodyFacts IFixedAbilityFrameReadPort.BodyFacts => m_AvailabilityBody;
        bool IFixedAbilityAvailabilityReadOwner.HasMoveInput => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).HasMoveInput;
        CharacterLocomotionMode IFixedAbilityAvailabilityReadOwner.LocomotionMode => m_AvailabilityControl.ReadDecision(m_AvailabilityState.ControlState, this).LocomotionMode;
        int IFixedAbilityAvailabilityReadOwner.ActionCount => m_AvailabilityState.ActionInstances.Length;
        GameplayAbilitySourceContext IFixedAbilityAvailabilityReadOwner.ReadActiveSource(int index)
        {
            ref readonly FixedActionInstanceState action = ref m_AvailabilityState.ActionInstances[index];
            return action.IsActive ? action.SourceContext : default;
        }
        ulong IFixedAbilityAvailabilityReadOwner.ReadSourceStartTick(int index) => m_AvailabilityState.ActionInstances[index].StartTick;
        bool IFixedAbilityAvailabilityReadOwner.TryReadSource(in GameplayAbilitySourceContext source, out AbilityLifecyclePhase phase)
        {
            bool matched = ((IFixedAbilityFrameReadPort)this).TryReadAction(source.InstanceId, out FixedActionInstanceState action) && action.SourceContext.Equals(source);
            phase = matched ? (AbilityLifecyclePhase)(byte)action.Phase : default;
            return matched;
        }
        SimulationInputValue IFixedAbilityFrameReadPort.ReadInputValue(CanonicalTextValue inputId, ProgramInputValueKind kind) =>
            m_AvailabilityInput.ReadInputValue(ActorId, inputId, kind);
        SimulationInputRequestState IFixedAbilityFrameReadPort.ReadInputRequest(CanonicalTextValue id)
        {
            var requests = m_AvailabilityState.InputRequests;
            for (int index = 0; index < requests.Length; index++)
                if (id.Equals(requests[index].Key)) return requests[index].Value;
            return default;
        }
        SimulationInputRequestState IFixedAbilityAvailabilityReadOwner.ReadInputRequest(string id) =>
            ((IFixedAbilityFrameReadPort)this).ReadInputRequest(id);
        bool IFixedAbilityAvailabilityReadOwner.IsInputRequestPending(in SimulationInputRequestState input) =>
            IsInputRequestPending(m_AvailabilityState, in input);
        bool IFixedAbilityAvailabilityReadOwner.HasGameplayEvent(string eventId) =>
            m_AvailabilityInput.HasGameplayEvent(ActorId, eventId);
        bool IFixedAbilityAvailabilityReadOwner.IsActionWindowActive(in GameplayAbilitySourceContext source, string windowTag) =>
            IsActionWindowActive(m_AvailabilityState, in source, windowTag);
        bool IFixedAbilityAvailabilityReadOwner.EvaluateRuleCondition(CharacterSkillId skillId, in GameplayAbilitySourceContext source, OperationHandle condition) =>
            GetInvocation(skillId).EvaluateRuleCondition(ActivationRules, in source, condition, GameplayAbilityRuleEvaluationMode.Availability);
        void IFixedAbilityAvailabilityReadOwner.PreviewRule(GameplayAbilityExecutionBinding ability, GameplayAbilityActivationRuleBinding rule,
            in GameplayAbilitySourceContext source, in SimulationInputRequestState input, out ActionAdmissionDecision admission) =>
            GetInvocation(ability.SkillId).PreviewAvailability(ability, rule, in source, in input, out admission);
        IReadOnlyList<FixedActionInstanceState> IFixedAbilityFrameReadPort.Actions => m_AvailabilityActions;
        IReadOnlyList<ThirdPersonSimulation.SimulationActionActivationRequestState<SimulationActionTargetSnapshot>> IFixedAbilityFrameReadPort.Pending => m_AvailabilityPending;
        bool IFixedAbilityFrameReadPort.TryReadAction(ulong instanceId, out FixedActionInstanceState action)
        {
            var actions = m_AvailabilityState.ActionInstances;
            for (int index = 0; index < actions.Length; index++)
                if (actions[index].InstanceId == instanceId) { action = actions[index]; return true; }
            action = default;
            return false;
        }
        FixedAbilityRuntimeState ReadAvailabilityAbility(CharacterSkillId skill)
        {
            var abilities = m_AvailabilityState.Abilities;
            for (int index = 0; index < abilities.Count; index++)
                if (abilities[index].AbilityIdentity.AbilityId == skill) return abilities[index];
            throw new InvalidOperationException("Availability skill is absent from the committed ability directory.");
        }
        AbilityStateValue IFixedAbilityFrameReadPort.ReadState(CharacterSkillId skill, int slot, in GameplayAbilitySourceContext source)
        {
            var installation = AbilityInstallations.Require(skill);
            var ability = ReadAvailabilityAbility(skill);
            if (installation.Layout.IsSkillExecutionStateSlot(slot) && source.IsValid && source.SkillId == skill)
            {
                var frame = ability.AbilityExecutionState.Find(source.InstanceId);
                return frame != null && frame.TryGetValue(slot, out AbilityStateValue value)
                    ? value : installation.Data.DefaultStateValue(slot);
            }
            return ability.StateValues.TryGetValue(slot, out AbilityStateValue stored) ? stored : installation.Data.DefaultStateValue(slot);
        }
        GameplayAbilityExecutionAggregate<AbilityStateValue> IFixedAbilityFrameReadPort.ReadAbilityState(CharacterSkillId skill) =>
            ReadAvailabilityAbility(skill).AbilityExecutionState;
        EquipmentStateAggregate IFixedAbilityFrameReadPort.Equipment => m_AvailabilityState.EquipmentState;

        sealed class ActionSnapshotView : IReadOnlyList<FixedActionInstanceState>
        {
            readonly SimulationActorBinding m_Owner;
            internal ActionSnapshotView(SimulationActorBinding owner) { m_Owner = owner; }
            public int Count => m_Owner.m_AvailabilityState.ActionInstances.Length;
            public FixedActionInstanceState this[int index] => m_Owner.m_AvailabilityState.ActionInstances[index];
            public IEnumerator<FixedActionInstanceState> GetEnumerator()
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

        internal FixedAbilityInvocationRuntime[] Invocations => m_Invocations;
        internal FixedAbilityInvocationRuntime GetInvocation(CharacterSkillId abilityId) => m_InvocationByAbility[abilityId];
        internal bool IsActionWindowActive(FixedCharacterRuntimeState state,
            in GameplayAbilitySourceContext source, string windowTag) =>
            FixedBlackboardSnapshotReader.IsFrameWindowActive(AbilityInstallations.Require(source.SkillId), state, in source, windowTag);

        internal bool IsInputRequestPending(FixedCharacterRuntimeState state, in SimulationInputRequestState input)
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

        internal Dictionary<CharacterSkillId, IFixedAbilityActivationPort> ActionRuntimes => m_ActionRuntimes;
        internal FixedGameplayEffectExecutionScratch EffectExecutionScratch => m_EffectExecutionScratch;
        internal FixedAbilityExecutionWorkspace[] Workspaces => m_Workspaces;
        internal List<AbilityTimelineAdvancePending> TimelineAdvances => m_TimelineAdvances;
        internal List<AbilityTimelineStopPending> TimelineStops => m_TimelineStops;
        internal Dictionary<ulong, FixedAbilityInvocationRuntime> ActionOwnerByInstanceId => m_ActionOwnerByInstanceId;
        internal List<AbilityTimelineLogicMotion> TimelineLogicMotion => m_TimelineLogicMotion;
        internal FixedCharacterEvaluationOutput EvaluationOutput => m_EvaluationOutput;
        internal FixedCharacterEvaluationResult EvaluationResult => m_EvaluationResult;
        internal FixedCharacterTraceSink CharacterTraceSink => m_CharacterTraceSink;
        internal FixedAbilityExecutionInput AbilityExecutionInput => m_AbilityExecutionInput;

        internal void RotateEvaluationOutput()
        {
            m_EvaluationOutput = m_RecycledEvaluationOutputs.Pop();
            m_EvaluationOutput.Clear();
        }

        internal void ReturnEvaluationOutput(FixedCharacterEvaluationOutput output)
        {
            if (output == null || ReferenceEquals(output, m_EvaluationOutput))
                throw new InvalidOperationException("Fixed Character evaluation output ownership is invalid.");
            output.Clear();
            m_RecycledEvaluationOutputs.Push(output);
        }
        internal FixedCharacterInputRequestState InputRequestState => m_InputRequestState;
        internal MotionWarpStatePageBank<FixedMotionWarpState> MotionWarpPages { get; }
        internal FixedCharacterRuntimeStatePageBank RuntimeStatePages { get; }
        internal FixedAbilityStateDirectoryBank AbilityDirectories { get; }
        internal FixedTimelineSnapshotPageBank TimelineSnapshotPages { get; }
        internal FixedInputRequestPageBank InputRequestPages { get; }
        internal FixedActionStatePageBank ActionStatePages { get; }
        internal Dictionary<CharacterSkillId, AbilityStateValuePageBank<AbilityStateValue>> StateValuePages { get; }
        internal Dictionary<CharacterSkillId, GameplayAbilityExecutionStateBank<AbilityStateValue>> ExecutionStatePages { get; }
        internal FixedCharacterActionRuntimeState ActionState => m_ActionState;
        internal CharacterControlRuntimeStateTransaction ControlState => m_ControlState;
        internal FixedCharacterEventSequenceState EventSequenceState => m_EventSequenceState;
        internal FixedCharacterHandleAllocatorState HandleAllocatorState => m_HandleAllocatorState;
        internal FixedCharacterGameplayEffectRuntimeState GameplayEffectState => m_GameplayEffectState;
        internal FixedCharacterEquipmentRuntimeState EquipmentState => m_EquipmentState;
        internal FixedCharacterRuntimeStateTransaction RuntimeState => m_RuntimeState;

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
            foreach (FixedCharacterEvaluationOutput output in m_RecycledEvaluationOutputs)
                output.Clear();
            m_RecycledEvaluationOutputs.Clear();
            for (int index = 0; index < m_Invocations.Length; index++)
                m_Invocations[index].DisposeStateExecutionText();
            ClearActionRuntimes();
            ClearTimelineTransfers();
            ClearTimelineMotionScratches();
            m_MotionContributions.Clear();
            m_CombatQueries.Clear();
#if UNITY_EDITOR
            CombatQueryDiagnostics?.Dispose();
#endif
            m_ActionOwnerByInstanceId.Clear();
            if (AbilityInstallations.GameplayEffectCatalog != null)
                m_GameplayEffectState.ReleaseStorage();
        }

        public CharacterBodyMotionBinding BodyMotionBinding { get; }
        public CharacterGameplayEffectRuntimeBinding GameplayEffectRuntimeBinding { get; }
        public int GameplayOutputCapacity => checked(m_PresentationCapacity * 2 +
            (AbilityInstallations.GameplayEffectCatalog == null ? 0 :
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.FactsPerTick +
                AbilityInstallations.GameplayEffectCatalog.ActorCapacity.PresentationRequestsPerTick));
        public CharacterEquipmentRuntimeBinding EquipmentRuntimeBinding { get; }
        public IAbilityTimelineRuntime TimelineRuntime { get; }
        public IAbilityTimelineLogicMotionReader TimelineMotionReader { get; }
        public FixedGameplayAbilityExecutionInstallationSet AbilityInstallations { get; }
        public StableHash StateSchemaHash { get; }
        public StableHash GameplayContentHash { get; }

        static StableHash ComputeStateSchemaHash(
            CharacterControlRuntimeBinding control,
            CharacterInputRuntimeBinding input,
            CharacterGameplayEffectRuntimeBinding gameplayEffects,
            CharacterEquipmentRuntimeBinding equipment,
            FixedGameplayAbilityExecutionInstallationSet abilities)
        {
            var parts = new List<string>
            {
                "fixed-character-state-schema/3",
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                FixedGameplayAbilityExecutionData data = abilities.Installations[i].Data;
                parts.Add(data.AbilityId.Value);
                parts.Add(data.StateSchemaHash.ToString());
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
            FixedGameplayAbilityExecutionInstallationSet abilities,
            CharacterCombatRuntimeBinding combat)
        {
            var parts = new List<string>
            {
                "fixed-simulation-actor-content/2",
                combat == null ? string.Empty : combat.ContentHash.ToString(),
                control.BindingHash.ToString(),
                input.BindingHash.ToString(),
                bodyMotion.BindingHash.ToString(),
                abilities.GameplayEffectCatalog != null ? gameplayEffects.BindingHash.ToString() : string.Empty,
                abilities.RequiresEquipment ? equipment.BindingHash.ToString() : string.Empty
            };
            for (int i = 0; i < abilities.Installations.Count; i++)
            {
                FixedGameplayAbilityExecutionData data = abilities.Installations[i].Data;
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
                : throw new ArgumentException("Fixed Timeline runtime must expose pending logic motion.", nameof(timelineRuntime));
        }



    }
}
