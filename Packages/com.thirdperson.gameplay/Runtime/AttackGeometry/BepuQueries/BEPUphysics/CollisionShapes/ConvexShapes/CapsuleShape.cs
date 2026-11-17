using BEPUutilities;
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

        public override void GetExtremePointWithoutMargin(FPVector3 direction, ref RigidTransform shapeTransform, out FPVector3 extremePoint)
        {
            if (shapeTransform.Orientation.X == FP._0 && shapeTransform.Orientation.Z == FP._0)
            {
                // 保留原旋转乘以1时的FP舍入与截位。
                FP localY = direction.Y * FP._1;
                extremePoint = new FPVector3(FP._0, FPMath.SignZero(localY) * m_HalfLength, FP._0);
                FP worldY = extremePoint.Y * FP._1;
                extremePoint = new FPVector3(FP._0, worldY, FP._0) + shapeTransform.Position;
                return;
            }
            base.GetExtremePointWithoutMargin(direction, ref shapeTransform, out extremePoint);
        }
    }
}
