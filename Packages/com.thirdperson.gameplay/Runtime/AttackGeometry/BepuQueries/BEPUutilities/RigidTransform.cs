using Photon.Deterministic;

namespace BEPUutilities
{
    public struct RigidTransform
    {
        public RigidTransform(FPVector3 position, FPQuaternion orientation)
        {
            Position = position;
            Orientation = orientation;
        }

        public FPVector3 Position;
        public FPQuaternion Orientation;

        public static void Transform(ref FPVector3 position, ref RigidTransform transform, out FPVector3 result)
        {
            result = transform.Orientation * position + transform.Position;
        }
    }
}
