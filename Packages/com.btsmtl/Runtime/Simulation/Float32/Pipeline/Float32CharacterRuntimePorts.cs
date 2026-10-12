using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public static class Float32PipelineRuntimePortIds
    {
        public const string CharacterRuntime = "simulation.target.float32-character-runtime";
        public const string CharacterRuntimeSchema = "float32-character-runtime-services";
        public const string WorkingState = "simulation.target.float32-working-state";
        public const string WorkingStateSchema = "float32-working-state-read";
        public const string CompletedSteps = "simulation.target.float32-completed-steps";
        public const string CompletedStepsSchema = "float32-completed-step-read";
        public const string CommittedObservation = "simulation.target.float32-committed-actor-observation";
        public const string CommittedObservationSchema = "committed-actor-pose-observation";
        public const string WorldSolver = "simulation.solver.float32-world";
        public const string WorldSolverSchema = "float32-world-solver";
        public const string Diagnostics = "simulation.diagnostics.session";
        public const string DiagnosticsSchema = "simulation-diagnostics-sink";
    }

    public sealed class Float32CharacterRuntime
    {
        readonly ReadOnlyCollection<SimulationActorBinding> m_Roster;
        readonly ReadOnlyCollection<Float32GameplayAbilityExecutionData> m_Abilities;
        readonly ReadOnlyCollection<string> m_InputRequestIds;
        readonly Dictionary<ActorId, int> m_ActorIndices = new Dictionary<ActorId, int>();
        readonly Dictionary<ActorId, Float32CharacterControlRuntime> m_ControlRuntimes =
            new Dictionary<ActorId, Float32CharacterControlRuntime>();

        public Float32CharacterRuntime(
            IEnumerable<SimulationActorBinding> roster,
            SimulationNumericProfile numericProfile,
            int tickRate,
            OperationSetVersion operationSetVersion,
            CharacterControlModuleCatalog controlModules)
        {
            if (!numericProfile.IsValid || tickRate <= 0 || !operationSetVersion.IsValid)
                throw new ArgumentException("Float32 Character Runtime execution identity is incomplete.");
            ControlModules = controlModules ?? throw new ArgumentNullException(nameof(controlModules));
            var values = roster == null
                ? new List<SimulationActorBinding>()
                : new List<SimulationActorBinding>(roster);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Float32 Character Runtime roster cannot be empty.", nameof(roster));
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Float32 Character Runtime roster contains a null or duplicate ActorId.", nameof(roster));
            }
            m_Roster = values.AsReadOnly();
            for (int i = 0; i < m_Roster.Count; i++)
                m_ActorIndices.Add(m_Roster[i].ActorId, i);
            RosterDescriptor = new SimulationActorRosterDescriptor(ActorIds(values));
            var abilities = new Dictionary<CharacterSkillId, Float32GameplayAbilityExecutionData>();
            for (int actorIndex = 0; actorIndex < values.Count; actorIndex++)
            {
                IReadOnlyList<Float32GameplayAbilityExecutionInstallation> actorAbilities =
                    values[actorIndex].AbilityInstallations.Installations;
                for (int abilityIndex = 0; abilityIndex < actorAbilities.Count; abilityIndex++)
                {
                    Float32GameplayAbilityExecutionData ability = actorAbilities[abilityIndex].Data;
                    if (abilities.TryGetValue(ability.AbilityId, out Float32GameplayAbilityExecutionData existing))
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
            var abilityValues = new List<Float32GameplayAbilityExecutionData>(abilities.Values);
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
                Float32GameplayAbilityExecutionData ability = m_Abilities[i];
                if (ability.NumericProfile != NumericProfile || ability.TickRate != TickRate ||
                    !ability.OperationSetVersion.Equals(OperationSetVersion))
                {
                    throw new InvalidOperationException("Float32 Character Runtime Ability data uses inconsistent execution identities.");
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
                CharacterControlModuleContract contract = ControlModules.RequireContract(actor.ControlRuntimeBinding.ModuleId);
                CharacterControlStateSchema schema = contract.StateSchema;
                actor.PrepareCheckpointControlContract(contract);
                actor.RuntimeStatePages.PrepareControlValueCapacity(schema.FieldCount);
                actor.ControlState.PrepareValueCapacity(schema.FieldCount);
                m_ControlRuntimes.Add(actor.ActorId, new Float32CharacterControlRuntime(
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
                "float32-character-runtime/1",
                RosterDescriptor.RosterHash.ToString()
            };
            for (int i = 0; i < values.Count; i++)
                parts.Add(values[i].GameplayContentHash.ToString());
            GameplayContentHash = new GameplayContentHash(StableHash.Compute(parts.ToArray()));
            var stateSchemaParts = new List<string>
            {
                "float32-character-runtime-state-schema/1",
                RosterDescriptor.RosterHash.ToString()
            };
            for (int i = 0; i < values.Count; i++)
                stateSchemaParts.Add(values[i].StateSchemaHash.ToString());
            StateSchemaHash = StableHash.Compute(stateSchemaParts.ToArray());
        }

        public IReadOnlyList<SimulationActorBinding> Roster => m_Roster;
        public IReadOnlyList<Float32GameplayAbilityExecutionData> Abilities => m_Abilities;
        public SimulationActorRosterDescriptor RosterDescriptor { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public int TickRate { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public CharacterControlModuleCatalog ControlModules { get; }
        public WorldCapability RequiredWorldCapabilities { get; }
        public IReadOnlyList<string> InputRequestIds => m_InputRequestIds;
        internal Float32CharacterControlRuntime ControlRuntime(ActorId actorId) =>
            m_ControlRuntimes[actorId];
        public StableHash StateSchemaHash { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public string AbilitySetSourceRevision { get; }

        public int GetActorIndex(ActorId actorId) =>
            m_ActorIndices.TryGetValue(actorId, out int index)
                ? index
                : throw new InvalidOperationException($"Actor '{actorId}' is not part of the locked Character Runtime roster.");

        public Float32CharacterRuntimeState CreateInitialState(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= Roster.Count)
                throw new ArgumentOutOfRangeException(nameof(actorIndex));
            SimulationActorBinding actor = Roster[actorIndex];
            CharacterControlModuleContract control = ControlModules.RequireContract(actor.ControlRuntimeBinding.ModuleId);
            CharacterControlRuntimeState controlState = CharacterControlRuntimeState.CreateInitial(
                actor.ControlRuntimeBinding,
                control);
            Float32GameplayEffectRuntimeCatalog effectCatalog = actor.AbilityInstallations.GameplayEffectCatalog;
            GameplayEffectStateAggregate effectState = effectCatalog == null
                ? default
                : actor.GameplayEffectState.CreateInitial(effectCatalog, actor.EffectExecutionScratch);
            EquipmentStateAggregate equipmentState = null;
            CreateEquipmentInitialState(actor, out equipmentState);
            Float32CharacterRuntimeStatePage page = actor.RuntimeStatePages.Begin();
            page.Data.CharacterBlackboards = actor.CharacterBlackboardPages.BeginEmpty();
            for (int index = 0; index < actor.AbilityInstallations.Installations.Count; index++)
            {
                GameplayAbilityExecutionIdentity identity = actor.AbilityInstallations.Installations[index].Identity;
                page.Abilities[index] = Float32AbilityRuntimeState.Adopt(
                    actor.StateValuePages[identity.AbilityId].BeginEmpty(),
                    actor.ExecutionStatePages[identity.AbilityId].Capture());
            }
            return Float32CharacterRuntimeState.CreateInitial(
                page,
                NumericProfile,
                new GameplayContentHash(actor.GameplayContentHash),
                actor.StateSchemaHash,
                controlState,
                effectState,
                equipmentState,
                actor.ActionStatePages.Begin(),
                actor.InputRequestPages.Begin(0),
                actor.MotionWarpPages.BeginEmpty());
        }

        static void CreateEquipmentInitialState(
            SimulationActorBinding actor,
            out EquipmentStateAggregate equipmentState)
        {
            equipmentState = null;
            if (!actor.AbilityInstallations.RequiresEquipment)
                return;
            equipmentState = actor.EquipmentState.CreateInitial();
        }

        static ActorId[] ActorIds(IReadOnlyList<SimulationActorBinding> values)
        {
            var result = new ActorId[values.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = values[i].ActorId;
            return result;
        }

        static string ComputeAbilitySetSourceRevision(
            IReadOnlyList<Float32GameplayAbilityExecutionData> abilities)
        {
            var parts = new List<string> { "float32-ability-set-revision/1" };
            for (int i = 0; i < abilities.Count; i++)
            {
                Float32GameplayAbilityExecutionData ability = abilities[i];
                parts.Add(ability.AbilityId.Value);
                parts.Add(ability.SourceRevision.ToString());
                parts.Add(ability.ContentHash.ToString());
                parts.Add(ability.StateSchemaHash.ToString());
            }
            return StableHash.Compute(parts.ToArray()).ToString();
        }
    }

    public interface IFloat32CharacterRuntimePort : ISimulationRuntimePort
    {
        Float32CharacterRuntime Runtime { get; }
    }

    public sealed class Float32CharacterRuntimePort : IFloat32CharacterRuntimePort
    {
        public Float32CharacterRuntimePort(
            SimulationComponentIdentity backend,
            Float32CharacterRuntime runtime)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.CharacterRuntime,
                Float32PipelineRuntimePortIds.CharacterRuntimeSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), Runtime.GameplayContentHash.ToString()),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public Float32CharacterRuntime Runtime { get; }


    }

    public interface IFloat32WorkingStateReadPort : ISimulationRuntimePort
    {
        SimulationWorldStateSet Current { get; }
        Float32SimulationStep Step { get; }
    }

    public sealed class Float32WorkingStatePort : IFloat32WorkingStateReadPort
    {
        public Float32WorkingStatePort(SimulationComponentIdentity backend)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.WorkingState,
                Float32PipelineRuntimePortIds.WorkingStateSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), "working-state/1"),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public SimulationWorldStateSet Current { get; private set; }
        public Float32SimulationStep Step { get; private set; }

        internal void Set(SimulationWorldStateSet current, Float32SimulationStep step)
        {
            Current = current ?? throw new ArgumentNullException(nameof(current));
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
            Current = null;
            Step = null;
        }
    }

    public interface IFloat32CompletedStepReadPort : ISimulationRuntimePort
    {
        IReadOnlyList<Float32CompletedSimulationStep> Steps { get; }
    }

    public sealed class Float32CompletedStepPort : IFloat32CompletedStepReadPort
    {
        IReadOnlyList<Float32CompletedSimulationStep> m_Steps = Array.Empty<Float32CompletedSimulationStep>();

        public Float32CompletedStepPort(SimulationComponentIdentity backend)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is invalid.", nameof(backend));
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.CompletedSteps,
                Float32PipelineRuntimePortIds.CompletedStepsSchema,
                backend.ComponentId,
                StableHash.Compute(backend.ToString(), "completed-steps/1"),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public IReadOnlyList<Float32CompletedSimulationStep> Steps => m_Steps;

        internal void Set(IReadOnlyList<Float32CompletedSimulationStep> steps) =>
            m_Steps = steps ?? throw new ArgumentNullException(nameof(steps));

        internal void Clear() => m_Steps = Array.Empty<Float32CompletedSimulationStep>();
    }

    public interface IFloat32WorldSolverRuntimePort : ISimulationRuntimePort
    {
        ICharacterWorldSolver Solver { get; }
    }

    public sealed class Float32WorldSolverRuntimePort : IFloat32WorldSolverRuntimePort
    {
        public Float32WorldSolverRuntimePort(
            SimulationComponentIdentity worldSolver,
            ICharacterWorldSolver solver)
        {
            if (!worldSolver.IsValid || worldSolver.Role != SimulationComponentRole.WorldSolver)
                throw new ArgumentException("World Solver component identity is invalid.", nameof(worldSolver));
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.WorldSolver,
                Float32PipelineRuntimePortIds.WorldSolverSchema,
                worldSolver.ComponentId,
                StableHash.Compute(worldSolver.ToString(), solver.Descriptor.ImplementationId.Value, solver.Descriptor.Version),
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public ICharacterWorldSolver Solver { get; }
    }

    public interface IFloat32DiagnosticsRuntimePort : ISimulationRuntimePort
    {
        ISimulationDiagnosticsSink Sink { get; }
    }

    public sealed class Float32DiagnosticsRuntimePort : IFloat32DiagnosticsRuntimePort
    {
        public Float32DiagnosticsRuntimePort(
            SimulationComponentIdentity diagnostics,
            ISimulationDiagnosticsSink sink)
        {
            if (!diagnostics.IsValid || diagnostics.Role != SimulationComponentRole.Diagnostics)
                throw new ArgumentException("Diagnostics component identity is invalid.", nameof(diagnostics));
            Sink = sink ?? throw new ArgumentNullException(nameof(sink));
            Descriptor = Float32PipelineRuntimePortDescriptor.Create(
                Float32PipelineRuntimePortIds.Diagnostics,
                Float32PipelineRuntimePortIds.DiagnosticsSchema,
                diagnostics.ComponentId,
                diagnostics.ConfigurationHash,
                SimulationPortDirection.Input);
        }

        public SimulationPortDescriptor Descriptor { get; }
        public ISimulationDiagnosticsSink Sink { get; }
    }

    static class Float32PipelineRuntimePortDescriptor
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
