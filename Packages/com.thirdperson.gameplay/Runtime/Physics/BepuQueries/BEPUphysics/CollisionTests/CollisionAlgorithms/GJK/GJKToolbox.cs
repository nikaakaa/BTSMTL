using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms.GJK
{
    public static class GJKToolbox
    {
        public const int MaximumGJKIterations = 64;
        public const int HighGJKIterations = 8;

        public static bool GetClosestPoints(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform transformA, ref RigidTransform transformB,
                                            out FPVector3 closestPointA, out FPVector3 closestPointB, out bool iterationLimitReached)
        {

            RigidTransform localtransformB;
            MinkowskiToolbox.GetLocalTransform(ref transformA, ref transformB, out localtransformB);

            bool toReturn = GetClosestPoints(shapeA, shapeB, ref localtransformB, out closestPointA, out closestPointB, out iterationLimitReached);

            RigidTransform.Transform(ref closestPointA, ref transformA, out closestPointA);
            RigidTransform.Transform(ref closestPointB, ref transformA, out closestPointB);
            return toReturn;
        }

        private static bool GetClosestPoints(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform localTransformB,
                                             out FPVector3 localClosestPointA, out FPVector3 localClosestPointB, out bool iterationLimitReached)
        {

            iterationLimitReached = false;
            var simplex = new PairSimplex(ref localTransformB);

            FPVector3 closestPoint;
            int count = 0;
            while (true)
            {
                if (simplex.GetPointClosestToOrigin(out closestPoint) ||
                    Toolbox.Length(closestPoint) <= FP.SmallestNonZero)
                {

                    simplex.GetClosestPoints(out localClosestPointA, out localClosestPointB);

                    return true;
                }

                if (++count > MaximumGJKIterations)
                {
                    iterationLimitReached = true;
                    break;
                }

                if (simplex.GetNewSimplexPoint(shapeA, shapeB, count, ref closestPoint))
                {

                    break;
                }

            }

            simplex.GetClosestPoints(out localClosestPointA, out localClosestPointB);

            return false;
        }
    }
}
