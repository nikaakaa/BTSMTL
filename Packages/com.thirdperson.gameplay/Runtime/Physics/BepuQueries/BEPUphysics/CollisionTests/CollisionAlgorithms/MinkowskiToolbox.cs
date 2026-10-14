using System;
using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms
{

    public static class MinkowskiToolbox
    {

        public static void GetLocalTransform(ref RigidTransform transformA, ref RigidTransform transformB,
                                             out RigidTransform localTransformB)
        {

            FPQuaternion conjugateOrientationA;
            conjugateOrientationA = FPQuaternion.Conjugate(transformA.Orientation);
            localTransformB.Orientation = conjugateOrientationA * transformB.Orientation;
            localTransformB.Position = transformB.Position - transformA.Position;
            localTransformB.Position = conjugateOrientationA * localTransformB.Position;
        }

        public static void GetLocalMinkowskiExtremePoint(ConvexShape shapeA, ConvexShape shapeB, ref FPVector3 direction, ref RigidTransform localTransformB, out FPVector3 extremePoint)
        {

            shapeA.GetLocalExtremePointWithoutMargin(ref direction, out extremePoint);
            FPVector3 v;
            FPVector3 negativeN;
            negativeN = -direction;
            shapeB.GetExtremePointWithoutMargin(negativeN, ref localTransformB, out v);
            extremePoint = extremePoint - v;

            ExpandMinkowskiSum(shapeA.collisionMargin, shapeB.collisionMargin, ref direction, out v);
            extremePoint = extremePoint + v;

        }

        public static void GetLocalMinkowskiExtremePoint(ConvexShape shapeA, ConvexShape shapeB, ref FPVector3 direction, ref RigidTransform localTransformB,
                                                 out FPVector3 extremePointA, out FPVector3 extremePointB, out FPVector3 extremePoint)
        {

            shapeA.GetLocalExtremePointWithoutMargin(ref direction, out extremePointA);
            FPVector3 v;
            v = -direction;
            shapeB.GetExtremePointWithoutMargin(v, ref localTransformB, out extremePointB);

            ExpandMinkowskiSum(shapeA.collisionMargin, shapeB.collisionMargin, direction, ref extremePointA, ref extremePointB);
            extremePoint = extremePointA - extremePointB;

        }

        public static void ExpandMinkowskiSum(FP marginA, FP marginB, ref FPVector3 direction, out FPVector3 contribution)
        {
            contribution = FPVector3.Normalize(direction) * (marginA + marginB);
        }

        public static void ExpandMinkowskiSum(FP marginA, FP marginB, FPVector3 direction, ref FPVector3 toExpandA, ref FPVector3 toExpandB)
        {
            FPVector3 normal = FPVector3.Normalize(direction);
            toExpandA += normal * marginA;
            toExpandB -= normal * marginB;
        }
    }
}
