using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public abstract class ConvexShape
    {
        protected internal FP collisionMargin;

        public FP CollisionMargin => collisionMargin;
        public FP MaximumRadius { get; protected set; }

        public abstract void GetLocalExtremePointWithoutMargin(ref FPVector3 direction, out FPVector3 extremePoint);

        public void GetExtremePointWithoutMargin(FPVector3 direction, ref RigidTransform shapeTransform, out FPVector3 extremePoint)
        {
            direction = FPQuaternion.Conjugate(shapeTransform.Orientation) * direction;
            GetLocalExtremePointWithoutMargin(ref direction, out extremePoint);
            extremePoint = shapeTransform.Orientation * extremePoint + shapeTransform.Position;
        }

        public void GetExtremePoint(FPVector3 direction, ref RigidTransform shapeTransform, out FPVector3 extremePoint)
        {
            GetExtremePointWithoutMargin(direction, ref shapeTransform, out extremePoint);
            extremePoint += FPVector3.Normalize(direction) * collisionMargin;
        }
    }
}
