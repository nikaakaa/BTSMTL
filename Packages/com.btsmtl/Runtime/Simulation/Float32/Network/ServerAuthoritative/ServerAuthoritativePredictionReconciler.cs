using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePredictionReconciler
    {
        readonly Float32CharacterRuntime m_CharacterRuntime;
        readonly ServerAuthoritativePipelineCompatibilityIdentity m_Compatibility;
        readonly ServerAuthoritativeWorldIdentity m_AuthorityWorld;
        WorldSimulationState m_DecodedWorld;

        public ServerAuthoritativePredictionReconciler(
            Float32CharacterRuntime characterRuntime,
            ServerAuthoritativePipelineCompatibilityIdentity compatibility,
            ServerAuthoritativeWorldIdentity authorityWorld)
        {
            m_CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            m_Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
            m_AuthorityWorld = authorityWorld ?? throw new ArgumentNullException(nameof(authorityWorld));
            if (!m_CharacterRuntime.GameplayContentHash.Equals(m_Compatibility.GameplayContentHash) ||
                !m_CharacterRuntime.StateSchemaHash.Equals(m_Compatibility.StateSchemaHash) ||
                !m_CharacterRuntime.OperationSetVersion.Equals(m_Compatibility.OperationSetVersion) ||
                m_CharacterRuntime.TickRate != m_Compatibility.TickRate)
            {
                throw new InvalidOperationException("Prediction state Character Runtime does not match the locked ServerAuthoritative compatibility identity.");
            }
            if ((m_AuthorityWorld.SolverCapabilities & m_Compatibility.AuthoritySolverRequiredCapabilities) !=
                m_Compatibility.AuthoritySolverRequiredCapabilities)
            {
                throw new InvalidOperationException("Authority World does not satisfy the locked ServerAuthoritative Solver capability contract.");
            }
        }

        internal void PrepareWorldReadStorage(int maximumPayloadBytes) =>
            m_DecodedWorld = new WorldSimulationState(m_CharacterRuntime.Roster.Count, maximumPayloadBytes);

        public void ValidateBaselineIdentity(
            AuthoritativeActorBaseline baseline,
            ServerAuthoritativePredictionHistoryRecord firstHistory)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            SimulationActorBinding actor = RequireActor(baseline.ActorId);
            GameplayContentHash actorContentHash = new GameplayContentHash(actor.GameplayContentHash);
            if (baseline.NumericProfile != m_CharacterRuntime.NumericProfile ||
                !baseline.TargetAbiVersion.Equals(m_CharacterRuntime.NumericProfile.AbiVersion) ||
                !CanonicalTextValue.Equals(baseline.StateCodecIdentity, Float32CharacterRuntimeStateCodec.CodecIdentity, StringComparison.Ordinal) ||
                !baseline.GameplayContentHash.Equals(actorContentHash) ||
                !baseline.OperationSetVersion.Equals(m_Compatibility.OperationSetVersion) ||
                !baseline.SolverId.Equals(m_AuthorityWorld.SolverId) ||
                !CanonicalTextValue.Equals(baseline.SolverVersion, m_AuthorityWorld.SolverVersion, StringComparison.Ordinal) ||
                baseline.SolverCapabilities != m_AuthorityWorld.SolverCapabilities ||
                !baseline.WorldRevision.Equals(m_AuthorityWorld.WorldRevision))
            {
                throw new InvalidOperationException("Authority baseline does not match the locked Character Runtime, operation-set, or Solver identity.");
            }
            Float32CharacterRuntimeState state = Float32CharacterRuntimeStateCodec.Read(
                baseline.CharacterStateBytes,
                actor, out CharacterStateHash hash);
            if (!hash.Equals(baseline.StateHash))
                throw new InvalidDataException("Authority baseline Character runtime state hash is invalid.");
            if (firstHistory == null)
                return;
            if (firstHistory.Input.ActorId != baseline.ActorId ||
                !firstHistory.Character.GameplayContentHash.Equals(baseline.GameplayContentHash) ||
                !firstHistory.World.WorldRevision.Equals(baseline.WorldRevision))
            {
                throw new InvalidOperationException("Authority baseline identity does not match Prediction history.");
            }
        }

        public PredictionCorrectionDecision Decide(
            AuthoritativeActorBaseline baseline,
            ServerAuthoritativePredictionHistoryRecord local,
            int replayCount,
            ServerAuthoritativeModelPolicy policy)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (local == null)
            {
                if (policy.HardRecoveryPolicy == ServerAuthoritativeHardRecoveryPolicy.FailSession)
                    throw new InvalidOperationException($"Prediction history does not cover authority Tick '{baseline.AuthorityTick}'.");
                return new PredictionCorrectionDecision(
                    PredictionCorrectionDecisionKind.HardRecovery,
                    PredictionCorrectionReason.HistoryUnavailable,
                    baseline.AuthorityTick,
                    baseline.AuthorityTick,
                    default,
                    default,
                    Float32Scalar.Zero,
                    Float32Scalar.Zero);
            }
            WorldBodyState localBody;
            try
            {
                localBody = local.World.DecodeWorldState(m_DecodedWorld).Bodies[0];
            }
            finally
            {
                m_DecodedWorld.ClearPrepared();
            }
            Float32Scalar positionError = PositionError(localBody, baseline.Body);
            Float32Scalar yawError = YawError(localBody, baseline.Body);
            bool stateMatches = local.Character.StateHash.Equals(baseline.StateHash);
            bool positionMatches = positionError.Value <= policy.BodyPositionTolerance;
            bool yawMatches = yawError.Value <= policy.BodyYawToleranceDegrees;
            if (stateMatches && positionMatches && yawMatches)
            {
                return new PredictionCorrectionDecision(
                    PredictionCorrectionDecisionKind.NoCorrection,
                    PredictionCorrectionReason.StateAndBodyMatch,
                    baseline.AuthorityTick,
                    default,
                    default,
                    default,
                    positionError,
                    yawError);
            }
            if (replayCount > policy.MaximumReplayTicksPerOuterTick)
            {
                return new PredictionCorrectionDecision(
                    PredictionCorrectionDecisionKind.HardRecovery,
                    PredictionCorrectionReason.ReplayLimitExceeded,
                    baseline.AuthorityTick,
                    baseline.AuthorityTick,
                    default,
                    default,
                    positionError,
                    yawError);
            }
            PredictionCorrectionReason reason = !stateMatches
                ? PredictionCorrectionReason.CharacterStateMismatch
                : !positionMatches
                    ? PredictionCorrectionReason.BodyPositionMismatch
                    : PredictionCorrectionReason.BodyYawMismatch;
            return new PredictionCorrectionDecision(
                PredictionCorrectionDecisionKind.RestoreReplay,
                reason,
                baseline.AuthorityTick,
                baseline.AuthorityTick,
                replayCount == 0 ? default : new SimulationTick(checked(baseline.AuthorityTick.Value + 1)),
                replayCount == 0 ? default : new SimulationTick(checked(baseline.AuthorityTick.Value + (ulong)replayCount)),
                positionError,
                yawError);
        }

        public ServerAuthoritativePredictionRestorePlan BuildRestorePlan(
            ServerAuthoritativePredictionHistoryRecord local,
            AuthoritativeActorBaseline baseline,
            PredictionCorrectionDecision decision,
            SimulationPipelineIdentity pipeline,
            byte[] correctionState,
            byte[] historyState,
            byte[] journalState)
        {
            if (local == null || !baseline.IsValid || !decision.IsValid ||
                decision.Kind == PredictionCorrectionDecisionKind.NoCorrection)
            {
                throw new ArgumentException("Prediction restore requires a local frame, corrective baseline, and decision.");
            }
            SimulationWorldSnapshot world = MergeWorld(local.World, baseline);
            SimulationPipelineStateSnapshot pipelineSnapshot = MergePipeline(
                local.PipelineProjection,
                pipeline,
                baseline.AuthorityTick.Value,
                correctionState,
                historyState,
                journalState);
            var snapshot = new Float32SimulationSessionSnapshot(local.CompositionIdentity, world, pipelineSnapshot);
            string snapshotId = $"server-authoritative/{baseline.ActorId}/{baseline.AuthorityTick}/{snapshot.SnapshotHash}";
            var directive = new SimulationRestoreDirective(
                snapshotId,
                baseline.AuthorityTick,
                world.GameplayContentHash,
                pipeline.Hash,
                pipelineSnapshot.Backend.ComponentId,
                pipelineSnapshot.Backend.SemanticVersion,
                snapshot.SnapshotHash);
            return new ServerAuthoritativePredictionRestorePlan(snapshotId, snapshot, directive);
        }

        SimulationWorldSnapshot MergeWorld(SimulationWorldSnapshot local, AuthoritativeActorBaseline baseline)
        {
            SimulationActorSnapshot localActor = local.Actors[0];
            if (localActor.ActorId != baseline.ActorId ||
                !localActor.GameplayContentHash.Equals(baseline.GameplayContentHash))
            {
                throw new InvalidOperationException("Authority baseline cannot replace another Character Actor history frame.");
            }
            var actor = new SimulationActorSnapshot(
                baseline.ActorId,
                baseline.GameplayContentHash,
                RequireActor(baseline.ActorId).StateSchemaHash,
                baseline.StateHash,
                Float32CharacterRuntimeStateCodec.CodecIdentity,
                baseline.StateBuffer);
            try
            {
                WorldSimulationState localWorld = local.DecodeWorldState(m_DecodedWorld);
                var world = WorldSimulationState.FromOwnedState(
                    localWorld.NumericProfile,
                    localWorld.SolverId,
                    localWorld.SolverVersion,
                    localWorld.WorldRevision,
                    localWorld.PersistenceMode,
                    new[] { baseline.Body },
                    localWorld.SolverStatePayload.ToArray());
                return new SimulationWorldSnapshot(
                    local.NumericProfile,
                    local.GameplayContentHash,
                    m_CharacterRuntime.StateSchemaHash,
                    local.SolverId,
                    local.SolverVersion,
                    local.WorldRevision,
                    baseline.AuthorityTick,
                    new[] { actor },
                    WorldSimulationStateCodec.Write(world),
                    false);
            }
            finally
            {
                m_DecodedWorld.ClearPrepared();
            }
        }

        static SimulationPipelineStateSnapshot MergePipeline(
            SimulationPipelineStateSnapshot projection,
            SimulationPipelineIdentity pipeline,
            ulong tick,
            byte[] correctionState,
            byte[] historyState,
            byte[] journalState)
        {
            int participantCount = 0;
            for (int i = 0; i < projection.Participants.Count; i++)
            {
                if (!ServerAuthoritativePredictionPassIds.IsPredictionStatePass(projection.Participants[i].PassId))
                    participantCount++;
            }
            var participants = new SimulationPipelinePassStateSnapshot[participantCount + 3];
            int participantIndex = 0;
            for (int i = 0; i < projection.Participants.Count; i++)
            {
                SimulationPipelinePassStateSnapshot participant = projection.Participants[i];
                if (!ServerAuthoritativePredictionPassIds.IsPredictionStatePass(participant.PassId))
                    participants[participantIndex++] = participant;
            }
            participants[participantIndex++] = ServerAuthoritativePredictionStateSnapshot.Create(
                ServerAuthoritativePredictionPassIds.CorrectionSchedule,
                ServerAuthoritativePredictionPassIds.CorrectionStateOwner,
                ServerAuthoritativePredictionPassIds.CorrectionStateSchema,
                correctionState,
                3);
            participants[participantIndex++] = ServerAuthoritativePredictionStateSnapshot.Create(
                ServerAuthoritativePredictionPassIds.HistoryEgress,
                ServerAuthoritativePredictionPassIds.HistoryStateOwner,
                ServerAuthoritativePredictionPassIds.HistoryStateSchema,
                historyState,
                ServerAuthoritativePredictionPassIds.HistoryStateSchemaVersion);
            participants[participantIndex++] = ServerAuthoritativePredictionStateSnapshot.Create(
                ServerAuthoritativePredictionPassIds.OutputDisposition,
                ServerAuthoritativePredictionPassIds.JournalStateOwner,
                ServerAuthoritativePredictionPassIds.JournalStateSchema,
                journalState,
                ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            return SimulationPipelineStateSnapshot.FromOwnedParticipants(pipeline, projection.Backend, tick, participants);
        }

        static Float32Scalar PositionError(WorldBodyState left, WorldBodyState right)
        {
            float x = left.Position.X.Value - right.Position.X.Value;
            float y = left.Position.Y.Value - right.Position.Y.Value;
            float z = left.Position.Z.Value - right.Position.Z.Value;
            return Float32Scalar.FromSingle(MathF.Sqrt(x * x + y * y + z * z));
        }

        static Float32Scalar YawError(WorldBodyState left, WorldBodyState right)
        {
            float delta = MathF.Abs(left.Yaw.Degrees.Value - right.Yaw.Degrees.Value) % 360f;
            return Float32Scalar.FromSingle(delta > 180f ? 360f - delta : delta);
        }

        SimulationActorBinding RequireActor(ActorId actorId)
        {
            for (int i = 0; i < m_CharacterRuntime.Roster.Count; i++)
                if (m_CharacterRuntime.Roster[i].ActorId == actorId)
                    return m_CharacterRuntime.Roster[i];
            throw new InvalidOperationException($"Character Runtime has no Actor '{actorId}'.");
        }
    }

    internal sealed class ServerAuthoritativePredictionRestorePlan
    {
        public ServerAuthoritativePredictionRestorePlan(
            string snapshotId,
            Float32SimulationSessionSnapshot snapshot,
            SimulationRestoreDirective directive)
        {
            SnapshotId = snapshotId;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Directive = directive ?? throw new ArgumentNullException(nameof(directive));
        }

        public string SnapshotId { get; }
        public Float32SimulationSessionSnapshot Snapshot { get; }
        public SimulationRestoreDirective Directive { get; }
    }
}
