using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationSpatialIdentity<TVector3, TYaw>
        where TVector3 : unmanaged
        where TYaw : unmanaged
    {
        public OrchestrationSpatialIdentity(OrchestrationIdentityValue identity, TVector3 position, TYaw yaw)
        {
            Identity = identity;
            Position = position;
            Yaw = yaw;
        }

        public OrchestrationIdentityValue Identity { get; }
        public TVector3 Position { get; }
        public TYaw Yaw { get; }
    }
}
