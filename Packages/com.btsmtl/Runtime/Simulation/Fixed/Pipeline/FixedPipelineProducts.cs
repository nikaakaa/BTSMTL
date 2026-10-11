using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedStepInput
    {
        public FixedStepInput(SimulationInput input)
        {
            Input = input;
        }

        public SimulationInput Input { get; }
    }

    public readonly struct FixedCanonicalInputBatch
    {
        readonly SimulationPipelineActorInput<FixedStepInput>[] m_Inputs;

        internal FixedCanonicalInputBatch(
            SimulationTickSourceIdentity source,
            SimulationPipelineActorInput<FixedStepInput>[] inputs,
            FixedLocalEnemyInput[] localEnemies = null,
            WorldKinematicBodySample[] kinematicBodies = null)
        {
            Source = source;
            m_Inputs = inputs;
            LocalEnemies = localEnemies ?? Array.Empty<FixedLocalEnemyInput>();
            KinematicBodies = kinematicBodies ?? Array.Empty<WorldKinematicBodySample>();
        }

        public SimulationTickSourceIdentity Source { get; }
        public IReadOnlyList<FixedLocalEnemyInput> LocalEnemies { get; }
        public IReadOnlyList<WorldKinematicBodySample> KinematicBodies { get; }
        public IReadOnlyList<SimulationPipelineActorInput<FixedStepInput>> Inputs => m_Inputs;
        public bool IsValid => Source.SourceTick != 0 && !CanonicalTextValue.IsNullOrEmpty(Source.ClockId) && m_Inputs != null;
    }

    public readonly struct FixedTypedIngressBatch
    {
        readonly IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> m_Ingress;

        public FixedTypedIngressBatch(IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
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

        internal FixedTypedIngressBatch(FixedQueuedIngressBuffer ingress) => m_Ingress = ingress;

        public static FixedTypedIngressBatch Empty => default;
        public static FixedTypedIngressBatch FromBufferedIngress(
            List<SimulationPipelineTypedIngress<SimulationIngress>> ingress) => new FixedTypedIngressBatch(ingress);

        FixedTypedIngressBatch(List<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
        {
            m_Ingress = ingress;
        }
        public IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> Ingress =>
            m_Ingress ?? Array.Empty<SimulationPipelineTypedIngress<SimulationIngress>>();
        public bool IsValid => Ingress != null;
    }

    public sealed class FixedSimulationStep : TargetSimulationPipelineStep<FixedStepInput, SimulationIngress>
    {
        public FixedSimulationStep(IReadOnlyList<ActorId> actors)
            : base(actors)
        {
        }

        public void Reset(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IReadOnlyList<SimulationPipelineActorInput<FixedStepInput>> inputs,
            IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
        {
            ResetOrderedInputs(tick, provenance, inputs, ingress);
        }

        public FixedSimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            IEnumerable<SimulationPipelineActorInput<FixedStepInput>> inputs,
            IEnumerable<SimulationPipelineTypedIngress<SimulationIngress>> ingress)
            : base(tick, provenance, inputs, ingress)
        {
        }

        public static FixedSimulationStep FromOwnedInputs(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<FixedStepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress)
        {
            return new FixedSimulationStep(tick, provenance, inputs, actors, ingress);
        }

        FixedSimulationStep(
            SimulationTick tick,
            SimulationPipelineStepProvenance provenance,
            SimulationPipelineActorInput<FixedStepInput>[] inputs,
            ActorId[] actors,
            SimulationPipelineTypedIngress<SimulationIngress>[] ingress)
            : base(tick, provenance, inputs, actors, ingress)
        {
        }
    }

    public sealed class FixedCharacterEvaluationResultBatch
    {
        static readonly Comparison<FixedCharacterEvaluationResult> s_CompareEvaluations = CompareEvaluations;
        readonly FixedCharacterEvaluationResult[] m_Evaluations;

        internal FixedCharacterEvaluationResultBatch(int actorCount)
        {
            if (actorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            m_Evaluations = new FixedCharacterEvaluationResult[actorCount];
        }

        internal FixedCharacterEvaluationResultBatch Reset(
            SimulationTick tick,
            FixedCharacterEvaluationResult[] evaluations)
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

        static int CompareEvaluations(FixedCharacterEvaluationResult left, FixedCharacterEvaluationResult right) =>
            left.ActorId.CompareTo(right.ActorId);

        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<FixedCharacterEvaluationResult> Evaluations => m_Evaluations;

        internal void DiscardUnconsumed()
        {
            for (int i = 0; i < m_Evaluations.Length; i++)
                m_Evaluations[i].DiscardUnconsumed();
        }
    }

    public readonly struct FixedCompletedSimulationStep
    {
        public FixedCompletedSimulationStep(
            FixedSimulationStep step,
            SimulationTickResult result,
            SimulationWorldStateSet state,
            FixedSimulationStepSnapshot stepSnapshot)
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

        public FixedSimulationStep Step { get; }
        public SimulationTickResult Result { get; }
        public SimulationWorldStateSet State { get; }
        public FixedSimulationStepSnapshot StepSnapshot { get; }
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

    public sealed class FixedSourceEgressRecord
    {
        readonly byte[] m_Payload;
        int m_PayloadLength;

        public FixedSourceEgressRecord(
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

    public sealed class FixedSimulationSessionSnapshot
    {
        public FixedSimulationSessionSnapshot(
            SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world,
            SimulationPipelineStateSnapshot pipeline)
        {
            if (!compositionIdentity.IsValid)
                throw new ArgumentException("Session snapshot composition identity is invalid.", nameof(compositionIdentity));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            if (pipeline.LastCompletedTick != world.Tick.Value)
                throw new ArgumentException("World and Pipeline snapshot Ticks do not match.");
            CompositionIdentity = compositionIdentity;
            SnapshotHash = ComputeSnapshotHash(
                "fixed-session-snapshot/2",
                compositionIdentity.Value,
                world.WorldHash.Value,
                pipeline.SnapshotHash);
        }

        public SimulationSessionCompositionIdentity CompositionIdentity { get; }
        public SimulationWorldSnapshot World { get; private set; }
        public SimulationPipelineStateSnapshot Pipeline { get; private set; }
        public SimulationTick Tick => World.Tick;
        public StableHash SnapshotHash { get; private set; }
#if UNITY_EDITOR
        internal SimulationSnapshotCaptureProfile CaptureProfile { get; set; }
#endif
        SimulationSessionCheckpointPayloadStorage.Page m_Storage;
        int m_PayloadLength;

        internal ReadOnlyMemory<byte> StoredPayload => m_Storage.Buffer.AsMemory(0, m_PayloadLength);
        internal SimulationSessionCheckpointPayloadStorage.Page Storage => m_Storage;

        internal void RetainStorage()
        {
            if (m_Storage != null)
                m_Storage.Retain();
            else
                Pipeline.RetainStepStorage();
        }

        public void ReleaseStorage()
        {
            if (m_Storage != null)
                m_Storage.Release();
            else
                Pipeline.ReleaseStepStorage();
        }

        internal void SetStorage(SimulationSessionCheckpointPayloadStorage.Page storage)
        {
            m_Storage = storage;
            m_PayloadLength = checked((int)storage.Writer.Length);
        }

        internal void ClearStorage()
        {
#if UNITY_EDITOR
            CaptureProfile = default;
#endif
            m_Storage = null;
            m_PayloadLength = 0;
            World = null;
            Pipeline = null;
            SnapshotHash = default;
        }

        internal void CompleteReception()
        {
            if (m_Storage != null && !m_Storage.ReceptionComplete)
            {
                World.SetPreparedTextResolver(m_Storage.Text);
                for (int index = 0; index < Pipeline.Participants.Count; index++)
                    Pipeline.Participants[index].SetPreparedTextResolver(m_Storage.Text);
                m_Storage.ReceptionComplete = true;
            }
        }

        internal FixedSimulationSessionSnapshot(SimulationSessionCompositionIdentity compositionIdentity)
        {
            CompositionIdentity = compositionIdentity;
        }

        internal void SetPrepared(SimulationWorldSnapshot world, SimulationPipelineStateSnapshot pipeline, CanonicalWriter writer)
        {
            World = world;
            Pipeline = pipeline;
            SnapshotHash = ComputeSnapshotHash("fixed-session-snapshot/2", CompositionIdentity.Value,
                world.WorldHash.Value, pipeline.SnapshotHash, writer);
        }

        static StableHash ComputeSnapshotHash(string domain, StableHash composition, StableHash world, StableHash pipeline) =>
            ComputeSnapshotHash(domain, composition, world, pipeline, StableHash.BeginHash());

        internal static StableHash ComputeSnapshotHash(
            string domain, StableHash composition, StableHash world, StableHash pipeline, CanonicalWriter writer)
        {
            writer.Reset();
            writer.WriteRawText(domain.AsSpan());
            WriteHashField(writer, composition);
            WriteHashField(writer, world);
            WriteHashField(writer, pipeline);
            return writer.ComputeHash();
        }

        static void WriteHashField(CanonicalWriter writer, StableHash value)
        {
            writer.WriteByte(0x1f);
            writer.WriteRawHash(value);
        }
    }

    public readonly struct FixedSimulationStepSnapshot
    {
        public FixedSimulationStepSnapshot(
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

        internal FixedSimulationStepSnapshot(FixedSimulationSessionSnapshot session)
        {
            Session = session;
            CompositionIdentity = session.CompositionIdentity;
            World = session.World;
            PipelineProjection = session.Pipeline;
        }

        internal FixedSimulationSessionSnapshot Session { get; }
        public SimulationSessionCompositionIdentity CompositionIdentity { get; }
        public SimulationWorldSnapshot World { get; }
        public SimulationPipelineStateSnapshot PipelineProjection { get; }
        public SimulationTick Tick => World.Tick;
        public bool IsValid => CompositionIdentity.IsValid && World != null && PipelineProjection != null;
    }

    public readonly struct FixedSimulationCommitBatch
    {
        readonly IReadOnlyList<FixedCompletedSimulationStep> m_Steps;
        readonly IReadOnlyList<FixedSourceEgressRecord> m_SourceEgress;

        public FixedSimulationCommitBatch(
            StableHash transactionIdentity,
            IReadOnlyList<FixedCompletedSimulationStep> steps,
            SimulationPipelineOutputDispositionSet outputDispositions,
            IReadOnlyList<FixedSourceEgressRecord> sourceEgress)
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
        public IReadOnlyList<FixedCompletedSimulationStep> Steps => m_Steps;
        public SimulationPipelineOutputDispositionSet OutputDispositions { get; }
        public IReadOnlyList<FixedSourceEgressRecord> SourceEgress => m_SourceEgress;
    }

    public interface IFixedSimulationCommitter
    {
        SimulationComponentIdentity Identity { get; }
        void Commit(FixedSimulationCommitBatch batch);
    }

    public interface IFixedSimulationSnapshotRetentionOwner : ISimulationPipelineSnapshotRetentionCapacity
    {
        void PrepareSnapshotStorage(IFixedSimulationSessionSnapshotCodec codec, in GameplayEffectSessionCapacity capacity);
        void ClearSnapshotStorage();
    }

    public interface IFixedSimulationRestoreSource : ISimulationRuntimePort
    {
        FixedSimulationSessionSnapshot GetRequiredSnapshot(SimulationRestoreDirective directive);
    }
}
