using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    static class SimulationActorLookup
    {
        public static bool ContainsSorted(IReadOnlyList<ActorId> actors, ActorId value)
        {
            int lower = 0;
            int upper = actors.Count - 1;
            while (lower <= upper)
            {
                int middle = lower + ((upper - lower) >> 1);
                int comparison = actors[middle].CompareTo(value);
                if (comparison == 0)
                    return true;
                if (comparison < 0)
                    lower = middle + 1;
                else
                    upper = middle - 1;
            }
            return false;
        }
    }

    public enum SimulationSessionExecutionPlanStatus : byte
    {
        Pending = 1,
        Executable = 2,
        NoStep = 3
    }

    public enum SimulationPipelineStepExecutionKind : byte
    {
        Forward = 1,
        Replay = 2,
        Current = 3,
        Authoritative = 4
    }

    [Flags]
    public enum SimulationSessionPlanRequirement : byte
    {
        None = 0,
        WorkingState = 1 << 0,
        Snapshot = 1 << 1,
        OutputDisposition = 1 << 2,
        StateHash = 1 << 3
    }

    public sealed class SimulationRestoreDirective
    {
        readonly SimulationSessionCheckpoint m_Checkpoint;
        readonly string m_SnapshotId;
        readonly SimulationTick m_Tick;
        readonly GameplayContentHash m_GameplayContentHash;
        readonly SimulationPipelineHash m_PipelineHash;
        readonly string m_BackendId;
        readonly string m_BackendSemanticVersion;
        readonly StableHash m_SnapshotHash;

        internal SimulationRestoreDirective(SimulationSessionCheckpoint checkpoint)
        {
            m_Checkpoint = checkpoint;
        }

        public SimulationRestoreDirective(
            string snapshotId,
            SimulationTick tick,
            GameplayContentHash gameplayContentHash,
            SimulationPipelineHash pipelineHash,
            string backendId,
            string backendSemanticVersion,
            StableHash snapshotHash)
        {
            if (!tick.IsValid || !gameplayContentHash.IsValid || !pipelineHash.IsValid || !snapshotHash.IsValid)
                throw new ArgumentException("Restore directive identity is incomplete.");
            m_SnapshotId = SimulationIdentity.Require(snapshotId, nameof(snapshotId));
            m_Tick = tick;
            m_GameplayContentHash = gameplayContentHash;
            m_PipelineHash = pipelineHash;
            m_BackendId = SimulationIdentity.Require(backendId, nameof(backendId));
            m_BackendSemanticVersion = SimulationIdentity.Require(backendSemanticVersion, nameof(backendSemanticVersion));
            m_SnapshotHash = snapshotHash;
        }

        public string SnapshotId => m_Checkpoint != null ? m_Checkpoint.SnapshotId : m_SnapshotId;
        public SimulationTick Tick => m_Checkpoint != null ? m_Checkpoint.Tick : m_Tick;
        public GameplayContentHash GameplayContentHash => m_Checkpoint != null ? m_Checkpoint.GameplayContentHash : m_GameplayContentHash;
        public SimulationPipelineHash PipelineHash => m_Checkpoint != null ? m_Checkpoint.PipelineHash : m_PipelineHash;
        public string BackendId => m_Checkpoint != null ? m_Checkpoint.BackendId : m_BackendId;
        public string BackendSemanticVersion => m_Checkpoint != null ? m_Checkpoint.BackendSemanticVersion : m_BackendSemanticVersion;
        public StableHash SnapshotHash => m_Checkpoint != null ? m_Checkpoint.SnapshotHash : m_SnapshotHash;
    }

    public readonly struct SimulationPipelineStepProvenance
    {
        public SimulationPipelineStepProvenance(
            SimulationPipelineStepExecutionKind executionKind,
            SimulationTickSourceIdentity source,
            ulong planSequence,
            CanonicalTextValue baselineIdentity = default)
        {
            if ((byte)executionKind < (byte)SimulationPipelineStepExecutionKind.Forward ||
                (byte)executionKind > (byte)SimulationPipelineStepExecutionKind.Authoritative ||
                CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 || planSequence == 0)
            {
                throw new ArgumentException("Pipeline Step provenance is incomplete.");
            }
            ExecutionKind = executionKind;
            Source = source;
            PlanSequence = planSequence;
            BaselineIdentity = baselineIdentity.IsNull ? string.Empty : baselineIdentity;
        }

        public SimulationPipelineStepExecutionKind ExecutionKind { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong PlanSequence { get; }
        public CanonicalTextValue BaselineIdentity { get; }
        public void Retain() { Source.Retain(); BaselineIdentity.Retain(); }
        public void Release() { BaselineIdentity.Release(); Source.Release(); }
    }

    public readonly struct SimulationPipelineStepSourceMapping
    {
        public SimulationPipelineStepSourceMapping(
            CanonicalTextValue stepClockId,
            CanonicalTextValue outerClockId,
            SimulationTickSourceKind sourceKind)
        {
            if ((byte)sourceKind < (byte)SimulationTickSourceKind.LocalLogic ||
                (byte)sourceKind > (byte)SimulationTickSourceKind.Replay)
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
            StepClockId = SimulationIdentity.Require(stepClockId, nameof(stepClockId));
            OuterClockId = SimulationIdentity.Require(outerClockId, nameof(outerClockId));
            SourceKind = sourceKind;
        }

        public CanonicalTextValue StepClockId { get; }
        public CanonicalTextValue OuterClockId { get; }
        public SimulationTickSourceKind SourceKind { get; }
        public void Retain() { StepClockId.Retain(); OuterClockId.Retain(); }
        public void Release() { OuterClockId.Release(); StepClockId.Release(); }
    }

    public abstract class SimulationPipelineStep
    {
        IReadOnlyList<ActorId> m_Actors;
        IReadOnlyList<ActorId> m_PendingActors;
        bool m_ActorsDirty;

        protected SimulationPipelineStep(IReadOnlyList<ActorId> actors)
        {
            m_Actors = actors;
        }

        protected SimulationPipelineStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IEnumerable<ActorId> actors)
            : this(
                tick,
                provenance,
                actors == null
                    ? new List<ActorId>()
                    : new List<ActorId>(actors))
        {
        }

        internal SimulationPipelineStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            List<ActorId> actors)
        {
            if (!tick.IsValid || CanonicalTextValue.IsNullOrEmpty(provenance.Source.ClockId) || provenance.Source.SourceTick == 0 ||
                (byte)provenance.ExecutionKind < (byte)SimulationPipelineStepExecutionKind.Forward ||
                (byte)provenance.ExecutionKind > (byte)SimulationPipelineStepExecutionKind.Authoritative)
            {
                throw new ArgumentException("Pipeline Step identity is incomplete.");
            }
            List<ActorId> values = actors ?? new List<ActorId>();
            values.Sort();
            if (values.Count == 0)
                throw new ArgumentException("Pipeline Step must contain at least one Actor.", nameof(actors));
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].IsValid || i > 0 && values[i - 1].Equals(values[i]))
                    throw new ArgumentException("Pipeline Step contains an invalid or duplicate ActorId.", nameof(actors));
            }
            Tick = tick;
            Provenance = provenance;
            m_Actors = values;
        }

        internal SimulationPipelineStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            ActorId[] actors)
        {
            if (!tick.IsValid || CanonicalTextValue.IsNullOrEmpty(provenance.Source.ClockId) || provenance.Source.SourceTick == 0 ||
                (byte)provenance.ExecutionKind < (byte)SimulationPipelineStepExecutionKind.Forward ||
                (byte)provenance.ExecutionKind > (byte)SimulationPipelineStepExecutionKind.Authoritative)
            {
                throw new ArgumentException("Pipeline Step identity is incomplete.");
            }
            ActorId[] values = actors ?? Array.Empty<ActorId>();
            Array.Sort(values);
            if (values.Length == 0)
                throw new ArgumentException("Pipeline Step must contain at least one Actor.", nameof(actors));
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].IsValid || i > 0 && values[i - 1].Equals(values[i]))
                    throw new ArgumentException("Pipeline Step contains an invalid or duplicate ActorId.", nameof(actors));
            }
            Tick = tick;
            Provenance = provenance;
            m_Actors = values;
        }

        public SimulationTick Tick { get; protected set; }
        public SimulationPipelineStepProvenance Provenance { get; protected set; }
        public SimulationTickSourceIdentity Source => Provenance.Source;
        public SimulationPipelineStepExecutionKind ExecutionKind => Provenance.ExecutionKind;
        public IReadOnlyList<ActorId> Actors => m_Actors;

        public void SetActors(IReadOnlyList<ActorId> actors)
        {
            m_PendingActors = actors;
            m_ActorsDirty = true;
        }

        protected void ApplyActorChanges()
        {
            if (m_ActorsDirty)
            {
                m_Actors = m_PendingActors;
                m_PendingActors = null;
                m_ActorsDirty = false;
            }
        }
    }

    public readonly struct SimulationPipelineActorInput<TInput>
    {
        public SimulationPipelineActorInput(ActorId actorId, ulong sequence, TInput value)
        {
            if (!actorId.IsValid || sequence == 0)
                throw new ArgumentException("Pipeline Actor input identity is incomplete.");
            ActorId = actorId;
            Sequence = sequence;
            Value = value;
        }

        public ActorId ActorId { get; }
        public ulong Sequence { get; }
        public TInput Value { get; }
    }

    public readonly struct SimulationPipelineTypedIngress<TIngress>
    {
        static readonly Comparison<SimulationPipelineTypedIngress<TIngress>> s_IdentityComparison = CompareIdentity;

        public SimulationPipelineTypedIngress(
            ActorId actorId,
            SimulationTickSourceIdentity source,
            ulong sequence,
            string factIdentity,
            TIngress value)
        {
            if (!actorId.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 || sequence == 0)
                throw new ArgumentException("Typed ingress identity is incomplete.");
            ActorId = actorId;
            Source = source;
            Sequence = sequence;
            FactIdentity = SimulationIdentity.Require(factIdentity, nameof(factIdentity));
            Value = value;
        }

        public ActorId ActorId { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong Sequence { get; }
        public string FactIdentity { get; }
        public TIngress Value { get; }

        public static void SortByIdentity(SimulationPipelineTypedIngress<TIngress>[] values)
        {
            Array.Sort(values, s_IdentityComparison);
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i - 1].ActorId.Equals(values[i].ActorId) &&
                    values[i - 1].Source.Equals(values[i].Source) &&
                    values[i - 1].Sequence == values[i].Sequence &&
                    string.Equals(values[i - 1].FactIdentity, values[i].FactIdentity, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Pipeline Step contains duplicate typed ingress.", nameof(values));
                }
            }
        }

        static int CompareIdentity(
            SimulationPipelineTypedIngress<TIngress> left,
            SimulationPipelineTypedIngress<TIngress> right)
        {
            int actor = left.ActorId.CompareTo(right.ActorId);
            if (actor != 0)
                return actor;
            int sourceTick = left.Source.SourceTick.CompareTo(right.Source.SourceTick);
            if (sourceTick != 0)
                return sourceTick;
            int sequence = left.Sequence.CompareTo(right.Sequence);
            return sequence != 0 ? sequence : string.CompareOrdinal(left.FactIdentity, right.FactIdentity);
        }
    }

    public class TargetSimulationPipelineStep<TInput, TIngress> : SimulationPipelineStep
    {
        static readonly Comparison<SimulationPipelineActorInput<TInput>> s_InputComparison = CompareInputs;
        IReadOnlyList<SimulationPipelineActorInput<TInput>> m_Inputs;
        IReadOnlyList<SimulationPipelineTypedIngress<TIngress>> m_Ingress;

        protected TargetSimulationPipelineStep(IReadOnlyList<ActorId> actors)
            : base(actors)
        {
            m_Inputs = Array.Empty<SimulationPipelineActorInput<TInput>>();
            m_Ingress = Array.Empty<SimulationPipelineTypedIngress<TIngress>>();
        }

        public TargetSimulationPipelineStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IEnumerable<SimulationPipelineActorInput<TInput>> inputs,
            IEnumerable<SimulationPipelineTypedIngress<TIngress>> ingress)
            : this(tick, provenance, MaterializeInputs(inputs), null, MaterializeIngress(ingress))
        {
        }

        protected TargetSimulationPipelineStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<TInput>[] inputValues,
            ActorId[] ownedActors,
            SimulationPipelineTypedIngress<TIngress>[] ingress)
            : base(tick, provenance, ownedActors == null ? CollectActors(inputValues) : FillActors(inputValues, ownedActors))
        {
            Array.Sort(inputValues, s_InputComparison);
            for (int i = 1; i < inputValues.Length; i++)
            {
                if (inputValues[i - 1].ActorId.Equals(inputValues[i].ActorId))
                    throw new ArgumentException("Pipeline Step contains duplicate Actor input.", nameof(inputValues));
            }
            m_Inputs = inputValues;
            if (ingress == null || ingress.Length == 0)
            {
                m_Ingress = Array.Empty<SimulationPipelineTypedIngress<TIngress>>();
                return;
            }
            SimulationPipelineTypedIngress<TIngress>.SortByIdentity(ingress);
            RequireIngressActors(ingress);
            m_Ingress = ingress;
        }

        public IReadOnlyList<SimulationPipelineActorInput<TInput>> Inputs => m_Inputs;
        public IReadOnlyList<SimulationPipelineTypedIngress<TIngress>> Ingress => m_Ingress;

        protected void ResetOrderedInputs(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IReadOnlyList<SimulationPipelineActorInput<TInput>> inputs,
            IReadOnlyList<SimulationPipelineTypedIngress<TIngress>> ingress)
        {
            ApplyActorChanges();
            RequireIngressActors(ingress);
            Tick = tick;
            Provenance = provenance;
            m_Inputs = inputs;
            m_Ingress = ingress;
        }

        void RequireIngressActors(IReadOnlyList<SimulationPipelineTypedIngress<TIngress>> ingress)
        {
            for (int i = 0; i < ingress.Count; i++)
            {
                if (!SimulationActorLookup.ContainsSorted(Actors, ingress[i].ActorId))
                    throw new ArgumentException($"Typed ingress targets Actor '{ingress[i].ActorId}' without Step input.", nameof(ingress));
            }
        }

        static SimulationPipelineActorInput<TInput>[] MaterializeInputs(
            IEnumerable<SimulationPipelineActorInput<TInput>> inputs)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            if (inputs is SimulationPipelineActorInput<TInput>[] source)
            {
                var values = (SimulationPipelineActorInput<TInput>[])source.Clone();
                return values;
            }
            if (inputs is List<SimulationPipelineActorInput<TInput>> sourceList)
                return sourceList.ToArray();
            return new List<SimulationPipelineActorInput<TInput>>(inputs).ToArray();
        }

        static ActorId[] FillActors(
            SimulationPipelineActorInput<TInput>[] inputs,
            ActorId[] actors)
        {
            if (actors.Length != inputs.Length)
                throw new ArgumentException("Pipeline Step actor workspace length does not match input count.", nameof(actors));
            for (int i = 0; i < inputs.Length; i++)
                actors[i] = inputs[i].ActorId;
            return actors;
        }

        static int CompareInputs(
            SimulationPipelineActorInput<TInput> left,
            SimulationPipelineActorInput<TInput> right)
        {
            return left.ActorId.CompareTo(right.ActorId);
        }

        static SimulationPipelineTypedIngress<TIngress>[] MaterializeIngress(
            IEnumerable<SimulationPipelineTypedIngress<TIngress>> ingress)
        {
            if (ingress == null)
                return Array.Empty<SimulationPipelineTypedIngress<TIngress>>();
            if (ingress is SimulationPipelineTypedIngress<TIngress>[] source)
                return (SimulationPipelineTypedIngress<TIngress>[])source.Clone();
            if (ingress is List<SimulationPipelineTypedIngress<TIngress>> sourceList)
                return sourceList.ToArray();
            return new List<SimulationPipelineTypedIngress<TIngress>>(ingress).ToArray();
        }

        static ActorId[] CollectActors(IReadOnlyList<SimulationPipelineActorInput<TInput>> inputs)
        {
            var actors = new ActorId[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
                actors[i] = inputs[i].ActorId;
            return actors;
        }
    }

    public readonly struct SimulationSessionExecutionPlan<TStep> where TStep : SimulationPipelineStep
    {
        static readonly Comparison<SimulationPipelineStepSourceMapping> s_SourceMappingComparison = CompareSourceMappings;
        readonly IReadOnlyList<TStep> m_Steps;
        readonly IReadOnlyList<SimulationPipelineStepSourceMapping> m_SourceMappings;

        public SimulationSessionExecutionPlan(
            SimulationSessionExecutionPlanStatus status,
            SimulationTickSourceIdentity outerSource,
            GameplayContentHash gameplayContentHash,
            SimulationPipelineHash pipelineHash,
            SimulationActorRosterDescriptor roster,
            IReadOnlyList<SimulationPipelineStepSourceMapping> sourceMappings,
            SimulationRestoreDirective restore,
            IReadOnlyList<TStep> steps,
            SimulationSessionPlanRequirement requirements)
            : this(
                status,
                outerSource,
                gameplayContentHash,
                pipelineHash,
                roster,
                CopyMappings(sourceMappings),
                restore,
                CopySteps(steps),
                requirements,
                true)
        {
        }

        SimulationSessionExecutionPlan(
            SimulationSessionExecutionPlanStatus status,
            SimulationTickSourceIdentity outerSource,
            GameplayContentHash gameplayContentHash,
            SimulationPipelineHash pipelineHash,
            SimulationActorRosterDescriptor roster,
            SimulationPipelineStepSourceMapping[] sourceMappings,
            SimulationRestoreDirective restore,
            IReadOnlyList<TStep> steps,
            SimulationSessionPlanRequirement requirements,
            bool _)
        {
            if ((byte)status < (byte)SimulationSessionExecutionPlanStatus.Pending ||
                (byte)status > (byte)SimulationSessionExecutionPlanStatus.NoStep ||
                CanonicalTextValue.IsNullOrEmpty(outerSource.ClockId) || outerSource.SourceTick == 0 ||
                !gameplayContentHash.IsValid || !pipelineHash.IsValid || roster == null)
            {
                throw new ArgumentException("Session ExecutionPlan identity is incomplete.");
            }
            m_SourceMappings = FreezeMappings(sourceMappings, outerSource.ClockId);
            if (status == SimulationSessionExecutionPlanStatus.Pending)
            {
                if (restore != null || steps.Count != 0 || requirements != SimulationSessionPlanRequirement.None)
                    throw new ArgumentException("Pending ExecutionPlan cannot contain restore, steps or execution requirements.");
            }
            else if (status == SimulationSessionExecutionPlanStatus.NoStep)
            {
                const SimulationSessionPlanRequirement required =
                    SimulationSessionPlanRequirement.WorkingState |
                    SimulationSessionPlanRequirement.OutputDisposition;
                if (steps.Count != 0 || (requirements & required) != required)
                    throw new ArgumentException("NoStep ExecutionPlan requires working state and output disposition without ordered Steps.");
            }
            else
            {
                const SimulationSessionPlanRequirement required =
                    SimulationSessionPlanRequirement.WorkingState |
                    SimulationSessionPlanRequirement.OutputDisposition;
                if (steps.Count == 0 || (requirements & required) != required)
                    throw new ArgumentException("Executable ExecutionPlan requires ordered Steps, working state and output disposition.");
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i] == null || i > 0 && steps[i - 1].Tick.CompareTo(steps[i].Tick) >= 0)
                        throw new ArgumentException("ExecutionPlan Steps must have strictly increasing SimulationTick values.", nameof(steps));
                    RequireSourceMapping(steps[i], m_SourceMappings);
                    RequireRosterBinding(steps[i], roster);
                    if (i > 0 && steps[i - 1].Provenance.PlanSequence >= steps[i].Provenance.PlanSequence)
                        throw new ArgumentException("ExecutionPlan Step provenance sequence must be strictly increasing.", nameof(steps));
                }
            }
            if (steps.Count > 0 && restore != null && restore.Tick.CompareTo(steps[0].Tick) >= 0)
                throw new ArgumentException("Restore Tick must precede the first ExecutionPlan Step.", nameof(restore));
            Status = status;
            OuterSource = outerSource;
            GameplayContentHash = gameplayContentHash;
            PipelineHash = pipelineHash;
            RosterHash = roster.RosterHash;
            Restore = restore;
            Requirements = requirements;
            m_Steps = steps;
        }

        public static SimulationSessionExecutionPlan<TStep> FromOwnedCollections(
            SimulationSessionExecutionPlanStatus status,
            SimulationTickSourceIdentity outerSource,
            GameplayContentHash gameplayContentHash,
            SimulationPipelineHash pipelineHash,
            SimulationActorRosterDescriptor roster,
            SimulationPipelineStepSourceMapping[] sourceMappings,
            SimulationRestoreDirective restore,
            IReadOnlyList<TStep> steps,
            SimulationSessionPlanRequirement requirements) =>
            new SimulationSessionExecutionPlan<TStep>(
                status,
                outerSource,
                gameplayContentHash,
                pipelineHash,
                roster,
                sourceMappings ?? throw new ArgumentNullException(nameof(sourceMappings)),
                restore,
                steps ?? throw new ArgumentNullException(nameof(steps)),
                requirements,
                true);

        public SimulationSessionExecutionPlanStatus Status { get; }
        public SimulationTickSourceIdentity OuterSource { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public SimulationPipelineHash PipelineHash { get; }
        public StableHash RosterHash { get; }
        public IReadOnlyList<SimulationPipelineStepSourceMapping> SourceMappings => m_SourceMappings;
        public SimulationRestoreDirective Restore { get; }
        public SimulationSessionPlanRequirement Requirements { get; }
        public IReadOnlyList<TStep> Steps => m_Steps;
        public bool IsValid => m_Steps != null;

        static TStep[] CopySteps(IReadOnlyList<TStep> steps)
        {
            if (steps == null || steps.Count == 0)
                return Array.Empty<TStep>();
            var values = new TStep[steps.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = steps[i];
            return values;
        }

        static SimulationPipelineStepSourceMapping[] CopyMappings(
            IReadOnlyList<SimulationPipelineStepSourceMapping> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<SimulationPipelineStepSourceMapping>();
            var values = new SimulationPipelineStepSourceMapping[source.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = source[i];
            return values;
        }

        static SimulationPipelineStepSourceMapping[] FreezeMappings(
            SimulationPipelineStepSourceMapping[] sourceMappings,
            CanonicalTextValue outerClockId)
        {
            Array.Sort(sourceMappings, s_SourceMappingComparison);
            for (int i = 0; i < sourceMappings.Length; i++)
            {
                if (!CanonicalTextValue.Equals(sourceMappings[i].OuterClockId, outerClockId, StringComparison.Ordinal) ||
                    i > 0 && CanonicalTextValue.Equals(sourceMappings[i - 1].StepClockId, sourceMappings[i].StepClockId, StringComparison.Ordinal) &&
                    sourceMappings[i - 1].SourceKind == sourceMappings[i].SourceKind)
                {
                    throw new ArgumentException(
                        "ExecutionPlan contains an invalid or duplicate source clock mapping.",
                        nameof(sourceMappings));
                }
            }
            return sourceMappings;
        }

        static int CompareSourceMappings(SimulationPipelineStepSourceMapping left, SimulationPipelineStepSourceMapping right)
        {
            int clock = CanonicalTextValue.CompareOrdinal(left.StepClockId, right.StepClockId);
            return clock != 0 ? clock : ((byte)left.SourceKind).CompareTo((byte)right.SourceKind);
        }

        static void RequireSourceMapping(TStep step, IReadOnlyList<SimulationPipelineStepSourceMapping> mappings)
        {
            for (int i = 0; i < mappings.Count; i++)
            {
                if (CanonicalTextValue.Equals(mappings[i].StepClockId, step.Source.ClockId, StringComparison.Ordinal) &&
                    mappings[i].SourceKind == step.Source.Kind)
                    return;
            }
            throw new ArgumentException($"ExecutionPlan Step source '{step.Source.Kind}:{step.Source.ClockId}' has no explicit outer clock mapping.");
        }

        static void RequireRosterBinding(TStep step, SimulationActorRosterDescriptor roster)
        {
            for (int i = 0; i < step.Actors.Count; i++)
            {
                if (!SimulationActorLookup.ContainsSorted(roster.Actors, step.Actors[i]))
                    throw new ArgumentException($"ExecutionPlan Step targets unknown Actor '{step.Actors[i]}'.");
            }
        }
    }
}
