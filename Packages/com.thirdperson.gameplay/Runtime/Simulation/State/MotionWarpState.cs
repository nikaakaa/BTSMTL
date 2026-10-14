using System;

namespace ThirdPersonSimulation
{
    [CanonicalContract]
    internal readonly partial struct MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> :
        IEquatable<MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>>
        where TScalar : unmanaged, IEquatable<TScalar>
        where TVector3 : unmanaged, IEquatable<TVector3>
        where TYaw : unmanaged, IEquatable<TYaw>
        where TAction : struct, IEquatable<TAction>, ICanonicalRecord<TAction>
        where TScalarCodec : struct, ICanonicalLeafCodec<TScalar>
        where TVector3Codec : struct, ICanonicalLeafCodec<TVector3>
        where TYawCodec : struct, ICanonicalLeafCodec<TYaw>
    {
        public MotionWarpState(
            bool active,
            bool initialized,
            ulong playbackGeneration,
            int cycle,
            TAction actionInstance,
            TVector3 startBodyPosition,
            TYaw startBodyYaw,
            TVector3 sourceWindowStartPosition,
            TScalar sourceWindowStartYaw,
            TVector3 resolvedTargetPosition,
            TYaw resolvedTargetYaw,
            ProgramMotionWarpLimitResult limitResult,
            TVector3 previousWarpedPosition,
            TYaw previousWarpedYaw,
            TScalar lastPositionProgress,
            TScalar lastYawProgress,
            OperationHandle sourceOperation,
            MotionWarpWindowDecision decision,
            bool hasTarget,
            TVector3 capturedTargetPosition,
            TYaw capturedTargetYaw)
        {
            Active = active;
            Initialized = initialized;
            PlaybackGeneration = playbackGeneration;
            Cycle = cycle;
            ActionInstance = actionInstance;
            StartBodyPosition = startBodyPosition;
            StartBodyYaw = startBodyYaw;
            SourceWindowStartPosition = sourceWindowStartPosition;
            SourceWindowStartYaw = sourceWindowStartYaw;
            ResolvedTargetPosition = resolvedTargetPosition;
            ResolvedTargetYaw = resolvedTargetYaw;
            LimitResult = limitResult;
            PreviousWarpedPosition = previousWarpedPosition;
            PreviousWarpedYaw = previousWarpedYaw;
            LastPositionProgress = lastPositionProgress;
            LastYawProgress = lastYawProgress;
            SourceOperation = sourceOperation;
            Decision = decision;
            HasTarget = hasTarget;
            CapturedTargetPosition = capturedTargetPosition;
            CapturedTargetYaw = capturedTargetYaw;
        }

        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);
        public bool Active { get; }
        public bool Initialized { get; }
        public ulong PlaybackGeneration { get; }
        public int Cycle { get; }
        public TAction ActionInstance { get; }
        public TVector3 StartBodyPosition { get; }
        public TYaw StartBodyYaw { get; }
        public TVector3 SourceWindowStartPosition { get; }
        public TScalar SourceWindowStartYaw { get; }
        public TVector3 ResolvedTargetPosition { get; }
        public TYaw ResolvedTargetYaw { get; }
        public ProgramMotionWarpLimitResult LimitResult { get; }
        public TVector3 PreviousWarpedPosition { get; }
        public TYaw PreviousWarpedYaw { get; }
        public TScalar LastPositionProgress { get; }
        public TScalar LastYawProgress { get; }
        public OperationHandle SourceOperation { get; }
        public MotionWarpWindowDecision Decision { get; }
        public bool HasTarget { get; }
        public TVector3 CapturedTargetPosition { get; }
        public TYaw CapturedTargetYaw { get; }
        public bool Equals(MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> other) =>
            Active == other.Active &&
            Initialized == other.Initialized &&
            PlaybackGeneration == other.PlaybackGeneration &&
            Cycle == other.Cycle &&
            ActionInstance.Equals(other.ActionInstance) &&
            StartBodyPosition.Equals(other.StartBodyPosition) &&
            StartBodyYaw.Equals(other.StartBodyYaw) &&
            SourceWindowStartPosition.Equals(other.SourceWindowStartPosition) &&
            SourceWindowStartYaw.Equals(other.SourceWindowStartYaw) &&
            ResolvedTargetPosition.Equals(other.ResolvedTargetPosition) &&
            ResolvedTargetYaw.Equals(other.ResolvedTargetYaw) &&
            LimitResult == other.LimitResult &&
            PreviousWarpedPosition.Equals(other.PreviousWarpedPosition) &&
            PreviousWarpedYaw.Equals(other.PreviousWarpedYaw) &&
            LastPositionProgress.Equals(other.LastPositionProgress) &&
            LastYawProgress.Equals(other.LastYawProgress) &&
            SourceOperation.Equals(other.SourceOperation) &&
            Decision == other.Decision &&
            HasTarget == other.HasTarget &&
            CapturedTargetPosition.Equals(other.CapturedTargetPosition) &&
            CapturedTargetYaw.Equals(other.CapturedTargetYaw);

        public MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> WithDecision(MotionWarpWindowDecision decision) =>
            new MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>(Active, Initialized, PlaybackGeneration, Cycle, ActionInstance,
                StartBodyPosition, StartBodyYaw, SourceWindowStartPosition, SourceWindowStartYaw,
                ResolvedTargetPosition, ResolvedTargetYaw, LimitResult, PreviousWarpedPosition,
                PreviousWarpedYaw, LastPositionProgress, LastYawProgress, SourceOperation,
                decision, HasTarget, CapturedTargetPosition, CapturedTargetYaw);

        public MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> WithProgress(
            TVector3 previousWarpedPosition,
            TYaw previousWarpedYaw,
            TScalar lastPositionProgress,
            TScalar lastYawProgress) =>
            new MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>(
                Active,
                Initialized,
                PlaybackGeneration,
                Cycle,
                ActionInstance,
                StartBodyPosition,
                StartBodyYaw,
                SourceWindowStartPosition,
                SourceWindowStartYaw,
                ResolvedTargetPosition,
                ResolvedTargetYaw,
                LimitResult,
                previousWarpedPosition,
                previousWarpedYaw,
                lastPositionProgress,
                lastYawProgress,
                SourceOperation,
                Decision,
                HasTarget,
                CapturedTargetPosition,
                CapturedTargetYaw);
    }

}
