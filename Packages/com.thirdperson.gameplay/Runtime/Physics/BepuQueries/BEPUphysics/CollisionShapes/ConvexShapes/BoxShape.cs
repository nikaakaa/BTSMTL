using Photon.Deterministic;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public sealed class BoxShape : ConvexShape
    {
        readonly FPVector3 m_HalfExtents;

        public BoxShape(FP width, FP height, FP length)
        {
            m_HalfExtents = new FPVector3(width / 2, height / 2, length / 2);
            FPVector3.Normalize(m_HalfExtents, out FP radius);
            MaximumRadius = radius;
        }

        public override void GetLocalExtremePointWithoutMargin(ref FPVector3 direction, out FPVector3 extremePoint)
        {
            extremePoint = new FPVector3(
                FPMath.SignZero(direction.X) * m_HalfExtents.X,
                FPMath.SignZero(direction.Y) * m_HalfExtents.Y,
                FPMath.SignZero(direction.Z) * m_HalfExtents.Z);
        }
    }
}
