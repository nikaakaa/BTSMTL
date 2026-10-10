using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUutilities;
using FixMath.NET;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms.GJK
{
    public static class GJKToolbox
    {
        public const int MaximumGJKIterations = 64;
        public const int HighGJKIterations = 8;

        public static bool GetClosestPoints(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform transformA, ref RigidTransform transformB,
                                            out Vector3 closestPointA, out Vector3 closestPointB, out bool iterationLimitReached)
        {
            //The cached simplex stores locations that are local to the shapes.  A fairly decent initial state is between the centroids of the objects.
            //In local space, the centroids are at the origins.

            RigidTransform localtransformB;
            MinkowskiToolbox.GetLocalTransform(ref transformA, ref transformB, out localtransformB);

            var simplex = new CachedSimplex {State = SimplexState.Point};
                // new CachedSimplex(shapeA, shapeB, ref localtransformB);
            bool toReturn = GetClosestPoints(shapeA, shapeB, ref localtransformB, ref simplex, out closestPointA, out closestPointB, out iterationLimitReached);

            RigidTransform.Transform(ref closestPointA, ref transformA, out closestPointA);
            RigidTransform.Transform(ref closestPointB, ref transformA, out closestPointB);
            return toReturn;
        }

        private static bool GetClosestPoints(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform localTransformB,
                                             ref CachedSimplex cachedSimplex, out Vector3 localClosestPointA, out Vector3 localClosestPointB, out bool iterationLimitReached)
        {

            iterationLimitReached = false;
            var simplex = new PairSimplex(ref cachedSimplex, ref localTransformB);

            Vector3 closestPoint;
            int count = 0;
            while (true)
            {
                if (simplex.GetPointClosestToOrigin(out closestPoint) || //Also reduces the simplex and computes barycentric coordinates if necessary.
                    closestPoint.LengthSquared() <= Toolbox.Epsilon * simplex.errorTolerance)
                {
                    //Intersecting.
                    simplex.GetClosestPoints(out localClosestPointA, out localClosestPointB);

                    simplex.UpdateCachedSimplex(ref cachedSimplex);
                    return true;
                }

                if (++count > MaximumGJKIterations)
                {
                    iterationLimitReached = true;
                    break;
                } //Must break BEFORE a new vertex is added if we're over the iteration limit.  This guarantees final simplex is not a tetrahedron.

                if (simplex.GetNewSimplexPoint(shapeA, shapeB, count, ref closestPoint))
                {
                    //No progress towards origin, not intersecting.
                    break;
                }

            }
            //Compute closest points from the contributing simplexes and barycentric coordinates
            simplex.GetClosestPoints(out localClosestPointA, out localClosestPointB);
            //simplex.VerifyContributions();
            //if (Vector3.Distance(localClosestPointA - localClosestPointB, closestPoint) > .00001f)
            //    Debug.WriteLine("break.");
            simplex.UpdateCachedSimplex(ref cachedSimplex);
            return false;
        }
    }
}
