using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeProducts
    {
        const string Owner = "thirdperson.network-model.server-authoritative-hybrid";

        static readonly SimulationPipelineProductContract s_OwnerCanonicalInputBatch = Exclusive(
            "server-authoritative.owner-canonical-input-batch",
            "server-authoritative-owner-input/1",
            "actor/source-tick/input-sequence/input",
            SimulationPipelinePhaseMask.Ingress,
            SimulationPipelinePhaseMask.Schedule | SimulationPipelinePhaseMask.Egress);

        static readonly SimulationPipelineProductContract s_AuthoritativeObservationBatch = Exclusive(
            "server-authoritative.authoritative-observation-batch",
            "server-authoritative-observation/1",
            "receive-sequence/baseline/remote/event",
            SimulationPipelinePhaseMask.Ingress,
            SimulationPipelinePhaseMask.Schedule | SimulationPipelinePhaseMask.Egress);

        static readonly SimulationPipelineProductContract s_AuthoritativeActorBaseline = Exclusive(
            "server-authoritative.actor-baseline",
            "server-authoritative-baseline/1",
            "actor/tick/program/layout/state/body/ack/horizon",
            SimulationPipelinePhaseMask.Egress,
            SimulationPipelinePhaseMask.Egress);

        static readonly SimulationPipelineProductContract s_PredictionCorrectionDecision = Exclusive(
            "server-authoritative.prediction-correction-decision",
            "server-authoritative-correction-decision/1",
            "kind/reason/baseline/restore/replay/error",
            SimulationPipelinePhaseMask.Schedule,
            SimulationPipelinePhaseMask.Egress,
            SimulationPipelineProductConsumption.BackendTerminal);

        static readonly SimulationPipelineProductContract s_AcceptedAuthorityInputBatch = Exclusive(
            "server-authoritative.accepted-authority-input-batch",
            "server-authoritative-accepted-input/1",
            "authority-tick/actor/input-sequence/input",
            SimulationPipelinePhaseMask.Ingress,
            SimulationPipelinePhaseMask.Schedule | SimulationPipelinePhaseMask.Egress);

        static readonly SimulationPipelineProductContract s_AuthorityReplicationBatch = Exclusive(
            "server-authoritative.authority-replication-batch",
            "server-authoritative-replication/1",
            "authority-tick/baseline/body/reliable-event",
            SimulationPipelinePhaseMask.Egress,
            SimulationPipelinePhaseMask.Egress,
            SimulationPipelineProductConsumption.CommitTerminal);

        static readonly SimulationPipelineProductContract s_RemotePresentationBatch = Exclusive(
            "server-authoritative.remote-presentation-batch",
            "server-authoritative-remote-presentation/1",
            "actor/body/producer/fact/event",
            SimulationPipelinePhaseMask.Ingress,
            SimulationPipelinePhaseMask.Egress,
            SimulationPipelineProductConsumption.CommitTerminal);

        static readonly SimulationPipelineProductContract s_SelectedRemoteBodyBatch = Exclusive(
            "server-authoritative.selected-remote-body-batch",
            "server-authoritative-selected-remote-body/1",
            "actor/current-step/body/reset",
            SimulationPipelinePhaseMask.Schedule,
            SimulationPipelinePhaseMask.Egress);

        static readonly ReadOnlyCollection<SimulationPipelineProductContract> s_All =
            new List<SimulationPipelineProductContract>
            {
                s_OwnerCanonicalInputBatch,
                s_AuthoritativeObservationBatch,
                s_AuthoritativeActorBaseline,
                s_PredictionCorrectionDecision,
                s_AcceptedAuthorityInputBatch,
                s_AuthorityReplicationBatch,
                s_RemotePresentationBatch,
                s_SelectedRemoteBodyBatch
            }.AsReadOnly();

        public static SimulationPipelineProductContract OwnerCanonicalInputBatch => s_OwnerCanonicalInputBatch;
        public static SimulationPipelineProductContract AuthoritativeObservationBatch => s_AuthoritativeObservationBatch;
        public static SimulationPipelineProductContract AuthoritativeActorBaseline => s_AuthoritativeActorBaseline;
        public static SimulationPipelineProductContract PredictionCorrectionDecision => s_PredictionCorrectionDecision;
        public static SimulationPipelineProductContract AcceptedAuthorityInputBatch => s_AcceptedAuthorityInputBatch;
        public static SimulationPipelineProductContract AuthorityReplicationBatch => s_AuthorityReplicationBatch;
        public static SimulationPipelineProductContract RemotePresentationBatch => s_RemotePresentationBatch;
        public static SimulationPipelineProductContract SelectedRemoteBodyBatch => s_SelectedRemoteBodyBatch;
        public static IReadOnlyList<SimulationPipelineProductContract> All => s_All;

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
    }

    public readonly struct OwnerCanonicalInputBatch
    {
        public OwnerCanonicalInputBatch(ActorId actorId, ulong sourceTick, ulong inputSequence, SimulationInput input)
        {
            if (!actorId.IsValid || sourceTick == 0 || inputSequence == 0)
                throw new ArgumentException("Owner canonical input identity is incomplete.");
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (input.Sequence != inputSequence || input.TickSource.SourceTick != sourceTick)
                throw new ArgumentException("Owner canonical input metadata does not match the canonical input.", nameof(input));
            ActorId = actorId;
            SourceTick = sourceTick;
            InputSequence = inputSequence;
        }

        internal OwnerCanonicalInputBatch(in OwnerCanonicalInputBatch original, SimulationInput input)
        {
            ActorId = original.ActorId;
            SourceTick = original.SourceTick;
            InputSequence = original.InputSequence;
            Input = input;
        }

        public ActorId ActorId { get; }
        public ulong SourceTick { get; }
        public ulong InputSequence { get; }
        public SimulationInput Input { get; }
        public bool IsValid => ActorId.IsValid && SourceTick != 0 && InputSequence != 0 && Input != null;
    }

    public readonly struct ServerAuthoritativeEventHorizon
    {
        public ServerAuthoritativeEventHorizon(ulong sequence, EventId eventId)
        {
            if ((sequence == 0) != !eventId.IsValid)
                throw new ArgumentException("Confirmed EventId horizon is incomplete.");
            Sequence = sequence;
            EventId = eventId;
        }

        public ulong Sequence { get; }
        public EventId EventId { get; }
        public bool IsEmpty => Sequence == 0;
        public static ServerAuthoritativeEventHorizon Empty => default;
    }

    public readonly struct AuthoritativeActorBaseline
    {
        readonly byte[] m_CharacterStateBytes;

        public AuthoritativeActorBaseline(
            ActorId actorId,
            SimulationTick authorityTick,
            SimulationNumericProfile numericProfile,
            TargetAbiVersion targetAbiVersion,
            string stateCodecIdentity,
            GameplayContentHash gameplayContentHash,
            OperationSetVersion operationSetVersion,
            byte[] ownedCharacterStateBytes,
            CharacterStateHash stateHash,
            WorldRevision worldRevision,
            SolverImplementationId solverId,
            string solverVersion,
            WorldCapability solverCapabilities,
            WorldBodyState body,
            ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon confirmedEventHorizon)
        {
            if (!actorId.IsValid || !authorityTick.IsValid || !numericProfile.IsValid ||
                !targetAbiVersion.Equals(numericProfile.AbiVersion) ||
                !CanonicalTextValue.Equals(stateCodecIdentity, Float32CharacterRuntimeStateCodec.CodecIdentity, StringComparison.Ordinal) ||
                !gameplayContentHash.IsValid ||
                !operationSetVersion.IsValid || !stateHash.IsValid || CanonicalTextValue.IsNullOrEmpty(worldRevision.Value) ||
                CanonicalTextValue.IsNullOrEmpty(solverId.Value) || solverCapabilities == WorldCapability.None || body.ActorId != actorId)
            {
                throw new ArgumentException("Authoritative baseline identity is incomplete.");
            }
            if (ownedCharacterStateBytes == null || ownedCharacterStateBytes.Length == 0)
                throw new ArgumentException("Authoritative baseline requires full Character state bytes.", nameof(ownedCharacterStateBytes));
            ActorId = actorId;
            AuthorityTick = authorityTick;
            NumericProfile = numericProfile;
            TargetAbiVersion = targetAbiVersion;
            StateCodecIdentity = stateCodecIdentity;
            GameplayContentHash = gameplayContentHash;
            OperationSetVersion = operationSetVersion;
            m_CharacterStateBytes = ownedCharacterStateBytes;
            StateHash = stateHash;
            WorldRevision = worldRevision;
            SolverId = solverId;
            SolverVersion = CanonicalTextValue.IsNullOrWhiteSpace(solverVersion)
                ? throw new ArgumentException("Solver version is required.", nameof(solverVersion))
                : solverVersion.Trim();
            SolverCapabilities = solverCapabilities;
            Body = body;
            BodyHash = ServerAuthoritativeCanonicalCodec.ComputeBodyHash(body);
            ConfirmedInputSequence = confirmedInputSequence;
            ConfirmedEventHorizon = confirmedEventHorizon;
        }

        public ActorId ActorId { get; }
        public SimulationTick AuthorityTick { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public string StateCodecIdentity { get; }
        public GameplayContentHash GameplayContentHash { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public ReadOnlyMemory<byte> CharacterStateBytes => m_CharacterStateBytes;
        public CharacterStateHash StateHash { get; }
        public WorldRevision WorldRevision { get; }
        public SolverImplementationId SolverId { get; }
        public string SolverVersion { get; }
        public WorldCapability SolverCapabilities { get; }
        public WorldBodyState Body { get; }
        public StableHash BodyHash { get; }
        public ulong ConfirmedInputSequence { get; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; }
        public bool IsValid => ActorId.IsValid && AuthorityTick.IsValid && m_CharacterStateBytes != null;
        internal byte[] StateBuffer => m_CharacterStateBytes;
    }

    public enum PredictionCorrectionDecisionKind : byte
    {
        NoCorrection = 1,
        RestoreReplay = 2,
        HardRecovery = 3
    }

    public enum PredictionCorrectionReason : byte
    {
        StateAndBodyMatch = 1,
        CharacterStateMismatch = 2,
        BodyPositionMismatch = 3,
        BodyYawMismatch = 4,
        HistoryUnavailable = 5,
        ReplayLimitExceeded = 6,
        NoAuthoritativeBaseline = 7
    }

    public readonly struct PredictionCorrectionDecision
    {
        public PredictionCorrectionDecision(
            PredictionCorrectionDecisionKind kind,
            PredictionCorrectionReason reason,
            SimulationTick baselineTick,
            SimulationTick restoreTick,
            SimulationTick replayStart,
            SimulationTick replayEnd,
            Float32Scalar positionError,
            Float32Scalar yawError)
        {
            if (kind is not (PredictionCorrectionDecisionKind.NoCorrection or
                    PredictionCorrectionDecisionKind.RestoreReplay or PredictionCorrectionDecisionKind.HardRecovery) ||
                reason is not (PredictionCorrectionReason.StateAndBodyMatch or PredictionCorrectionReason.CharacterStateMismatch or
                    PredictionCorrectionReason.BodyPositionMismatch or PredictionCorrectionReason.BodyYawMismatch or
                    PredictionCorrectionReason.HistoryUnavailable or PredictionCorrectionReason.ReplayLimitExceeded or
                    PredictionCorrectionReason.NoAuthoritativeBaseline) || !baselineTick.IsValid)
            {
                throw new ArgumentException("Prediction correction decision is incomplete.");
            }
            bool restores = kind != PredictionCorrectionDecisionKind.NoCorrection;
            if (restores != restoreTick.IsValid)
                throw new ArgumentException("Prediction correction restore identity does not match its kind.");
            if (replayStart.IsValid != replayEnd.IsValid ||
                replayStart.IsValid && replayEnd.CompareTo(replayStart) < 0)
            {
                throw new ArgumentException("Prediction correction replay range is invalid.");
            }
            Kind = kind;
            Reason = reason;
            BaselineTick = baselineTick;
            RestoreTick = restoreTick;
            ReplayStart = replayStart;
            ReplayEnd = replayEnd;
            PositionError = positionError;
            YawError = yawError;
        }

        public PredictionCorrectionDecisionKind Kind { get; }
        public PredictionCorrectionReason Reason { get; }
        public SimulationTick BaselineTick { get; }
        public SimulationTick RestoreTick { get; }
        public SimulationTick ReplayStart { get; }
        public SimulationTick ReplayEnd { get; }
        public Float32Scalar PositionError { get; }
        public Float32Scalar YawError { get; }
        public bool IsValid => Kind != default && Reason != default && BaselineTick.IsValid;
    }

    public readonly struct AcceptedAuthorityInput
    {
        public AcceptedAuthorityInput(ActorId actorId, ulong inputSequence, SimulationInput input)
        {
            if (!actorId.IsValid || inputSequence == 0)
                throw new ArgumentException("Accepted authority input identity is incomplete.");
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (input.Sequence != inputSequence)
                throw new ArgumentException("Accepted authority input sequence does not match its canonical input.", nameof(input));
            ActorId = actorId;
            InputSequence = inputSequence;
        }

        public ActorId ActorId { get; }
        public ulong InputSequence { get; }
        public SimulationInput Input { get; }
        public bool IsValid => ActorId.IsValid && InputSequence != 0 && Input != null;
    }

    public readonly struct AcceptedAuthorityInputBatch
    {
        readonly AcceptedAuthorityInput[] m_Inputs;

        public AcceptedAuthorityInputBatch(SimulationTick authorityTick, AcceptedAuthorityInput[] inputs)
        {
            if (!authorityTick.IsValid)
                throw new ArgumentException("Authority input Tick is invalid.", nameof(authorityTick));
            if (inputs == null || inputs.Length == 0)
                throw new ArgumentException("Authority input batch cannot be empty.", nameof(inputs));
            AuthorityTick = authorityTick;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (!inputs[i].IsValid)
                    throw new ArgumentException("Authority input batch contains an invalid accepted input.", nameof(inputs));
            }
            Array.Sort(inputs, CompareByActor);
            for (int i = 1; i < inputs.Length; i++)
            {
                if (inputs[i - 1].ActorId == inputs[i].ActorId)
                    throw new ArgumentException("Authority input batch contains a duplicate ActorId.", nameof(inputs));
            }
            m_Inputs = inputs;
        }

        public SimulationTick AuthorityTick { get; }
        public IReadOnlyList<AcceptedAuthorityInput> Inputs => m_Inputs;
        public bool IsValid => AuthorityTick.IsValid && m_Inputs != null;

        static int CompareByActor(AcceptedAuthorityInput left, AcceptedAuthorityInput right) =>
            left.ActorId.CompareTo(right.ActorId);
    }

    public readonly struct ServerAuthoritativeReliableEvent
    {
        public const int MaximumTextReferences = GameplayFact.MaximumTextReferences;

        public ServerAuthoritativeReliableEvent(GameplayFact fact)
        {
            GameplayFact = fact;
            PresentationCommand = default;
            IsGameplay = true;
        }

        public ServerAuthoritativeReliableEvent(PresentationCommand command)
        {
            GameplayFact = default;
            PresentationCommand = command;
            IsGameplay = false;
        }

        public GameplayFact GameplayFact { get; }
        public PresentationCommand PresentationCommand { get; }
        public bool IsGameplay { get; }
        public SimulationEventHeader Header => IsGameplay ? GameplayFact.Header : PresentationCommand.Header;

        public void RetainText()
        {
            if (IsGameplay)
                GameplayFact.Retain();
            else
                PresentationCommand.Retain();
        }

        public void ReleaseText()
        {
            if (IsGameplay)
                GameplayFact.Release();
            else
                PresentationCommand.Release();
        }
    }

    public readonly struct RemotePresentationBatch
    {
        static readonly Comparison<CharacterBodySample> s_CompareByTick =
            (left, right) => left.Tick.CompareTo(right.Tick);

        readonly IReadOnlyList<CharacterBodySample> m_BodySamples;
        readonly IReadOnlyList<PresentationCommand> m_SampleCommands;
        readonly IReadOnlyList<ServerAuthoritativeReliableEvent> m_ReliableEvents;

        public RemotePresentationBatch(
            ActorId actorId,
            CharacterBodySample[] bodySamples,
            PresentationCommand[] sampleCommands,
            ServerAuthoritativeReliableEvent[] reliableEvents,
            bool resetBodyStream)
        {
            if (!actorId.IsValid || bodySamples == null || sampleCommands == null || reliableEvents == null)
                throw new ArgumentException("Remote presentation ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            ResetBodyStream = resetBodyStream;
            Array.Sort(bodySamples, s_CompareByTick);
            for (int i = 0; i < bodySamples.Length; i++)
            {
                if (bodySamples[i].ActorId != actorId || i > 0 && bodySamples[i - 1].Tick == bodySamples[i].Tick)
                    throw new ArgumentException("Remote body stream contains an invalid Actor or duplicate Tick.", nameof(bodySamples));
            }

            for (int i = 0; i < sampleCommands.Length; i++)
            {
                if (sampleCommands[i].Header.ActorId != actorId ||
                    sampleCommands[i].Kind != PresentationCommandKind.SampleProducer)
                    throw new ArgumentException("Remote presentation sample stream contains an invalid Actor or command kind.", nameof(sampleCommands));
            }

            Array.Sort(sampleCommands, ServerAuthoritativeProductOrder.CompareCommands);
            for (int i = 1; i < sampleCommands.Length; i++)
            {
                if (CanonicalTextValue.Equals(sampleCommands[i - 1].ProducerId, sampleCommands[i].ProducerId, StringComparison.Ordinal) &&
                    sampleCommands[i - 1].ProducerGeneration == sampleCommands[i].ProducerGeneration &&
                    sampleCommands[i - 1].SourceActionInstanceId != sampleCommands[i].SourceActionInstanceId)
                    throw new ArgumentException(
                        "Remote presentation playback changed its source Action instance.",
                        nameof(sampleCommands));
            }

            Array.Sort(reliableEvents, ServerAuthoritativeProductOrder.CompareEvents);
            for (int i = 0; i < reliableEvents.Length; i++)
            {
                if (reliableEvents[i].Header.ActorId != actorId ||
                    i > 0 && reliableEvents[i - 1].Header.EventId.Equals(reliableEvents[i].Header.EventId))
                    throw new ArgumentException("Remote reliable events contain an invalid Actor or duplicate EventId.", nameof(reliableEvents));
            }
            m_BodySamples = bodySamples;
            m_SampleCommands = sampleCommands;
            m_ReliableEvents = reliableEvents;
        }

        RemotePresentationBatch(
            ActorId actorId,
            IReadOnlyList<CharacterBodySample> bodySamples,
            IReadOnlyList<PresentationCommand> sampleCommands,
            IReadOnlyList<ServerAuthoritativeReliableEvent> reliableEvents,
            bool resetBodyStream)
        {
            ActorId = actorId;
            ResetBodyStream = resetBodyStream;
            m_BodySamples = bodySamples;
            m_SampleCommands = sampleCommands;
            m_ReliableEvents = reliableEvents;
        }

        internal static RemotePresentationBatch FromPreparedOutputs(
            ActorId actorId,
            IReadOnlyList<CharacterBodySample> bodySamples,
            IReadOnlyList<PresentationCommand> sampleCommands,
            IReadOnlyList<ServerAuthoritativeReliableEvent> reliableEvents,
            bool resetBodyStream) =>
            new RemotePresentationBatch(actorId, bodySamples, sampleCommands, reliableEvents, resetBodyStream);

        public ActorId ActorId { get; }
        public bool ResetBodyStream { get; }
        public IReadOnlyList<CharacterBodySample> BodySamples => m_BodySamples;
        public IReadOnlyList<PresentationCommand> SampleCommands => m_SampleCommands;
        public IReadOnlyList<ServerAuthoritativeReliableEvent> ReliableEvents => m_ReliableEvents;
        public bool IsValid => ActorId.IsValid && m_BodySamples != null && m_SampleCommands != null && m_ReliableEvents != null;

        public void RetainText()
        {
            ActorId.Retain();
            for (int i = 0; i < m_BodySamples.Count; i++)
            {
                m_BodySamples[i].ActorId.Retain();
                m_BodySamples[i].BeforeBody.ActorId.Retain();
                m_BodySamples[i].FinalBody.ActorId.Retain();
            }
            for (int i = 0; i < m_SampleCommands.Count; i++)
                m_SampleCommands[i].Retain();
            for (int i = 0; i < m_ReliableEvents.Count; i++)
                m_ReliableEvents[i].RetainText();
        }

        public void ReleaseText()
        {
            ActorId.Release();
            for (int i = 0; i < m_BodySamples.Count; i++)
            {
                m_BodySamples[i].ActorId.Release();
                m_BodySamples[i].BeforeBody.ActorId.Release();
                m_BodySamples[i].FinalBody.ActorId.Release();
            }
            for (int i = 0; i < m_SampleCommands.Count; i++)
                m_SampleCommands[i].Release();
            for (int i = 0; i < m_ReliableEvents.Count; i++)
                m_ReliableEvents[i].ReleaseText();
        }
    }

    public readonly struct SelectedRemoteBodyBatch
    {
        static readonly Comparison<CharacterBodySample> s_CompareByTick =
            (left, right) => left.Tick.CompareTo(right.Tick);

        readonly CharacterBodySample[] m_BodySamples;

        public SelectedRemoteBodyBatch(
            ActorId actorId,
            SimulationTick tick,
            CharacterBodySample[] bodySamples,
            bool resetStream)
        {
            if (!actorId.IsValid || !tick.IsValid || bodySamples == null)
                throw new ArgumentException("Selected remote body ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            Tick = tick;
            ResetStream = resetStream;
            m_BodySamples = bodySamples;
            Array.Sort(m_BodySamples, s_CompareByTick);
            for (int i = 0; i < m_BodySamples.Length; i++)
            {
                if (m_BodySamples[i].ActorId != actorId || i > 0 && m_BodySamples[i - 1].Tick == m_BodySamples[i].Tick)
                    throw new ArgumentException("Selected remote body stream is invalid.", nameof(bodySamples));
            }
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public IReadOnlyList<CharacterBodySample> BodySamples => m_BodySamples;
        public bool ResetStream { get; }
        public bool IsValid => ActorId.IsValid && Tick.IsValid && m_BodySamples != null;
    }

    public readonly struct AuthoritativeInputAck
    {
        public AuthoritativeInputAck(
            ActorId actorId,
            SimulationTick authorityTick,
            ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon confirmedEventHorizon)
        {
            if (!actorId.IsValid || !authorityTick.IsValid)
                throw new ArgumentException("Authority input ack identity is incomplete.");
            ActorId = actorId;
            AuthorityTick = authorityTick;
            ConfirmedInputSequence = confirmedInputSequence;
            ConfirmedEventHorizon = confirmedEventHorizon;
        }

        public ActorId ActorId { get; }
        public SimulationTick AuthorityTick { get; }
        public ulong ConfirmedInputSequence { get; }
        public ServerAuthoritativeEventHorizon ConfirmedEventHorizon { get; }
        public bool IsValid => ActorId.IsValid && AuthorityTick.IsValid;
    }

    public readonly struct AuthoritativeObservationBatch
    {
        static readonly Comparison<AuthoritativeActorBaseline> s_CompareBaselines =
            (left, right) => left.ActorId.CompareTo(right.ActorId);
        static readonly Comparison<RemotePresentationBatch> s_CompareRemote =
            (left, right) => left.ActorId.CompareTo(right.ActorId);

        readonly AuthoritativeActorBaseline[] m_Baselines;
        readonly RemotePresentationBatch[] m_RemotePresentation;

        public AuthoritativeObservationBatch(
            ulong receiveSequence,
            ulong authorityTickEstimate,
            AuthoritativeInputAck ownerAck,
            AuthoritativeActorBaseline[] baselines,
            RemotePresentationBatch[] remotePresentation)
        {
            if (receiveSequence == 0 || baselines == null || remotePresentation == null)
                throw new ArgumentOutOfRangeException(nameof(receiveSequence));
            ReceiveSequence = receiveSequence;
            AuthorityTickEstimate = authorityTickEstimate;
            OwnerAck = ownerAck;
            m_Baselines = baselines;
            m_RemotePresentation = remotePresentation;
            Array.Sort(m_Baselines, s_CompareBaselines);
            Array.Sort(m_RemotePresentation, s_CompareRemote);
            for (int i = 0; i < m_Baselines.Length; i++)
            {
                if (!m_Baselines[i].IsValid ||
                    i > 0 && m_Baselines[i - 1].ActorId == m_Baselines[i].ActorId)
                    throw new ArgumentException("Observation baseline contains a missing or duplicate ActorId.", nameof(baselines));
            }
            for (int i = 0; i < m_RemotePresentation.Length; i++)
            {
                if (!m_RemotePresentation[i].IsValid ||
                    i > 0 && m_RemotePresentation[i - 1].ActorId == m_RemotePresentation[i].ActorId)
                    throw new ArgumentException("Observation remote presentation contains a missing or duplicate ActorId.", nameof(remotePresentation));
            }
        }

        public ulong ReceiveSequence { get; }
        public ulong AuthorityTickEstimate { get; }
        public AuthoritativeInputAck OwnerAck { get; }
        public IReadOnlyList<AuthoritativeActorBaseline> Baselines => m_Baselines;
        public IReadOnlyList<RemotePresentationBatch> RemotePresentation => m_RemotePresentation;
        public bool IsValid => ReceiveSequence != 0 && m_Baselines != null && m_RemotePresentation != null;

        public void RetainText()
        {
            OwnerAck.ActorId.Retain();
            for (int i = 0; i < m_Baselines.Length; i++)
            {
                m_Baselines[i].ActorId.Retain();
                m_Baselines[i].Body.ActorId.Retain();
            }
            for (int i = 0; i < m_RemotePresentation.Length; i++)
                m_RemotePresentation[i].RetainText();
        }

        public void ReleaseText()
        {
            OwnerAck.ActorId.Release();
            for (int i = 0; i < m_Baselines.Length; i++)
            {
                m_Baselines[i].ActorId.Release();
                m_Baselines[i].Body.ActorId.Release();
            }
            for (int i = 0; i < m_RemotePresentation.Length; i++)
                m_RemotePresentation[i].ReleaseText();
        }
    }

    public readonly struct AuthorityReplicationBatch
    {
        readonly IReadOnlyList<AuthoritativeInputAck> m_Acks;
        readonly IReadOnlyList<AuthoritativeActorBaseline> m_Baselines;
        readonly IReadOnlyList<RemotePresentationBatch> m_RemotePresentation;

        public AuthorityReplicationBatch(
            SimulationTick authorityTick,
            AuthoritativeInputAck[] acks,
            AuthoritativeActorBaseline[] baselines,
            RemotePresentationBatch[] remotePresentation)
        {
            if (!authorityTick.IsValid || acks == null || baselines == null || remotePresentation == null)
                throw new ArgumentException("Authority replication Tick is invalid.", nameof(authorityTick));
            AuthorityTick = authorityTick;
            Array.Sort(acks, CompareAcks);
            Array.Sort(baselines, CompareBaselines);
            Array.Sort(remotePresentation, CompareRemote);
            if (acks.Length == 0 || remotePresentation.Length == 0)
                throw new ArgumentException("Authority replication requires Actor acks and presentation streams.", nameof(acks));
            for (int i = 0; i < acks.Length; i++)
            {
                if (!acks[i].IsValid ||
                    i > 0 && acks[i - 1].ActorId == acks[i].ActorId ||
                    acks[i].AuthorityTick != authorityTick)
                    throw new ArgumentException("Authority replication ack set is invalid or does not match the batch.", nameof(acks));
            }
            for (int i = 0; i < baselines.Length; i++)
            {
                if (!baselines[i].IsValid ||
                    i > 0 && baselines[i - 1].ActorId == baselines[i].ActorId ||
                    baselines[i].AuthorityTick != authorityTick)
                    throw new ArgumentException("Authority replication baseline set is invalid or does not match the batch.", nameof(baselines));
            }
            for (int i = 0; i < remotePresentation.Length; i++)
            {
                if (!remotePresentation[i].IsValid ||
                    i > 0 && remotePresentation[i - 1].ActorId == remotePresentation[i].ActorId)
                    throw new ArgumentException("Authority replication presentation set is invalid.", nameof(remotePresentation));
            }
            m_Acks = acks;
            m_Baselines = baselines;
            m_RemotePresentation = remotePresentation;
        }

        AuthorityReplicationBatch(
            SimulationTick authorityTick,
            IReadOnlyList<AuthoritativeInputAck> acks,
            IReadOnlyList<AuthoritativeActorBaseline> baselines,
            IReadOnlyList<RemotePresentationBatch> remotePresentation)
        {
            AuthorityTick = authorityTick;
            m_Acks = acks;
            m_Baselines = baselines;
            m_RemotePresentation = remotePresentation;
        }

        internal static AuthorityReplicationBatch FromPreparedOutputs(
            SimulationTick authorityTick,
            IReadOnlyList<AuthoritativeInputAck> acks,
            IReadOnlyList<AuthoritativeActorBaseline> baselines,
            IReadOnlyList<RemotePresentationBatch> remotePresentation) =>
            new AuthorityReplicationBatch(authorityTick, acks, baselines, remotePresentation);

        static int CompareAcks(AuthoritativeInputAck left, AuthoritativeInputAck right) =>
            left.ActorId.CompareTo(right.ActorId);

        static int CompareBaselines(AuthoritativeActorBaseline left, AuthoritativeActorBaseline right) =>
            left.ActorId.CompareTo(right.ActorId);

        static int CompareRemote(RemotePresentationBatch left, RemotePresentationBatch right) =>
            left.ActorId.CompareTo(right.ActorId);

        public SimulationTick AuthorityTick { get; }
        public IReadOnlyList<AuthoritativeInputAck> Acks => m_Acks;
        public IReadOnlyList<AuthoritativeActorBaseline> Baselines => m_Baselines;
        public IReadOnlyList<RemotePresentationBatch> RemotePresentation => m_RemotePresentation;
        public bool IsValid => AuthorityTick.IsValid && m_Acks != null && m_Baselines != null && m_RemotePresentation != null;
    }

    static class ServerAuthoritativeProductOrder
    {
        public static int CompareEvents(ServerAuthoritativeReliableEvent left, ServerAuthoritativeReliableEvent right)
        {
            int tick = left.Header.Tick.CompareTo(right.Header.Tick);
            if (tick != 0)
                return tick;
            int sequence = left.Header.Sequence.CompareTo(right.Header.Sequence);
            return sequence != 0 ? sequence : left.Header.EventId.CompareTo(right.Header.EventId);
        }

        public static int CompareCommands(PresentationCommand left, PresentationCommand right)
        {
            int producer = CanonicalTextValue.CompareOrdinal(left.ProducerId, right.ProducerId);
            if (producer != 0)
                return producer;
            int generation = left.ProducerGeneration.CompareTo(right.ProducerGeneration);
            if (generation != 0)
                return generation;
            return left.Header.Sequence.CompareTo(right.Header.Sequence);
        }
    }
}
