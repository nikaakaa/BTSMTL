using Photon.Deterministic;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public sealed class SphereShape : ConvexShape
    {
        public SphereShape(FP radius)
        {
            collisionMargin = radius;
            MaximumRadius = radius;
        }

        public override void GetLocalExtremePointWithoutMargin(ref FPVector3 direction, out FPVector3 extremePoint)
        {
            extremePoint = FPVector3.Zero;
        }
    }
}
