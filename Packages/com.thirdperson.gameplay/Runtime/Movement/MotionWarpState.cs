using System;

namespace ThirdPersonSimulation
{
    [ValueStorage(Construct = true), ValueOperations(Resources = false)]
    public readonly partial struct MotionWarpState<TScalar, TVector3, TYaw, TAction> :
        IEquatable<MotionWarpState<TScalar, TVector3, TYaw, TAction>>
        where TScalar : unmanaged, IEquatable<TScalar>
        where TVector3 : unmanaged, IEquatable<TVector3>
        where TYaw : unmanaged, IEquatable<TYaw>
        where TAction : unmanaged, IEquatable<TAction>
    {

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

        [ValueUpdate]
        public partial MotionWarpState<TScalar, TVector3, TYaw, TAction> WithDecision(MotionWarpWindowDecision decision);

        [ValueUpdate]
        public partial MotionWarpState<TScalar, TVector3, TYaw, TAction> WithProgress(
            TVector3 previousWarpedPosition,
            TYaw previousWarpedYaw,
            TScalar lastPositionProgress,
            TScalar lastYawProgress);
    }

}
