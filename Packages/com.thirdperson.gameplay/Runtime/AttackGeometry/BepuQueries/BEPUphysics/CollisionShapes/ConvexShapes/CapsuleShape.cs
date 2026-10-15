using Photon.Deterministic;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public sealed class CapsuleShape : ConvexShape
    {
        readonly FP m_HalfLength;

        public CapsuleShape(FP length, FP radius)
        {
            m_HalfLength = length / 2;
            collisionMargin = radius;
            MaximumRadius = m_HalfLength + radius;
        }

        public override void GetLocalExtremePointWithoutMargin(ref FPVector3 direction, out FPVector3 extremePoint)
        {
            extremePoint = new FPVector3(FP._0, FPMath.SignZero(direction.Y) * m_HalfLength, FP._0);
        }
    }
}
