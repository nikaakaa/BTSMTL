using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class FixedPipelineRuntimePortIds
    {
        public const string CharacterRuntime = "simulation.target.fixed-character-runtime";
        public const string CharacterRuntimeSchema = "fixed-character-runtime-services";
        public const string WorkingState = "simulation.target.fixed-working-state";
        public const string WorkingStateSchema = "fixed-working-state-read";
        public const string CompletedSteps = "simulation.target.fixed-completed-steps";
        public const string CompletedStepsSchema = "fixed-completed-step-read";
        public const string CommittedObservation = "simulation.target.fixed-committed-actor-observation";
        public const string CommittedObservationSchema = "committed-actor-pose-observation";
        public const string WorldSolver = "simulation.solver.fixed-world";
        public const string WorldSolverSchema = "fixed-world-solver";
        public const string Diagnostics = "simulation.diagnostics.session";
        public const string DiagnosticsSchema = "simulation-diagnostics-sink";
        public const string Combat = "simulation.target.fixed-combat";
        public const string CombatSchema = "fixed-combat-state-and-ingress";
    }

    public interface IFixedCombatRuntimePort : ISimulationRuntimePort
    {
        FixedCombatRuntime Combat { get; }
        int MaximumCheckpointPayloadBytes { get; }
    }

    public sealed class FixedCombatRuntimePort : IFixedCombatRuntimePort
    {
        public FixedCombatRuntimePort(SimulationComponentIdentity backend, FixedCharacterRuntime characters,
            IReadOnlyList<FixedLocalEnemyBinding> localEnemies, int maximumSessionCheckpointPayloadBytes)
        {
            Combat = new FixedCombatRuntime(characters, localEnemies);
            MaximumCheckpointPayloadBytes = maximumSessionCheckpointPayloadBytes;
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(FixedPipelineRuntimePortIds.Combat,
                FixedPipelineRuntimePortIds.CombatSchema, backend.ToString(),
                characters.GameplayContentHash.Value, SimulationPortDirection.Input);
        }
        public SimulationPortDescriptor Descriptor { get; }
        public FixedCombatRuntime Combat { get; }
        public int MaximumCheckpointPayloadBytes { get; }
    }

    public sealed class FixedCharacterRuntime
    {
        readonly ReadOnlyCollection<SimulationActorBinding> m_Roster;
        readonly ReadOnlyCollection<FixedGameplayAbilityExecutionData> m_Abilities;
        readonly ReadOnlyCollection<string> m_InputRequestIds;
        readonly Dictionary<ActorId, int> m_ActorIndices = new Dictionary<ActorId, int>();
        readonly Dictionary<ActorId, FixedCharacterControlRuntime> m_ControlRuntimes =
            new Dictionary<ActorId, FixedCharacterControlRuntime>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly StableHash s_SimulatedAttackIdentity = StableHash.Compute("simulation.debug.incoming-attack");
        static readonly ActorId s_SimulatedAttacker = new ActorId("fixed-target");

        public void EnqueueSimulatedIncomingAttack(ActorId actorId, bool heavy)
        {
            m_Roster[GetActorIndex(actorId)].SimulatedIncomingAttacks |= (byte)(heavy ? 2 : 1);
        }

        internal int CopySimulatedAttackIngress(int actorIndex, FixedCharacterRuntimeState state,
            SimulationWorldStateSet world, SimulationTick tick, FixedCombatRuntime combat, SimulationIngress[] destination, int count)
        {
            SimulationActorBinding actor = m_Roster[actorIndex];
            byte attacks = actor.SimulatedIncomingAttacks;
            actor.SimulatedIncomingAttacks = 0;
            if (attacks == 0)
                return count;
            ReadOnlySpan<FixedActionInstanceState> actions = state.ActionInstances;
            for (int i = 0; i < actions.Length; i++)
            {
                FixedActionInstanceState action = actions[i];
                if (!action.IsActive)
                    continue;
                CharacterCombatRuntimeBinding binding = m_Roster[actorIndex].CombatBinding;
                GameplayAbilitySourceContext source = action.SourceContext;
                GameplayEffectScope scope = GameplayEffectScope.Action(source);
                bool parry = state.GameplayEffectState.HasTag(binding.GuardingTag, scope);
                bool evade = state.GameplayEffectState.HasTag(binding.EvadingTag, scope) &&
                    state.GameplayEffectState.HasTagFromAction(binding.InvulnerableTag, GameplayEffectScope.Actor, in source) &&
                    !state.GameplayEffectState.HasTag(binding.CounterOpportunityTag, scope);
                if (!parry && !evade)
                    break;
                for (int type = 0; type < 2; type++)
                {
                    if ((attacks & (1 << type)) == 0)
                        continue;
                    WorldBodyState sourceBody = world.WorldState.Bodies[GetActorIndex(s_SimulatedAttacker)];
                    CombatHitResult hit = actor.SimulatedIncomingAttackResults[type];
                    hit.ResultId = tick.Value * 2 + (ulong)type;
                    hit.SourceTick = tick.Value;
                    hit.SourceActor = sourceBody.ActorId;
                    hit.SourceTargetSnapshot = new SimulationActionTargetSnapshot(
                        sourceBody.ActorId.Value, sourceBody.Position, sourceBody.Yaw);
                    hit.TargetActor = actor.ActorId;
                    hit.TargetActionInstanceId = action.InstanceId;
                    hit.TargetFeedbackEvent = evade ? binding.PerfectEvadeEvent :
                        type == 0 ? binding.ParryLightEvent : binding.ParryHeavyEvent;
                    hit.Disposition = evade ? CombatHitDisposition.Evaded : CombatHitDisposition.Parried;
                    hit.Heavy = type != 0;
                    var header = new SimulationIngressHeader(NumericProfile, m_Roster[actorIndex].ActorId,
                        tick.Value, tick.Value * 2 + (ulong)type, s_SimulatedAttackIdentity,
                        SimulationIngressKind.GameplayResult);
                    destination[count++] = new SimulationIngress(header,
                        new SimulationGameplayResultIngress(hit));
                    if (parry)
                        combat.ApplySimulatedParryHalt(actorIndex, action.InstanceId, type != 0, tick.Value);
                }
                break;
            }
            return count;
        }
#endif

        public FixedCharacterRuntime(
            IEnumerable<SimulationActorBinding> roster,
            SimulationNumericProfile numericProfile,
            int tickRate,
            OperationSetVersion operationSetVersion,
            CharacterControlModuleCatalog controlModules)
        {
            if (!numericProfile.IsValid || tickRate <= 0 || !operationSetVersion.IsValid)
                throw new ArgumentException("Fixed Character Runtime execution identity is incomplete.");
            ControlModules = controlModules ?? throw new ArgumentNullException(nameof(controlModules));
            var values = roster == null
                ? new List<SimulationActorBinding>()
                : new List<SimulationActorBinding>(roster);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Fixed Character Runtime roster cannot be empty.", nameof(roster));
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Fixed Character Runtime roster contains a null or duplicate ActorId.", nameof(roster));
            }
            m_Roster = values.AsReadOnly();
            for (int i = 0; i < m_Roster.Count; i++)
                m_ActorIndices.Add(m_Roster[i].ActorId, i);
            RosterDescriptor = new SimulationActorRosterDescriptor(ActorIds(values));
            var abilities = new Dictionary<CharacterSkillId, FixedGameplayAbilityExecutionData>();
            for (int actorIndex = 0; actorIndex < values.Count; actorIndex++)
            {
                IReadOnlyList<FixedGameplayAbilityExecutionInstallation> actorAbilities =
                    values[actorIndex].AbilityInstallations.Installations;
                for (int abilityIndex = 0; abilityIndex < actorAbilities.Count; abilityIndex++)
                {
                    FixedGameplayAbilityExecutionData ability = actorAbilities[abilityIndex].Data;
                    if (abilities.TryGetValue(ability.AbilityId, out FixedGameplayAbilityExecutionData existing))
                    {
                        if (!existing.ContentHash.Equals(ability.ContentHash) ||
                            !existing.StateSchemaHash.Equals(ability.StateSchemaHash) ||
                            !existing.OperationSetVersion.Equals(ability.OperationSetVersion) ||
                            !existing.NumericProfile.Equals(ability.NumericProfile) ||
                            existing.TickRate != ability.TickRate)
                        {
                            throw new InvalidOperationException($"Ability '{ability.AbilityId}' has different execution identity across Character Runtime bindings.");
                        }
                    }
                    else
                    {
                        abilities.Add(ability.AbilityId, ability);
                    }
                }
            }
            var abilityValues = new List<FixedGameplayAbilityExecutionData>(abilities.Values);
            abilityValues.Sort((left, right) => left.AbilityId.CompareTo(right.AbilityId));
            m_Abilities = abilityValues.AsReadOnly();
            AbilitySetSourceRevision = ComputeAbilitySetSourceRevision(m_Abilities);
            NumericProfile = numericProfile;
            TickRate = tickRate;
            OperationSetVersion = operationSetVersion;
            WorldCapability requiredWorldCapabilities = WorldCapability.None;
            for (int i = 0; i < values.Count; i++)
                requiredWorldCapabilities |= values[i].BodyMotionBinding.RequiredWorldCapability;
            for (int i = 0; i < m_Abilities.Count; i++)
            {
                FixedGameplayAbilityExecutionData ability = m_Abilities[i];
                if (ability.NumericProfile != NumericProfile || ability.TickRate != TickRate ||
                    !ability.OperationSetVersion.Equals(OperationSetVersion))
                {
                    throw new InvalidOperationException("Fixed Character Runtime Ability data uses inconsistent execution identities.");
                }
            }
            RequiredWorldCapabilities = requiredWorldCapabilities;
            var requestIds = new HashSet<string>(StringComparer.Ordinal);
            for (int actorIndex = 0; actorIndex < values.Count; actorIndex++)
                for (int requestIndex = 0; requestIndex < values[actorIndex].InputRuntimeBinding.InputRequestIds.Count; requestIndex++)
                    requestIds.Add(values[actorIndex].InputRuntimeBinding.InputRequestIds[requestIndex]);
            var sortedRequestIds = new List<string>(requestIds);
            sortedRequestIds.Sort(StringComparer.Ordinal);
            m_InputRequestIds = sortedRequestIds.AsReadOnly();
            for (int i = 0; i < values.Count; i++)
            {
                SimulationActorBinding actor = values[i];
                actor.InputRequestState.Prepare(m_InputRequestIds);
                actor.InputRequestPages.PrepareRequestCapacity(m_InputRequestIds.Count);
                int controlValueCount = ControlModules.RequireContract(actor.ControlRuntimeBinding.ModuleId).StateSchema.FieldCount;
                actor.ControlState.PrepareValueCapacity(controlValueCount);
                actor.RuntimeStatePages.PrepareControlValueCapacity(controlValueCount);
                m_ControlRuntimes.Add(actor.ActorId, new FixedCharacterControlRuntime(
                    ControlModules,
                    actor.ControlRuntimeBinding,
                    actor.AbilityExecutionInput,
                    actor.ActorId,
                    TickRate,
                    actor.ControlMotion,
                    actor.CharacterTraceSink));
            }

            var parts = new List<string>
            {
                "fixed-character-runtime/1",
                RosterDescriptor.RosterHash.ToString()
            };
            for (int i = 0; i < values.Count; i++)
                parts.Add(values[i].GameplayContentHash.ToString());
            GameplayContentHash = new GameplayContentHash(StableHash.Compute(parts.ToArray()));
            var stateSchemaParts = new List<string>
            {
                "fixed-character-runtime-state-schema/1",
                RosterDescriptor.RosterHash.ToString()
            };
            for (int i = 0; i < values.Count; i++)
                stateSchemaParts.Add(values[i].StateSchemaHash.ToString());
            StateSchemaHash = StableHash.Compute(stateSchemaParts.ToArray());
        }

        public IReadOnlyList<SimulationActorBinding> Roster => m_Roster;
        public IReadOnlyList<FixedGameplayAbilityExecutionData> Abilities => m_Abilities;
        public SimulationActorRosterDescriptor RosterDescriptor { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public int TickRate { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public CharacterControlModuleCatalog ControlModules { get; }
        public WorldCapability RequiredWorldCapabilities { get; }
        public IReadOnlyList<string> InputRequestIds => m_InputRequestIds;
        internal FixedCharacterControlRuntime ControlRuntime(ActorId actorId) =>
            m_ControlRuntimes[actorId];
        public StableHash StateSchemaHash { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public string AbilitySetSourceRevision { get; }

        public int GetActorIndex(ActorId actorId) =>
            m_ActorIndices.TryGetValue(actorId, out int index)
                ? index
                : throw new InvalidOperationException($"Actor '{actorId}' is not part of the locked Character Runtime roster.");

        public ulong QueueBlackboardWrite(ActorId actor, FixedBlackboardWriteCommand command) =>
            m_Roster[GetActorIndex(actor)].GetInvocation(command.Ability.AbilityId).QueueBlackboardWrite(command);


        public bool TryTakeBlackboardWriteResult(ActorId actor, CharacterSkillId ability, ulong sequence,
            out FixedBlackboardWriteResult result) =>
            m_Roster[GetActorIndex(actor)].GetInvocation(ability).TryTakeBlackboardResult(sequence, out result);

        public void ClearBlackboardWrites(ActorId actor)
        {
            SimulationActorBinding binding = m_Roster[GetActorIndex(actor)];
            for (int i = 0; i < binding.AbilityInstallations.Installations.Count; i++)
                binding.GetInvocation(binding.AbilityInstallations.Installations[i].Data.AbilityId).ClearBlackboardWrites();
        }

        public FixedCharacterRuntimeState CreateInitialState(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= Roster.Count)
                throw new ArgumentOutOfRangeException(nameof(actorIndex));
            SimulationActorBinding actor = Roster[actorIndex];
            CharacterControlModuleContract control = ControlModules.RequireContract(actor.ControlRuntimeBinding.ModuleId);
            CharacterControlRuntimeState controlState = CharacterControlRuntimeState.CreateInitial(
                actor.ControlRuntimeBinding,
                control);
            FixedGameplayEffectRuntimeCatalog effectCatalog = actor.AbilityInstallations.GameplayEffectCatalog;
            GameplayEffectStateAggregate effectState = effectCatalog == null
                ? default
                : actor.GameplayEffectState.CreateInitial(effectCatalog, actor.EffectExecutionScratch);
            EquipmentStateAggregate equipmentState = null;
            if (actor.AbilityInstallations.RequiresEquipment)
            {
                equipmentState = actor.EquipmentState.CreateInitial();
            }
            FixedAbilityStateDirectoryReference abilityDirectory = actor.AbilityDirectories.Begin();
            FixedAbilityRuntimeState[] abilities = abilityDirectory.Page.Values;
            for (int i = 0; i < abilities.Length; i++)
            {
                GameplayAbilityExecutionIdentity identity = actor.AbilityInstallations.Installations[i].Identity;
                abilities[i] = FixedAbilityRuntimeState.Adopt(
                    actor.StateValuePages[identity.AbilityId].BeginEmpty(),
                    actor.ExecutionStatePages[identity.AbilityId].Capture());
            }
            return FixedCharacterRuntimeState.CreateInitial(
                actor.RuntimeStatePages,
                actor.CharacterBlackboardPages.BeginEmpty(),
                abilityDirectory,
                actor.ActionStatePages.Begin(),
                actor.InputRequestPages.Begin(0),
                NumericProfile,
                new GameplayContentHash(actor.GameplayContentHash),
                actor.StateSchemaHash,
                controlState,
                effectState,
                equipmentState,
                actor.TimelineSnapshotPages.Begin(),
                actor.MotionWarpPages.BeginEmpty());
        }

        static ActorId[] ActorIds(IReadOnlyList<SimulationActorBinding> values)
        {
            var result = new ActorId[values.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = values[i].ActorId;
            return result;
        }

        static string ComputeAbilitySetSourceRevision(
            IReadOnlyList<FixedGameplayAbilityExecutionData> abilities)
        {
            var parts = new List<string> { "fixed-ability-set-revision/1" };
            for (int i = 0; i < abilities.Count; i++)
            {
                FixedGameplayAbilityExecutionData ability = abilities[i];
                parts.Add(ability.AbilityId.Value);
                parts.Add(ability.SourceRevision.ToString());
                parts.Add(ability.ContentHash.ToString());
                parts.Add(ability.StateSchemaHash.ToString());
            }
            return StableHash.Compute(parts.ToArray()).ToString();
        }
    }

    public interface IFixedCharacterRuntimePort : ISimulationRuntimePort
    {
        FixedCharacterRuntime Runtime { get; }
    }

    public sealed class FixedCharacterRuntimePort : IFixedCharacterRuntimePort
    {
        public FixedCharacterRuntimePort(
            SimulationComponentIdentity backend,
            FixedCharacterRuntime runtime)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.CharacterRuntime,
                FixedPipelineRuntimePortIds.CharacterRuntimeSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), Runtime.GameplayContentHash.ToString()),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public FixedCharacterRuntime Runtime { get; }


    }

    public interface IFixedWorkingStateReadPort : ISimulationRuntimePort
    {
        SimulationWorldStateSet Current { get; }
        FixedSimulationStep Step { get; }
    }

    public sealed class FixedWorkingStatePort : IFixedWorkingStateReadPort
    {
        public FixedWorkingStatePort(SimulationComponentIdentity backend)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.WorkingState,
                FixedPipelineRuntimePortIds.WorkingStateSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), "working-state/1"),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public SimulationWorldStateSet Current { get; private set; }
        public FixedSimulationStep Step { get; private set; }

        internal void Set(SimulationWorldStateSet current, FixedSimulationStep step)
        {
            Current = current;
            Step = step ?? throw new ArgumentNullException(nameof(step));
            if (current.Actors.Count != step.Actors.Count)
                throw new InvalidOperationException("Working state and Step rosters do not match.");
            for (int i = 0; i < current.Actors.Count; i++)
            {
                if (!current.Actors[i].ActorId.Equals(step.Actors[i]))
                    throw new InvalidOperationException("Working state and Step Actor order do not match.");
            }
        }

        internal void Clear()
        {
            Current = default;
            Step = null;
        }
    }

    public interface IFixedCompletedStepReadPort : ISimulationRuntimePort
    {
        IReadOnlyList<FixedCompletedSimulationStep> Steps { get; }
    }

    public sealed class FixedCompletedStepPort : IFixedCompletedStepReadPort
    {
        IReadOnlyList<FixedCompletedSimulationStep> m_Steps = Array.Empty<FixedCompletedSimulationStep>();

        public FixedCompletedStepPort(SimulationComponentIdentity backend)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.CompletedSteps,
                FixedPipelineRuntimePortIds.CompletedStepsSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), "completed-steps/1"),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public IReadOnlyList<FixedCompletedSimulationStep> Steps => m_Steps;

        internal void Set(IReadOnlyList<FixedCompletedSimulationStep> steps) =>
            m_Steps = steps ?? throw new ArgumentNullException(nameof(steps));

        internal void Clear() => m_Steps = Array.Empty<FixedCompletedSimulationStep>();
    }

    public interface IFixedWorldSolverRuntimePort : ISimulationRuntimePort
    {
        ICharacterWorldSolver Solver { get; }
    }

    public sealed class FixedWorldSolverRuntimePort : IFixedWorldSolverRuntimePort
    {
        public FixedWorldSolverRuntimePort(
            SimulationComponentIdentity worldSolver,
            ICharacterWorldSolver solver)
        {
            if (!worldSolver.IsValid || worldSolver.Role != SimulationComponentRole.WorldSolver)
                throw new ArgumentException("World Solver component identity is invalid.", nameof(worldSolver));
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.WorldSolver,
                FixedPipelineRuntimePortIds.WorldSolverSchema,
                worldSolver.ComponentId,
                StableHash.Compute(worldSolver.ToString(), solver.Descriptor.ImplementationId.Value, solver.Descriptor.Version),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public ICharacterWorldSolver Solver { get; }
    }

    public interface IFixedDiagnosticsRuntimePort : ISimulationRuntimePort
    {
        ISimulationDiagnosticsSink Sink { get; }
    }

    public sealed class FixedDiagnosticsRuntimePort : IFixedDiagnosticsRuntimePort
    {
        public FixedDiagnosticsRuntimePort(
            SimulationComponentIdentity diagnostics,
            ISimulationDiagnosticsSink sink)
        {
            if (!diagnostics.IsValid || diagnostics.Role != SimulationComponentRole.Diagnostics)
                throw new ArgumentException("Diagnostics component identity is invalid.", nameof(diagnostics));
            Sink = sink ?? throw new ArgumentNullException(nameof(sink));
            Descriptor = FixedPipelineRuntimePortDescriptor.Create(
                FixedPipelineRuntimePortIds.Diagnostics,
                FixedPipelineRuntimePortIds.DiagnosticsSchema,
                diagnostics.ComponentId,
                diagnostics.ConfigurationHash,
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public ISimulationDiagnosticsSink Sink { get; }
    }

    static class FixedPipelineRuntimePortDescriptor
    {
        public static SimulationPortDescriptor Create(
            string portId,
            string schemaId,
            string owner,
            StableHash configurationHash,
            SimulationPortDirection direction) =>
            new SimulationPortDescriptor(portId, schemaId, 1, direction, owner, configurationHash);
    }
}
