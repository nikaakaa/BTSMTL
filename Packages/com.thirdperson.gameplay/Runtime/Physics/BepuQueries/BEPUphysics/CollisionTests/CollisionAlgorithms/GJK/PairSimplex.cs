using BEPUphysics.CollisionShapes.ConvexShapes;

using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms.GJK
{

    public enum SimplexState : byte
    {
        Empty,
        Point,
        Segment,
        Triangle,
        Tetrahedron
    }

    public struct ContributingShapeSimplex
    {
        public FPVector3 A;
        public FPVector3 B;
        public FPVector3 C;
        public FPVector3 D;
    }

    public struct PairSimplex
    {

        public static readonly FP ProgressionEpsilon = FP.SmallestNonZero;

        public static readonly FP DistanceConvergenceEpsilon = FP.SmallestNonZero;

        public ContributingShapeSimplex SimplexA;

        public ContributingShapeSimplex SimplexB;

        public FPVector3 A;
        public FPVector3 B;
        public FPVector3 C;
        public FPVector3 D;
        public SimplexState State;

        public FP U;

        public FP V;

        public FP W;

        public RigidTransform LocalTransformB;

        public PairSimplex(ref RigidTransform localTransformB)
        {

            previousDistanceToClosest = FP.MaxValue;
            errorTolerance = FP._0;
            LocalTransformB = localTransformB;

            State = SimplexState.Point;
            SimplexA = new ContributingShapeSimplex();
            SimplexB = new ContributingShapeSimplex {A = localTransformB.Position};

            A = -localTransformB.Position;
            B = new FPVector3();
            C = new FPVector3();
            D = new FPVector3();
            U = FP._0;
            V = FP._0;
            W = FP._0;
        }

        public bool GetPointClosestToOrigin(out FPVector3 point)
        {

            switch (State)
            {

                case SimplexState.Point:
                    point = A;
                    U = FP._1;
                    break;
                case SimplexState.Segment:
                    GetPointOnSegmentClosestToOrigin(out point);
                    break;
                case SimplexState.Triangle:
                    GetPointOnTriangleClosestToOrigin(out point);
                    break;
                case SimplexState.Tetrahedron:
                    return GetPointOnTetrahedronClosestToOrigin(out point);
                default:
                    point = FPVector3.Zero;
                    break;

            }
            return false;
        }

        public void GetPointOnSegmentClosestToOrigin(out FPVector3 point)
        {
            FPVector3 segmentDisplacement;
            segmentDisplacement = B - A;

            FP dotA;
            dotA = FPVector3.Dot(segmentDisplacement, A);
            if (dotA > FP._0)
            {

                State = SimplexState.Point;

                U = FP._1;
                point = A;
                return;
            }
            FP dotB;
            dotB = FPVector3.Dot(segmentDisplacement, B);
            if (dotB > FP._0)
            {

                U = dotB / segmentDisplacement.SqrMagnitude;
                V = FP._1 - U;
                point = segmentDisplacement * V;
                point = point + A;
                return;

            }

            A = B;
            SimplexA.A = SimplexA.B;
            SimplexB.A = SimplexB.B;
            State = SimplexState.Point;

            U = FP._1;
            point = A;

        }

        public void GetPointOnTriangleClosestToOrigin(out FPVector3 point)
        {
            FPVector3 ab, ac;
            ab = B - A;
            ac = C - A;

            FP AdotAB, AdotAC;
            AdotAB = FPVector3.Dot(ab, A);
            AdotAC = FPVector3.Dot(ac, A);
            AdotAB = -AdotAB;
            AdotAC = -AdotAC;
            if (AdotAC <= FP._0 && AdotAB <= FP._0)
            {

                State = SimplexState.Point;
                U = FP._1;
                point = A;
                return;
            }

            FP BdotAB, BdotAC;
            BdotAB = FPVector3.Dot(ab, B);
            BdotAC = FPVector3.Dot(ac, B);
            BdotAB = -BdotAB;
            BdotAC = -BdotAC;
            if (BdotAB >= FP._0 && BdotAC <= BdotAB)
            {

                State = SimplexState.Point;
                A = B;
                U = FP._1;
                SimplexA.A = SimplexA.B;
                SimplexB.A = SimplexB.B;
                point = B;
                return;
            }

            FP vc = AdotAB * BdotAC - BdotAB * AdotAC;
            if (vc <= FP._0 && AdotAB > FP._0 && BdotAB < FP._0)
            {
                State = SimplexState.Segment;
                V = AdotAB / (AdotAB - BdotAB);
                U = FP._1 - V;

                point = ab * V;
                point = point + A;
                return;
            }

            FP CdotAB, CdotAC;
            CdotAB = FPVector3.Dot(ab, C);
            CdotAC = FPVector3.Dot(ac, C);
            CdotAB = -CdotAB;
            CdotAC = -CdotAC;
            if (CdotAC >= FP._0 && CdotAB <= CdotAC)
            {

                State = SimplexState.Point;
                A = C;
                SimplexA.A = SimplexA.C;
                SimplexB.A = SimplexB.C;
                U = FP._1;
                point = A;
                return;
            }

            FP vb = CdotAB * AdotAC - AdotAB * CdotAC;
            if (vb <= FP._0 && AdotAC > FP._0 && CdotAC < FP._0)
            {

                State = SimplexState.Segment;
                B = C;
                SimplexA.B = SimplexA.C;
                SimplexB.B = SimplexB.C;
                V = AdotAC / (AdotAC - CdotAC);
                U = FP._1 - V;
                point = ac * V;
                point = point + A;
                return;
            }

            FP va = BdotAB * CdotAC - CdotAB * BdotAC;
            FP d3d4;
            FP d6d5;
            if (va <= FP._0 && (d3d4 = BdotAC - BdotAB) > FP._0 && (d6d5 = CdotAB - CdotAC) > FP._0)
            {

                State = SimplexState.Segment;
                A = C;
                SimplexA.A = SimplexA.C;
                SimplexB.A = SimplexB.C;
                U = d3d4 / (d3d4 + d6d5);
                V = FP._1 - U;

                FPVector3 bc;
                bc = C - B;
                point = bc * U;
                point = point + B;
                return;
            }

            FP denom = va + vb + vc;
            V = vb / denom;
            W = vc / denom;
            U = FP._1 - V - W;
            point = ab * V;
            FPVector3 acw;
            acw = ac * W;
            point = A + point;
            point = point + acw;

        }

        public bool GetPointOnTetrahedronClosestToOrigin(out FPVector3 point)
        {

            PairSimplex minimumSimplex = new PairSimplex();
            point = new FPVector3();
            FP minimumDistance = FP.MaxValue;

            PairSimplex candidate;
            FP candidateDistance;
            FPVector3 candidatePoint;
            if (TryTetrahedronTriangle(ref A, ref C, ref D,
                                       ref SimplexA.A, ref SimplexA.C, ref SimplexA.D,
                                       ref SimplexB.A, ref SimplexB.C, ref SimplexB.D,
                                       errorTolerance,
                                       ref B, out candidate, out candidatePoint))
            {
                point = candidatePoint;
                minimumSimplex = candidate;
                minimumDistance = candidatePoint.SqrMagnitude;
            }

            if (TryTetrahedronTriangle(ref B, ref D, ref C,
                                       ref SimplexA.B, ref SimplexA.D, ref SimplexA.C,
                                       ref SimplexB.B, ref SimplexB.D, ref SimplexB.C,
                                       errorTolerance,
                                       ref A, out candidate, out candidatePoint) &&
                (candidateDistance = candidatePoint.SqrMagnitude) < minimumDistance)
            {
                point = candidatePoint;
                minimumSimplex = candidate;
                minimumDistance = candidateDistance;
            }

            if (TryTetrahedronTriangle(ref A, ref D, ref B,
                                       ref SimplexA.A, ref SimplexA.D, ref SimplexA.B,
                                       ref SimplexB.A, ref SimplexB.D, ref SimplexB.B,
                                       errorTolerance,
                                       ref C, out candidate, out candidatePoint) &&
                (candidateDistance = candidatePoint.SqrMagnitude) < minimumDistance)
            {
                point = candidatePoint;
                minimumSimplex = candidate;
                minimumDistance = candidateDistance;
            }

            if (TryTetrahedronTriangle(ref A, ref B, ref C,
                                       ref SimplexA.A, ref SimplexA.B, ref SimplexA.C,
                                       ref SimplexB.A, ref SimplexB.B, ref SimplexB.C,
                                       errorTolerance,
                                       ref D, out candidate, out candidatePoint) &&
                (candidateDistance = candidatePoint.SqrMagnitude) < minimumDistance)
            {
                point = candidatePoint;
                minimumSimplex = candidate;
                minimumDistance = candidateDistance;
            }

            if (minimumDistance < FP.MaxValue)
            {
                minimumSimplex.LocalTransformB = LocalTransformB;
                minimumSimplex.previousDistanceToClosest = previousDistanceToClosest;
                minimumSimplex.errorTolerance = errorTolerance;
                this = minimumSimplex;
                return false;
            }
            FPVector3 ab = B - A;
            FPVector3 ac = C - A;
            FPVector3 ad = D - A;
            FPVector3 origin = -A;
            FPVector3 cross = FPVector3.Cross(ac, ad);
            FP volume = FPVector3.Dot(ab, cross);
            V = FPVector3.Dot(origin, cross) / volume;
            W = FPVector3.Dot(ab, FPVector3.Cross(origin, ad)) / volume;
            FP fourth = FPVector3.Dot(ab, FPVector3.Cross(ac, origin)) / volume;
            U = FP._1 - V - W - fourth;
            return true;
        }

        private static bool TryTetrahedronTriangle(ref FPVector3 A, ref FPVector3 B, ref FPVector3 C,
                                                   ref FPVector3 A1, ref FPVector3 B1, ref FPVector3 C1,
                                                   ref FPVector3 A2, ref FPVector3 B2, ref FPVector3 C2,
                                                   FP errorTolerance,
                                                   ref FPVector3 otherPoint, out PairSimplex simplex, out FPVector3 point)
        {

            simplex = new PairSimplex();
            point = new FPVector3();

            FPVector3 ab, ac;
            ab = B - A;
            ac = C - A;
            FPVector3 normal;
            normal = FPVector3.Cross(ab, ac);
            FP AdotN, ADdotN;
            FPVector3 AD;
            AD = otherPoint - A;
            AdotN = FPVector3.Dot(A, normal);
            ADdotN = FPVector3.Dot(AD, normal);

            if (AdotN * ADdotN >= -Toolbox.Epsilon * errorTolerance)
            {

                FP AdotAB, AdotAC;
                AdotAB = FPVector3.Dot(ab, A);
                AdotAC = FPVector3.Dot(ac, A);
                AdotAB = -AdotAB;
                AdotAC = -AdotAC;
                if (AdotAC <= FP._0 && AdotAB <= FP._0)
                {

                    simplex.State = SimplexState.Point;
                    simplex.A = A;
                    simplex.U = FP._1;
                    simplex.SimplexA.A = A1;
                    simplex.SimplexB.A = A2;
                    point = A;
                    return true;
                }

                FP BdotAB, BdotAC;
                BdotAB = FPVector3.Dot(ab, B);
                BdotAC = FPVector3.Dot(ac, B);
                BdotAB = -BdotAB;
                BdotAC = -BdotAC;
                if (BdotAB >= FP._0 && BdotAC <= BdotAB)
                {

                    simplex.State = SimplexState.Point;
                    simplex.A = B;
                    simplex.U = FP._1;
                    simplex.SimplexA.A = B1;
                    simplex.SimplexB.A = B2;
                    point = B;
                    return true;
                }

                FP vc = AdotAB * BdotAC - BdotAB * AdotAC;
                if (vc <= FP._0 && AdotAB > FP._0 && BdotAB < FP._0)
                {
                    simplex.State = SimplexState.Segment;
                    simplex.V = AdotAB / (AdotAB - BdotAB);
                    simplex.U = FP._1 - simplex.V;
                    simplex.A = A;
                    simplex.B = B;
                    simplex.SimplexA.A = A1;
                    simplex.SimplexB.A = A2;
                    simplex.SimplexA.B = B1;
                    simplex.SimplexB.B = B2;

                    point = ab * simplex.V;
                    point = point + A;
                    return true;
                }

                FP CdotAB, CdotAC;
                CdotAB = FPVector3.Dot(ab, C);
                CdotAC = FPVector3.Dot(ac, C);
                CdotAB = -CdotAB;
                CdotAC = -CdotAC;
                if (CdotAC >= FP._0 && CdotAB <= CdotAC)
                {

                    simplex.State = SimplexState.Point;
                    simplex.A = C;
                    simplex.U = FP._1;
                    simplex.SimplexA.A = C1;
                    simplex.SimplexB.A = C2;
                    point = C;
                    return true;
                }

                FP vb = CdotAB * AdotAC - AdotAB * CdotAC;
                if (vb <= FP._0 && AdotAC > FP._0 && CdotAC < FP._0)
                {
                    simplex.State = SimplexState.Segment;
                    simplex.A = A;
                    simplex.B = C;
                    simplex.SimplexA.A = A1;
                    simplex.SimplexA.B = C1;
                    simplex.SimplexB.A = A2;
                    simplex.SimplexB.B = C2;
                    simplex.V = AdotAC / (AdotAC - CdotAC);
                    simplex.U = FP._1 - simplex.V;
                    point = ac * simplex.V;
                    point = point + A;
                    return true;
                }

                FP va = BdotAB * CdotAC - CdotAB * BdotAC;
                FP d3d4;
                FP d6d5;
                if (va <= FP._0 && (d3d4 = BdotAC - BdotAB) > FP._0 && (d6d5 = CdotAB - CdotAC) > FP._0)
                {
                    simplex.State = SimplexState.Segment;
                    simplex.A = B;
                    simplex.B = C;
                    simplex.SimplexA.A = B1;
                    simplex.SimplexA.B = C1;
                    simplex.SimplexB.A = B2;
                    simplex.SimplexB.B = C2;
                    simplex.V = d3d4 / (d3d4 + d6d5);
                    simplex.U = FP._1 - simplex.V;

                    FPVector3 bc;
                    bc = C - B;
                    point = bc * simplex.V;
                    point = point + B;
                    return true;
                }

                simplex.A = A;
                simplex.B = B;
                simplex.C = C;
                simplex.SimplexA.A = A1;
                simplex.SimplexA.B = B1;
                simplex.SimplexA.C = C1;
                simplex.SimplexB.A = A2;
                simplex.SimplexB.B = B2;
                simplex.SimplexB.C = C2;
                simplex.State = SimplexState.Triangle;
                FP denom = va + vb + vc;
                simplex.W = vc / denom;
                simplex.V = vb / denom;
                simplex.U = FP._1 - simplex.V - simplex.W;
                point = ab * simplex.V;
                FPVector3 acw;
                acw = ac * simplex.W;
                point = A + point;
                point = point + acw;
                return true;
            }
            return false;
        }

        internal FP errorTolerance;

        FP previousDistanceToClosest;

        public bool GetNewSimplexPoint(ConvexShape shapeA, ConvexShape shapeB, int iterationCount, ref FPVector3 closestPoint)
        {
            FPVector3 negativeDirection;
            negativeDirection = -closestPoint;
            FPVector3 sa, sb;
            shapeA.GetLocalExtremePointWithoutMargin(ref negativeDirection, out sa);
            shapeB.GetExtremePointWithoutMargin(closestPoint, ref LocalTransformB, out sb);
            FPVector3 S;
            S = sa - sb;

            FP dotS;
            dotS = FPVector3.Dot(S, negativeDirection);
            FP distanceToClosest = closestPoint.SqrMagnitude;

            FP progression = dotS + distanceToClosest;

            if (iterationCount > GJKToolbox.HighGJKIterations && distanceToClosest - previousDistanceToClosest < DistanceConvergenceEpsilon * errorTolerance)
                return true;
            if (distanceToClosest < previousDistanceToClosest)
                previousDistanceToClosest = distanceToClosest;

            switch (State)
            {
                case SimplexState.Point:
                    if (progression <= (errorTolerance = FPMath.Max(A.SqrMagnitude, S.SqrMagnitude)) * ProgressionEpsilon)
                        return true;

                    State = SimplexState.Segment;
                    B = S;
                    SimplexA.B = sa;
                    SimplexB.B = sb;
                    return false;
                case SimplexState.Segment:
                    if (progression <= (errorTolerance = FPMath.Max(FPMath.Max(A.SqrMagnitude, B.SqrMagnitude), S.SqrMagnitude)) * ProgressionEpsilon)
                        return true;

                    State = SimplexState.Triangle;
                    C = S;
                    SimplexA.C = sa;
                    SimplexB.C = sb;
                    return false;
                case SimplexState.Triangle:
                    if (progression <= (errorTolerance = FPMath.Max(FPMath.Max(A.SqrMagnitude, B.SqrMagnitude), FPMath.Max(C.SqrMagnitude, S.SqrMagnitude))) * ProgressionEpsilon)
                        return true;

                    State = SimplexState.Tetrahedron;
                    D = S;
                    SimplexA.D = sa;
                    SimplexB.D = sb;
                    return false;
            }
            return false;
        }

        public void GetClosestPoints(out FPVector3 closestPointA, out FPVector3 closestPointB)
        {

            switch (State)
            {
                case SimplexState.Point:
                    closestPointA = SimplexA.A;
                    closestPointB = SimplexB.A;
                    return;
                case SimplexState.Segment:
                    FPVector3 temp;
                    closestPointA = SimplexA.A * U;
                    temp = SimplexA.B * V;
                    closestPointA = closestPointA + temp;

                    closestPointB = SimplexB.A * U;
                    temp = SimplexB.B * V;
                    closestPointB = closestPointB + temp;
                    return;
                case SimplexState.Triangle:
                case SimplexState.Tetrahedron:
                    closestPointA = SimplexA.A * U;
                    temp = SimplexA.B * V;
                    closestPointA = closestPointA + temp;
                    temp = SimplexA.C * W;
                    closestPointA = closestPointA + temp;

                    closestPointB = SimplexB.A * U;
                    temp = SimplexB.B * V;
                    closestPointB = closestPointB + temp;
                    temp = SimplexB.C * W;
                    closestPointB = closestPointB + temp;
                    if (State == SimplexState.Tetrahedron)
                    {
                        FP fourth = FP._1 - U - V - W;
                        temp = SimplexA.D * fourth;
                        closestPointA = closestPointA + temp;
                        temp = SimplexB.D * fourth;
                        closestPointB = closestPointB + temp;
                    }
                    return;
            }
            closestPointA = FPVector3.Zero;
            closestPointB = FPVector3.Zero;

        }

    }
}
