using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct Float32StepInput
    {
        public Float32StepInput(SimulationInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public SimulationInput Input { get; }
    }

    public readonly struct Float32CanonicalInputBatch
    {
        readonly SimulationPipelineActorInput<Float32StepInput>[] m_Inputs;

        internal Float32CanonicalInputBatch(
            SimulationTickSourceIdentity source,
            SimulationPipelineActorInput<Float32StepInput>[] inputs)
        {
            Source = source;
            m_Inputs = inputs;
        }

        public SimulationTickSourceIdentity Source { get; }
        public IReadOnlyList<SimulationPipelineActorInput<Float32StepInput>> Inputs => m_Inputs;
        public bool IsValid => Source.SourceTick != 0 && !CanonicalTextValue.IsNullOrEmpty(Source.ClockId) && m_Inputs != null;
    }

    public readonly struct Float32TypedIngressBatch
    {
        public static Float32TypedIngressBatch Empty => default;
        public IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> Ingress =>
            Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
        public bool IsValid => Ingress != null;
    }

    public sealed class Float32SimulationStep : TargetSimulationPipelineStep<Float32StepInput, SimulationIngress>
    {
        public Float32SimulationStep(IReadOnlyList<ActorId> actors)
            : base(actors)
        {
        }

        public void Reset(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IReadOnlyList<SimulationPipelineActorInput<Float32StepInput>> inputs,
            IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingress,
            ObservedWorldConstraintFrame observedWorldConstraints)
        {
            ObservedWorldConstraints = observedWorldConstraints;
            ResetOrderedInputs(tick, provenance, inputs, ingress);
        }

        public static Float32SimulationStep FromOwnedInputs(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<Float32StepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress,
            ObservedWorldConstraintFrame observedWorldConstraints)
        {
            return new Float32SimulationStep(tick, provenance, inputs, actors, ingress, observedWorldConstraints);
        }

        public Float32SimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IEnumerable<SimulationPipelineActorInput<Float32StepInput>> inputs,
            IEnumerable<SimulationPipelineTypedIngress<SimulationIngress>> ingress,
            ObservedWorldConstraintFrame observedWorldConstraints)
            : base(tick, provenance, inputs, ingress)
        {
            ObservedWorldConstraints = observedWorldConstraints ??
                throw new ArgumentNullException(nameof(observedWorldConstraints));
            if (ObservedWorldConstraints.Tick != tick)
                throw new ArgumentException("Observed world constraint frame Tick does not match the Step.", nameof(observedWorldConstraints));
        }

        Float32SimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<Float32StepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress,
            ObservedWorldConstraintFrame observedWorldConstraints)
            : base(tick, provenance, inputs, actors, ingress)
        {
            ObservedWorldConstraints = observedWorldConstraints ??
                throw new ArgumentNullException(nameof(observedWorldConstraints));
            if (ObservedWorldConstraints.Tick != tick)
                throw new ArgumentException("Observed world constraint frame Tick does not match the Step.", nameof(observedWorldConstraints));
        }

        public ObservedWorldConstraintFrame ObservedWorldConstraints { get; private set; }
    }

    public sealed class Float32CharacterEvaluationResultBatch
    {
        static readonly Comparison<Float32CharacterEvaluationResult> s_CompareEvaluations = CompareEvaluations;
        readonly Float32CharacterEvaluationResult[] m_Evaluations;

        internal Float32CharacterEvaluationResultBatch(int actorCount)
        {
            if (actorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Evaluations = new Float32CharacterEvaluationResult[actorCount];
        }

        internal Float32CharacterEvaluationResultBatch Reset(
            SimulationTick tick,
            Float32CharacterEvaluationResult[] evaluations)
        {
            if (!tick.IsValid || evaluations == null || evaluations.Length != m_Evaluations.Length)
                throw new ArgumentException("Character evaluation result batch workspace is invalid.");
            for (int i = 0; i < evaluations.Length; i++)
            {
                if (evaluations[i] == null)
                    throw new ArgumentException("Character evaluation result batch contains a missing evaluation.", nameof(evaluations));
            }
            Array.Copy(evaluations, m_Evaluations, evaluations.Length);
            Array.Sort(m_Evaluations, s_CompareEvaluations);
            for (int i = 0; i < m_Evaluations.Length; i++)
            {
                if (m_Evaluations[i].Tick != tick ||
                    i > 0 && m_Evaluations[i - 1].ActorId.Equals(m_Evaluations[i].ActorId))
                {
                    throw new ArgumentException("Character evaluation result batch identity is invalid.", nameof(evaluations));
                }
            }
            Tick = tick;
            return this;
        }

        static int CompareEvaluations(Float32CharacterEvaluationResult left, Float32CharacterEvaluationResult right) =>
            left.ActorId.CompareTo(right.ActorId);

        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<Float32CharacterEvaluationResult> Evaluations => m_Evaluations;

        internal void DiscardUnconsumed()
        {
            for (int i = 0; i < m_Evaluations.Length; i++)
                m_Evaluations[i].DiscardUnconsumed();
        }
    }

    public readonly struct Float32CompletedSimulationStep
    {
        public Float32CompletedSimulationStep(
            Float32SimulationStep step,
            SimulationTickResult result,
            SimulationWorldStateSet state,
            Float32SimulationStepSnapshot stepSnapshot)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (step.Tick != result.Tick || state.LastCompletedTick != step.Tick.Value)
                throw new ArgumentException("Completed Step result and state Tick do not match.");
            if (stepSnapshot.IsValid && stepSnapshot.Tick != step.Tick)
                throw new ArgumentException("Completed Step snapshot Tick does not match.", nameof(stepSnapshot));
            StepSnapshot = stepSnapshot;
        }

        public Float32SimulationStep Step { get; }
        public SimulationTickResult Result { get; }
        public SimulationWorldStateSet State { get; }
        public Float32SimulationStepSnapshot StepSnapshot { get; }
        public SimulationPipelineStateSnapshot PipelineProjection => StepSnapshot.IsValid ? StepSnapshot.PipelineProjection : null;
        public bool IsValid => Step != null && Result != null && State != null;
    }

    public readonly struct SimulationPipelineOutputDispositionSet
    {
        internal static readonly Comparison<SimulationOutputDisposition> s_CompareDispositions = CompareDispositions;
        readonly SimulationOutputDispositionView m_Dispositions;

        public static SimulationPipelineOutputDispositionSet FromOwnedDispositions(
            StableHash transactionIdentity,
            SimulationOutputDisposition[] dispositions)
        {
            if (!transactionIdentity.IsValid)
                throw new ArgumentException("Output disposition transaction identity is invalid.", nameof(transactionIdentity));
            SimulationOutputDisposition[] values = dispositions ?? Array.Empty<SimulationOutputDisposition>();
            Array.Sort(values, s_CompareDispositions);
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i - 1].SourceEventId.Equals(values[i].SourceEventId))
                    throw new ArgumentException("Output disposition set contains duplicate EventId ownership.", nameof(dispositions));
            }
            return new SimulationPipelineOutputDispositionSet(transactionIdentity, new SimulationOutputDispositionView(values));
        }

        public static SimulationPipelineOutputDispositionSet FromBufferedDispositions(
            StableHash transactionIdentity,
            SimulationOutputDispositionBuffer dispositions)
        {
            dispositions.Sort();
            return new SimulationPipelineOutputDispositionSet(transactionIdentity,
                new SimulationOutputDispositionView(dispositions));
        }

        SimulationPipelineOutputDispositionSet(
            StableHash transactionIdentity,
            SimulationOutputDispositionView dispositions)
        {
            TransactionIdentity = transactionIdentity;
            m_Dispositions = dispositions;
        }

        public StableHash TransactionIdentity { get; }
        public SimulationOutputDispositionView Dispositions => m_Dispositions;
        public bool IsValid => TransactionIdentity.IsValid && m_Dispositions.IsInitialized;

        static int CompareDispositions(SimulationOutputDisposition left, SimulationOutputDisposition right) =>
            left.SourceEventId.CompareTo(right.SourceEventId);
    }

    public readonly struct SimulationOutputDispositionView
    {
        readonly SimulationOutputDisposition[] m_OwnedValues;
        readonly SimulationOutputDispositionBuffer m_Buffer;
        readonly int m_Version;

        internal SimulationOutputDispositionView(SimulationOutputDisposition[] values)
        {
            m_OwnedValues = values;
            m_Buffer = null;
            m_Version = 0;
        }

        internal SimulationOutputDispositionView(SimulationOutputDispositionBuffer buffer)
        {
            m_OwnedValues = null;
            m_Buffer = buffer;
            m_Version = buffer.Version;
        }

        internal bool IsInitialized => m_OwnedValues != null || m_Buffer != null;
        public int Count => m_Buffer == null ? m_OwnedValues.Length : m_Buffer.GetCount(m_Version);
        public SimulationOutputDisposition this[int index] =>
            m_Buffer == null ? m_OwnedValues[index] : m_Buffer.Get(index, m_Version);
    }

    public sealed class SimulationOutputDispositionBuffer
    {
        readonly List<SimulationOutputDisposition> m_Values;

        public SimulationOutputDispositionBuffer(int capacity)
        {
            m_Values = new List<SimulationOutputDisposition>(capacity);
        }

        internal int Version { get; private set; }
        public int Count => m_Values.Count;
        public SimulationOutputDisposition this[int index] => m_Values[index];

        public void Begin()
        {
            Version++;
            m_Values.Clear();
        }

        public void Add(SimulationOutputDisposition value) => m_Values.Add(value);
        internal void Sort() => m_Values.Sort(SimulationPipelineOutputDispositionSet.s_CompareDispositions);

        internal int GetCount(int version)
        {
            RequireVersion(version);
            return m_Values.Count;
        }

        internal SimulationOutputDisposition Get(int index, int version)
        {
            RequireVersion(version);
            return m_Values[index];
        }

        void RequireVersion(int version)
        {
            if (version != Version)
                throw new InvalidOperationException("Output disposition buffer has been reused by a later transaction.");
        }

    }

    public sealed class Float32SourceEgressRecord
    {
        readonly byte[] m_Payload;
        int m_PayloadLength;

        public Float32SourceEgressRecord(string channelId, string schemaId, int schemaVersion,
            byte[] payloadBuffer)
        {
            ChannelId = channelId;
            SchemaId = schemaId;
            SchemaVersion = schemaVersion;
            m_Payload = payloadBuffer;
        }

        public void Publish(ActorId actorId, SimulationTick tick, int payloadLength)
        {
            ActorId = actorId;
            Tick = tick;
            m_PayloadLength = payloadLength;
            PayloadHash = SimulationCanonicalPayloadHash.Compute(Payload.Span);
        }

        public ActorId ActorId { get; private set; }
        public SimulationTick Tick { get; private set; }
        public string ChannelId { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public StableHash PayloadHash { get; private set; }
        public ReadOnlyMemory<byte> Payload => new ReadOnlyMemory<byte>(m_Payload, 0, m_PayloadLength);
    }

    [ValueOperations(RetainMethod = nameof(RetainStorage), ReleaseMethod = nameof(ReleaseStorage))]
    public sealed partial class Float32SimulationSessionSnapshot : ISimulationSessionSnapshot<SimulationWorldSnapshot>
    {
        public Float32SimulationSessionSnapshot(
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipeline)
        {
            CompositionIdentity = compositionIdentity;
            World = world;
            Pipeline = pipeline;
        }

        public Float32SimulationSessionSnapshot(SimulationSessionCompositionIdentity compositionIdentity)
        {
            CompositionIdentity = compositionIdentity;
        }

        public SimulationSessionCompositionIdentity CompositionIdentity { get; }
        [ValueResource(nameof(SimulationWorldSnapshot.RetainStateStorage), nameof(SimulationWorldSnapshot.ReleaseStateStorage))]
        public SimulationWorldSnapshot World { get; private set; }
        [ValueResource(nameof(SimulationPipelineStateSnapshot.RetainStepStorage), nameof(SimulationPipelineStateSnapshot.ReleaseStepStorage))]
        public SimulationPipelineStateSnapshot Pipeline { get; private set; }
        public SimulationTick Tick => World.Tick;

        public void SetPrepared(SimulationWorldSnapshot world, SimulationPipelineStateSnapshot pipeline)
        {
            World = world;
            Pipeline = pipeline;
        }

        public partial void RetainStorage();
        public partial void ReleaseStorage();
        void IValueResources.Retain() => RetainStorage();
        void IValueResources.Release() => ReleaseStorage();
    }

    public readonly struct Float32SimulationStepSnapshot
    {
        public Float32SimulationStepSnapshot(
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipelineProjection)
        {
            if (!compositionIdentity.IsValid)
                throw new ArgumentException("Step snapshot composition identity is invalid.", nameof(compositionIdentity));
            World = world ?? throw new ArgumentNullException(nameof(world));
            PipelineProjection = pipelineProjection ?? throw new ArgumentNullException(nameof(pipelineProjection));
            if (pipelineProjection.LastCompletedTick != world.Tick.Value)
                throw new ArgumentException("World snapshot and Pipeline projection Ticks do not match.");
            CompositionIdentity = compositionIdentity;
        }

        public SimulationSessionCompositionIdentity CompositionIdentity { get; }
        public SimulationWorldSnapshot World { get; }
        public SimulationPipelineStateSnapshot PipelineProjection { get; }
        public SimulationTick Tick => World.Tick;
        public bool IsValid => CompositionIdentity.IsValid && World != null && PipelineProjection != null;
    }

    public readonly struct Float32SimulationCommitBatch
    {
        readonly IReadOnlyList<Float32CompletedSimulationStep> m_Steps;
        readonly IReadOnlyList<Float32SourceEgressRecord> m_SourceEgress;

        public Float32SimulationCommitBatch(
            StableHash transactionIdentity,
            IReadOnlyList<Float32CompletedSimulationStep> steps,
            SimulationPipelineOutputDispositionSet outputDispositions,
            IReadOnlyList<Float32SourceEgressRecord> sourceEgress)
        {
            TransactionIdentity = transactionIdentity;
            OutputDispositions = outputDispositions;
            m_Steps = steps;
            m_SourceEgress = sourceEgress;
        }

        public StableHash TransactionIdentity { get; }
        public bool IsValid => TransactionIdentity.IsValid &&
                               m_Steps != null &&
                               m_SourceEgress != null &&
                               OutputDispositions.IsValid;
        public IReadOnlyList<Float32CompletedSimulationStep> Steps => m_Steps;
        public SimulationPipelineOutputDispositionSet OutputDispositions { get; }
        public IReadOnlyList<Float32SourceEgressRecord> SourceEgress => m_SourceEgress;
    }

    public interface IFloat32SimulationCommitter
    {
        SimulationComponentIdentity Identity { get; }
        void PrepareOutputCapacity(int stepOutputCapacity, int frameOutputCapacity);
        void Commit(Float32SimulationCommitBatch batch);
    }

    public interface IFloat32SimulationRestoreSource : ISimulationRuntimePort
    {
        Float32SimulationSessionSnapshot GetRequiredSnapshot(SimulationRestoreDirective directive);
    }
}
