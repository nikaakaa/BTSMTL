using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationPipelineProductId : IEquatable<SimulationPipelineProductId>, IComparable<SimulationPipelineProductId>
    {
        public SimulationPipelineProductId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(SimulationPipelineProductId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(SimulationPipelineProductId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationPipelineProductId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationPipelineProductSchemaVersion : IEquatable<SimulationPipelineProductSchemaVersion>
    {
        public SimulationPipelineProductSchemaVersion(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(SimulationPipelineProductSchemaVersion other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SimulationPipelineProductSchemaVersion other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    public enum SimulationPipelineProductMultiplicity : byte
    {
        Exclusive = 1,
        AppendOnly = 2
    }

    [Flags]
    public enum SimulationPipelineProvenanceFields : byte
    {
        None = 0,
        ActorId = 1 << 0,
        Tick = 1 << 1,
        Sequence = 1 << 2,
        Source = 1 << 3,
        All = ActorId | Tick | Sequence | Source
    }

    public enum SimulationPipelineAppendOrdering : byte
    {
        ActorTickSequenceSource = 1,
        TickSequenceSourceActor = 2
    }

    [Flags]
    public enum SimulationPipelinePhaseMask : byte
    {
        None = 0,
        Ingress = 1 << 0,
        Schedule = 1 << 1,
        Step = 1 << 2,
        Egress = 1 << 3,
        All = Ingress | Schedule | Step | Egress
    }

    public enum SimulationPipelineProductConsumption : byte
    {
        InternalRequired = 1,
        BackendTerminal = 2,
        CommitTerminal = 3
    }

    public sealed class SimulationPipelineProductContract : IEquatable<SimulationPipelineProductContract>
    {
        public SimulationPipelineProductContract(
            SimulationPipelineProductId productId,
            SimulationPipelineProductSchemaVersion schemaVersion,
            string owner,
            SimulationPipelineProductMultiplicity multiplicity,
            string canonicalIdentity,
            string diagnosticsShape,
            SimulationPipelinePhaseMask producerPhases,
            SimulationPipelinePhaseMask consumerPhases,
            SimulationPipelineProductConsumption consumption,
            SimulationPipelineProvenanceFields provenanceFields = SimulationPipelineProvenanceFields.None,
            SimulationPipelineAppendOrdering appendOrdering = default)
        {
            if (!productId.IsValid || !schemaVersion.IsValid ||
                !Enum.IsDefined(typeof(SimulationPipelineProductMultiplicity), multiplicity) ||
                producerPhases == SimulationPipelinePhaseMask.None || consumerPhases == SimulationPipelinePhaseMask.None ||
                !Enum.IsDefined(typeof(SimulationPipelineProductConsumption), consumption))
            {
                throw new ArgumentException("Pipeline Product contract identity is incomplete.");
            }
            if (multiplicity == SimulationPipelineProductMultiplicity.AppendOnly)
            {
                if (provenanceFields != SimulationPipelineProvenanceFields.All ||
                    !Enum.IsDefined(typeof(SimulationPipelineAppendOrdering), appendOrdering))
                {
                    throw new ArgumentException("Append-only Product requires ActorId, Tick, sequence, source provenance and stable ordering.");
                }
            }
            else if (provenanceFields != SimulationPipelineProvenanceFields.None || appendOrdering != default)
            {
                throw new ArgumentException("Exclusive Product cannot declare append-only provenance.");
            }
            ProductId = productId;
            SchemaVersion = schemaVersion;
            Owner = SimulationIdentity.Require(owner, nameof(owner));
            Multiplicity = multiplicity;
            CanonicalIdentity = SimulationIdentity.Require(canonicalIdentity, nameof(canonicalIdentity));
            DiagnosticsShape = SimulationIdentity.Require(diagnosticsShape, nameof(diagnosticsShape));
            ProducerPhases = producerPhases;
            ConsumerPhases = consumerPhases;
            Consumption = consumption;
            ProvenanceFields = provenanceFields;
            AppendOrdering = appendOrdering;
        }

        public SimulationPipelineProductId ProductId { get; }
        public SimulationPipelineProductSchemaVersion SchemaVersion { get; }
        public string Owner { get; }
        public SimulationPipelineProductMultiplicity Multiplicity { get; }
        public string CanonicalIdentity { get; }
        public string DiagnosticsShape { get; }
        public SimulationPipelinePhaseMask ProducerPhases { get; }
        public SimulationPipelinePhaseMask ConsumerPhases { get; }
        public SimulationPipelineProductConsumption Consumption { get; }
        public SimulationPipelineProvenanceFields ProvenanceFields { get; }
        public SimulationPipelineAppendOrdering AppendOrdering { get; }
        public string VersionedIdentity => $"{ProductId}@{SchemaVersion}";

        public bool Equals(SimulationPipelineProductContract other)
        {
            return other != null && ProductId.Equals(other.ProductId) && SchemaVersion.Equals(other.SchemaVersion) &&
                   string.Equals(Owner, other.Owner, StringComparison.Ordinal) && Multiplicity == other.Multiplicity &&
                   string.Equals(CanonicalIdentity, other.CanonicalIdentity, StringComparison.Ordinal) &&
                   string.Equals(DiagnosticsShape, other.DiagnosticsShape, StringComparison.Ordinal) &&
                   ProducerPhases == other.ProducerPhases && ConsumerPhases == other.ConsumerPhases &&
                   Consumption == other.Consumption &&
                   ProvenanceFields == other.ProvenanceFields && AppendOrdering == other.AppendOrdering;
        }

        public override bool Equals(object obj) => Equals(obj as SimulationPipelineProductContract);
        public override int GetHashCode() => HashCode.Combine(ProductId, SchemaVersion, Owner, (int)Multiplicity);
    }

    public readonly struct SimulationPipelineAppendEntryIdentity : IComparable<SimulationPipelineAppendEntryIdentity>
    {
        public SimulationPipelineAppendEntryIdentity(
            ActorId actorId,
            SimulationTick tick,
            ulong sequence,
            SimulationTickSourceIdentity source)
        {
            if (!actorId.IsValid || !tick.IsValid || sequence == 0 || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0)
                throw new ArgumentException("Append-only Product provenance is incomplete.");
            ActorId = actorId;
            Tick = tick;
            Sequence = sequence;
            Source = source;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public ulong Sequence { get; }
        public SimulationTickSourceIdentity Source { get; }

        public int CompareTo(SimulationPipelineAppendEntryIdentity other)
        {
            int actor = ActorId.CompareTo(other.ActorId);
            if (actor != 0)
                return actor;
            int tick = Tick.CompareTo(other.Tick);
            if (tick != 0)
                return tick;
            int sequence = Sequence.CompareTo(other.Sequence);
            if (sequence != 0)
                return sequence;
            int kind = Source.Kind.CompareTo(other.Source.Kind);
            if (kind != 0)
                return kind;
            int clock = CanonicalTextValue.CompareOrdinal(Source.ClockId, other.Source.ClockId);
            return clock != 0 ? clock : Source.SourceTick.CompareTo(other.Source.SourceTick);
        }
    }

    public static class SimulationPipelineProducts
    {
        const string Owner = "thirdperson.simulation.core";
        static readonly ReadOnlyCollection<SimulationPipelineProductContract> s_All =
            new List<SimulationPipelineProductContract>
            {
                Exclusive("simulation.canonical-inputs", "canonical-input-batch/3", "actor/input/local-combat/kinematic-body/source", SimulationPipelinePhaseMask.Ingress, SimulationPipelinePhaseMask.Ingress | SimulationPipelinePhaseMask.Schedule | SimulationPipelinePhaseMask.Step),
                Exclusive("simulation.typed-ingress", "target-typed-ingress/1", "actor/fact/source", SimulationPipelinePhaseMask.Ingress, SimulationPipelinePhaseMask.Schedule | SimulationPipelinePhaseMask.Step),
                Exclusive("simulation.execution-plan", "session-execution-plan/1", "restore/steps/provenance", SimulationPipelinePhaseMask.Schedule, SimulationPipelinePhaseMask.Step | SimulationPipelinePhaseMask.Egress, SimulationPipelineProductConsumption.BackendTerminal),
                Exclusive("simulation.character-evaluation-results", "character-evaluation-results/1", "character/result/tick", SimulationPipelinePhaseMask.Step, SimulationPipelinePhaseMask.Step),
                Exclusive("simulation.world-solve-batch-request", "target-world-request-batch/2", "actor/request/kinematic-body/order", SimulationPipelinePhaseMask.Step, SimulationPipelinePhaseMask.Step),
                Exclusive("simulation.world-solve-batch-result", "target-world-result-batch/1", "actor/result/order", SimulationPipelinePhaseMask.Step, SimulationPipelinePhaseMask.Step),
                Append("simulation.finalized-step-result", "target-finalized-step/1", "actor/tick/event", SimulationPipelinePhaseMask.Step, SimulationPipelinePhaseMask.Step | SimulationPipelinePhaseMask.Egress),
                Append("simulation.pipeline-snapshot-contribution", "pipeline-pass-state/1", "actor/tick/pass/state", SimulationPipelinePhaseMask.Step | SimulationPipelinePhaseMask.Egress, SimulationPipelinePhaseMask.Egress, SimulationPipelineProductConsumption.BackendTerminal),
                Exclusive("simulation.output-disposition-set", "event-output-disposition/1", "event/disposition/target", SimulationPipelinePhaseMask.Egress, SimulationPipelinePhaseMask.Egress, SimulationPipelineProductConsumption.BackendTerminal),
                Append("simulation.source-egress", "source-egress-record/1", "actor/tick/channel/payload", SimulationPipelinePhaseMask.Egress, SimulationPipelinePhaseMask.Egress, SimulationPipelineProductConsumption.CommitTerminal),
                Exclusive("simulation.combat-batch-result", "combat-batch-result/1", "source/target/action/event/accepted", SimulationPipelinePhaseMask.Step, SimulationPipelinePhaseMask.Step)
            }.AsReadOnly();

        public static IReadOnlyList<SimulationPipelineProductContract> All => s_All;
        public static SimulationPipelineProductContract CanonicalInputs => s_All[0];
        public static SimulationPipelineProductContract TypedIngress => s_All[1];
        public static SimulationPipelineProductContract ExecutionPlan => s_All[2];
        public static SimulationPipelineProductContract CharacterEvaluationResults => s_All[3];
        public static SimulationPipelineProductContract WorldSolveBatchRequest => s_All[4];
        public static SimulationPipelineProductContract WorldSolveBatchResult => s_All[5];
        public static SimulationPipelineProductContract FinalizedStepResult => s_All[6];
        public static SimulationPipelineProductContract PipelineSnapshotContribution => s_All[7];
        public static SimulationPipelineProductContract OutputDispositionSet => s_All[8];
        public static SimulationPipelineProductContract SourceEgress => s_All[9];
        public static SimulationPipelineProductContract CombatBatchResult => s_All[10];

        static SimulationPipelineProductContract Exclusive(
            string id,
            string canonicalIdentity,
            string diagnosticsShape,
            SimulationPipelinePhaseMask producerPhases,
            SimulationPipelinePhaseMask consumerPhases,
            SimulationPipelineProductConsumption consumption = SimulationPipelineProductConsumption.InternalRequired)
        {
            return new SimulationPipelineProductContract(
                new SimulationPipelineProductId(id),
                new SimulationPipelineProductSchemaVersion(1),
                Owner,
                SimulationPipelineProductMultiplicity.Exclusive,
                canonicalIdentity,
                diagnosticsShape,
                producerPhases,
                consumerPhases,
                consumption);
        }

        static SimulationPipelineProductContract Append(
            string id,
            string canonicalIdentity,
            string diagnosticsShape,
            SimulationPipelinePhaseMask producerPhases,
            SimulationPipelinePhaseMask consumerPhases,
            SimulationPipelineProductConsumption consumption = SimulationPipelineProductConsumption.InternalRequired)
        {
            return new SimulationPipelineProductContract(
                new SimulationPipelineProductId(id),
                new SimulationPipelineProductSchemaVersion(1),
                Owner,
                SimulationPipelineProductMultiplicity.AppendOnly,
                canonicalIdentity,
                diagnosticsShape,
                producerPhases,
                consumerPhases,
                consumption,
                SimulationPipelineProvenanceFields.All,
                SimulationPipelineAppendOrdering.ActorTickSequenceSource);
        }
    }
}
