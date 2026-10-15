using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.FPGameplay
{
    public readonly struct FPStepInput
    {
        public FPStepInput(SimulationInput input)
        {
            Input = input;
        }

        public SimulationInput Input { get; }
    }

    public readonly struct FPCanonicalInputBatch
    {
        readonly SimulationPipelineActorInput<FPStepInput>[] m_Inputs;

        public FPCanonicalInputBatch(
            SimulationTickSourceIdentity source,
            SimulationPipelineActorInput<FPStepInput>[] inputs,
            FPLocalEnemyInput[] localEnemies = null,
            WorldKinematicBodySample[] kinematicBodies = null)
        {
            Source = source;
            m_Inputs = inputs;
            LocalEnemies = localEnemies ?? Array.Empty<FPLocalEnemyInput>();
            KinematicBodies = kinematicBodies ?? Array.Empty<WorldKinematicBodySample>();
        }

        public SimulationTickSourceIdentity Source { get; }
        public IReadOnlyList<FPLocalEnemyInput> LocalEnemies { get; }
        public IReadOnlyList<WorldKinematicBodySample> KinematicBodies { get; }
        public IReadOnlyList<SimulationPipelineActorInput<FPStepInput>> Inputs => m_Inputs;
        public bool IsValid => Source.SourceTick != 0 && !CanonicalTextValue.IsNullOrEmpty(Source.ClockId) && m_Inputs != null;
    }

    public readonly struct FPTypedIngressBatch
    {
        readonly IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> m_Ingress;

        public FPTypedIngressBatch(IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
        {
            if (ingress == null || ingress.Count == 0)
            {
                m_Ingress = Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
                return;
            }
            var values = new SimulationPipelineTypedIngress<SimulationIngress>[ingress.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = ingress[i];
            SimulationPipelineTypedIngress<SimulationIngress>.SortByIdentity(values);
            m_Ingress = values;
        }

        internal FPTypedIngressBatch(FPQueuedIngressBuffer ingress) => m_Ingress = ingress;

        public static FPTypedIngressBatch Empty => default;
        public static FPTypedIngressBatch FromBufferedIngress(
            List<SimulationPipelineTypedIngress<SimulationIngress>> ingress) => new FPTypedIngressBatch(ingress);

        FPTypedIngressBatch(List<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
        {
            m_Ingress = ingress;
        }
        public IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> Ingress =>
            m_Ingress ?? Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
        public bool IsValid => Ingress != null;
    }

    public sealed class FPSimulationStep : TargetSimulationPipelineStep<FPStepInput, SimulationIngress>
    {
        public FPSimulationStep(IReadOnlyList<ActorId> actors)
            : base(actors)
        {
        }

        public void Reset(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IReadOnlyList<SimulationPipelineActorInput<FPStepInput>> inputs,
            IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
        {
            ResetOrderedInputs(tick, provenance, inputs, ingress);
        }

        public FPSimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IEnumerable<SimulationPipelineActorInput<FPStepInput>> inputs,
            IEnumerable<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
            : base(tick, provenance, inputs, ingress)
        {
        }

        public static FPSimulationStep FromOwnedInputs(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<FPStepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress)
        {
            return new FPSimulationStep(tick, provenance, inputs, actors, ingress);
        }

        FPSimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<FPStepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress)
            : base(tick, provenance, inputs, actors, ingress)
        {
        }
    }

    public sealed class FPCharacterEvaluationResultBatch
    {
        static readonly Comparison<FPCharacterEvaluationResult> s_CompareEvaluations = CompareEvaluations;
        readonly FPCharacterEvaluationResult[] m_Evaluations;

        internal FPCharacterEvaluationResultBatch(int actorCount)
        {
            if (actorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Evaluations = new FPCharacterEvaluationResult[actorCount];
        }

        internal FPCharacterEvaluationResultBatch Reset(
            SimulationTick tick,
            FPCharacterEvaluationResult[] evaluations)
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

        static int CompareEvaluations(FPCharacterEvaluationResult left, FPCharacterEvaluationResult right) =>
            left.ActorId.CompareTo(right.ActorId);

        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<FPCharacterEvaluationResult> Evaluations => m_Evaluations;

        public void DiscardUnconsumed()
        {
            for (int i = 0; i < m_Evaluations.Length; i++)
                m_Evaluations[i].DiscardUnconsumed();
        }
    }

    public readonly struct FPCompletedSimulationStep
    {
        public FPCompletedSimulationStep(
            FPSimulationStep step,
            SimulationTickResult result,
            SimulationWorldStateSet state,
            FPSimulationStepSnapshot stepSnapshot)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            State = state;
            if (step.Tick != result.Tick || state.LastCompletedTick != step.Tick.Value)
                throw new ArgumentException("Completed Step result and state Tick do not match.");
            if (stepSnapshot.IsValid && stepSnapshot.Tick != step.Tick)
                throw new ArgumentException("Completed Step snapshot Tick does not match.", nameof(stepSnapshot));
            StepSnapshot = stepSnapshot;
        }

        public FPSimulationStep Step { get; }
        public SimulationTickResult Result { get; }
        public SimulationWorldStateSet State { get; }
        public FPSimulationStepSnapshot StepSnapshot { get; }
        public SimulationPipelineStateSnapshot PipelineProjection => StepSnapshot.IsValid ? StepSnapshot.PipelineProjection : null;
        public bool IsValid => Step != null && Result != null && State.IsValid;
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
            if (!transactionIdentity.IsValid)
                throw new ArgumentException("Output disposition transaction identity is invalid.", nameof(transactionIdentity));
            dispositions.Sort();
            var values = new SimulationOutputDispositionView(dispositions);
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].SourceEventId.Equals(values[i].SourceEventId))
                    throw new ArgumentException("Output disposition set contains duplicate EventId ownership.", nameof(dispositions));
            }
            return new SimulationPipelineOutputDispositionSet(transactionIdentity, values);
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

    public sealed class FPSourceEgressRecord
    {
        readonly byte[] m_Payload;
        int m_PayloadLength;

        public FPSourceEgressRecord(
            string channelId,
            string schemaId,
            int schemaVersion,
            int payloadCapacity)
        {
            if (schemaVersion <= 0 || payloadCapacity <= 0)
                throw new ArgumentException("Source egress record identity is incomplete.");
            ChannelId = SimulationIdentity.Require(channelId, nameof(channelId));
            SchemaId = SimulationIdentity.Require(schemaId, nameof(schemaId));
            SchemaVersion = schemaVersion;
            m_Payload = new byte[payloadCapacity];
        }

        public void Capture(ActorId actorId, SimulationTick tick, ReadOnlySpan<byte> canonicalPayload)
        {
            ActorId = actorId;
            Tick = tick;
            canonicalPayload.CopyTo(m_Payload);
            m_PayloadLength = canonicalPayload.Length;
            PayloadHash = SimulationCanonicalPayloadHash.Compute(canonicalPayload);
        }

        public ActorId ActorId { get; private set; }
        public SimulationTick Tick { get; private set; }
        public string ChannelId { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public StableHash PayloadHash { get; private set; }
        public ReadOnlyMemory<byte> Payload => m_Payload.AsMemory(0, m_PayloadLength);
    }

    [ValueOperations(RetainMethod = nameof(RetainStorage), ReleaseMethod = nameof(ReleaseStorage))]
    public sealed partial class FPSimulationSessionSnapshot : ISimulationSessionSnapshot<SimulationWorldSnapshot>
    {
        public FPSimulationSessionSnapshot(
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipeline)
        {
            CompositionIdentity = compositionIdentity;
            World = world;
            Pipeline = pipeline;
        }

        public FPSimulationSessionSnapshot(SimulationSessionCompositionIdentity compositionIdentity)
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

    public readonly struct FPSimulationStepSnapshot
    {
        public FPSimulationStepSnapshot(
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
            Session = null;
        }

        internal FPSimulationStepSnapshot(FPSimulationSessionSnapshot session)
        {
            Session = session;
            CompositionIdentity = session.CompositionIdentity;
            World = session.World;
            PipelineProjection = session.Pipeline;
        }

        public FPSimulationSessionSnapshot Session { get; }
        public SimulationSessionCompositionIdentity CompositionIdentity { get; }
        public SimulationWorldSnapshot World { get; }
        public SimulationPipelineStateSnapshot PipelineProjection { get; }
        public SimulationTick Tick => World.Tick;
        public bool IsValid => CompositionIdentity.IsValid && World != null && PipelineProjection != null;
    }

    public readonly struct FPSimulationCommitBatch
    {
        readonly IReadOnlyList<FPCompletedSimulationStep> m_Steps;
        readonly IReadOnlyList<FPSourceEgressRecord> m_SourceEgress;

        public FPSimulationCommitBatch(
            StableHash transactionIdentity,
            IReadOnlyList<FPCompletedSimulationStep> steps,
            SimulationPipelineOutputDispositionSet outputDispositions,
            IReadOnlyList<FPSourceEgressRecord> sourceEgress)
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
        public IReadOnlyList<FPCompletedSimulationStep> Steps => m_Steps;
        public SimulationPipelineOutputDispositionSet OutputDispositions { get; }
        public IReadOnlyList<FPSourceEgressRecord> SourceEgress => m_SourceEgress;
    }

    public interface IFPSimulationCommitter
    {
        SimulationComponentIdentity Identity { get; }
        void Commit(FPSimulationCommitBatch batch);
    }

    public interface IFPSimulationRestoreSource : ISimulationRuntimePort
    {
        FPSimulationSessionSnapshot GetRequiredSnapshot(SimulationRestoreDirective directive);
    }
}
