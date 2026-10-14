using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms
{

    public static class MPRToolbox
    {

        public const int InnerIterationLimit = 15;

        public const int OuterIterationLimit = 15;

        private static readonly FP surfaceEpsilon = FP.SmallestNonZero;

        private static readonly FP rayCastSurfaceEpsilon = FP.SmallestNonZero;

        public static bool AreShapesOverlapping(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform transformA, ref RigidTransform transformB, out bool iterationLimitReached)
        {
            iterationLimitReached = false;
            RigidTransform localTransformB;
            MinkowskiToolbox.GetLocalTransform(ref transformA, ref transformB, out localTransformB);
            return AreLocalShapesOverlapping(shapeA, shapeB, ref localTransformB.Position, ref localTransformB, ref iterationLimitReached);

        }

        static bool AreLocalShapesOverlapping(ConvexShape shapeA, ConvexShape shapeB, ref FPVector3 originRay, ref RigidTransform localTransformB, ref bool iterationLimitReached)
        {

            if (originRay == FPVector3.Zero)
            {
                return true;
            }

            FPVector3 v0;
            v0 = -originRay;

            FPVector3 n = originRay;
            FPVector3 v1;
            MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v1);

            FPVector3 v2;
            n = FPVector3.Cross(v1, v0);
            if (n == FPVector3.Zero)
            {

                FP dot;
                dot = FPVector3.Dot(v1, originRay);
                if (dot < FP._0)
                {

                    return false;
                }

                return true;
            }
            MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v2);

            FPVector3 temp1, temp2;

            temp1 = v1 - v0;
            temp2 = v2 - v0;
            n = FPVector3.Cross(temp1, temp2);

            FPVector3 v3;
            int count = 0;
            while (true)
            {

                MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v3);

                if (count > MPRToolbox.OuterIterationLimit)
                {
                    iterationLimitReached = true;
                    break;
                }
                count++;

                temp1 = FPVector3.Cross(v1, v3);
                FP dot;
                dot = FPVector3.Dot(temp1, v0);
                if (dot < FP._0)
                {

                    v2 = v3;

                    temp1 = v1 - v0;
                    temp2 = v3 - v0;
                    n = FPVector3.Cross(temp1, temp2);
                    continue;
                }

                temp1 = FPVector3.Cross(v3, v2);
                dot = FPVector3.Dot(temp1, v0);
                if (dot < FP._0)
                {

                    v1 = v3;

                    temp1 = v2 - v0;
                    temp2 = v3 - v0;
                    n = FPVector3.Cross(temp1, temp2);
                    continue;
                }
                break;
            }

            while (true)
            {

                temp1 = v3 - v2;
                temp2 = v1 - v2;
                n = FPVector3.Cross(temp1, temp2);
                FP dot;
                dot = FPVector3.Dot(n, v1);
                if (dot >= FP._0)
                {
                    return true;
                }

                FPVector3 v4;
                MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v4);

                FP dot2;
                dot2 = FPVector3.Dot(v4, n);
                if (dot2 < FP._0)
                {

                    return false;
                }

                if (dot2 - dot < surfaceEpsilon || count > MPRToolbox.InnerIterationLimit)
                {
                    iterationLimitReached |= dot2 - dot >= surfaceEpsilon && count > MPRToolbox.InnerIterationLimit;
                    return false;
                }
                count++;

                temp1 = FPVector3.Cross(v4, v0);
                dot = FPVector3.Dot(v1, temp1);
                if (dot >= FP._0)
                {
                    dot = FPVector3.Dot(v2, temp1);
                    if (dot >= FP._0)
                    {
                        v1 = v4;
                    }
                    else
                    {
                        v3 = v4;
                    }
                }
                else
                {
                    dot = FPVector3.Dot(v3, temp1);
                    if (dot >= FP._0)
                    {
                        v2 = v4;
                    }
                    else
                    {
                        v1 = v4;
                    }
                }

            }

        }

        public static bool Sweep(ConvexShape shapeA, ConvexShape shapeB, ref FPVector3 sweepA, ref FPVector3 sweepB, ref RigidTransform transformA, ref RigidTransform transformB, out RayHit hit, out bool iterationLimitReached, out bool positionAvailable)
        {
            iterationLimitReached = false;
            positionAvailable = false;

            FPVector3 velocityWorld;

            velocityWorld = sweepA - sweepB;
            FPQuaternion conjugateOrientationA;
            conjugateOrientationA = FPQuaternion.Conjugate(transformA.Orientation);
            FPVector3 localDirection;
            localDirection = conjugateOrientationA * velocityWorld;

            RigidTransform localTransformB;
            MinkowskiToolbox.GetLocalTransform(ref transformA, ref transformB, out localTransformB);

            FPVector3 unitDirection = FPVector3.Normalize(localDirection, out FP rayLength);
            FP sweepLength = rayLength == FP._0 ? FP._0 :
                (FPVector3.Dot(localTransformB.Position, unitDirection) +
                    shapeA.MaximumRadius + shapeB.MaximumRadius) / rayLength;

            bool negativeLength = sweepLength < FP._0;
            if (negativeLength)
                sweepLength = FP._0;

            FPVector3 sweep;
            sweep = localDirection * sweepLength;

            if (!AreSweptShapesIntersecting(shapeA, shapeB, ref sweep, ref localTransformB, out hit.Location, ref iterationLimitReached))
            {

                hit.T = FP.MaxValue;
                hit.Normal = new FPVector3();
                hit.Location = new FPVector3();
                return false;
            }
            if (negativeLength)
            {

                hit.T = FP._0;
                hit.Normal = FPVector3.Normalize(localDirection);
                hit.Normal = transformA.Orientation * hit.Normal;

                hit.Location = transformA.Orientation * hit.Location;
                hit.Location = hit.Location + transformA.Position;
                hit.Location += sweepA * hit.T;
                return true;
            }

            if (LocalSweepCast(shapeA, shapeB, sweepLength, rayLength, ref localDirection, ref sweep, ref localTransformB, out hit, ref iterationLimitReached))
            {

                FPVector3 minkowskiRayHit = -hit.T * localDirection;

                GetLocalPosition(shapeA, shapeB, ref localTransformB, ref minkowskiRayHit, out hit.Location, ref iterationLimitReached, out positionAvailable);

                RigidTransform.Transform(ref hit.Location, ref transformA, out hit.Location);
                hit.Normal = transformA.Orientation * hit.Normal;

                FPVector3 temp;
                temp = sweepA * hit.T;
                hit.Location = temp + hit.Location;
                return true;
            }
            return false;

        }

        private static bool LocalSweepCast(ConvexShape shape, ConvexShape shapeB, FP sweepLength, FP rayLength, ref FPVector3 localDirection, ref FPVector3 sweep, ref RigidTransform localTransformB, out RayHit hit, ref bool iterationLimitReached)
        {

            FPVector3 n = localDirection;
            FPVector3 v1, v1A;
            GetSweptExtremePoint(shape, shapeB, ref localTransformB, ref sweep, ref n, out v1A, out v1);

            FPVector3 v2, v2A;
            n = FPVector3.Cross(localDirection, v1);
            hit.Location = new FPVector3();
            if (n == FPVector3.Zero)
            {

                if (rayLength > FP._0)
                    hit.Normal = FPVector3.Normalize(localDirection);
                else
                    hit.Normal = new FPVector3();

                FP rate;
                rate = FPVector3.Dot(hit.Normal, localDirection);
                FP distance;
                distance = FPVector3.Dot(hit.Normal, v1);
                if (rate > FP._0)
                    hit.T = sweepLength - distance / rate;
                else
                    hit.T = sweepLength;

                if (hit.T < FP._0)
                    hit.T = FP._0;

                return hit.T <= FP._1;

            }
            GetSweptExtremePoint(shape, shapeB, ref localTransformB, ref sweep, ref n, out v2A, out v2);

            FPVector3 temp1, temp2;

            n = FPVector3.Cross(v1, v2);

            FP dot;
            dot = FPVector3.Dot(n, localDirection);
            if (dot > FP._0)
            {

                n = -n;
                temp1 = v1;
                v1 = v2;
                v2 = temp1;
                temp1 = v1A;
                v1A = v2A;
                v2A = temp1;
            }

            FPVector3 v3, v3A;
            int count = 0;
            while (true)
            {

                GetSweptExtremePoint(shape, shapeB, ref localTransformB, ref sweep, ref n, out v3A, out v3);

                if (count > MPRToolbox.OuterIterationLimit)
                {
                    iterationLimitReached = true;

                    hit.T = FP.MaxValue;
                    hit.Normal = new FPVector3();
                    hit.Location = new FPVector3();
                    return false;
                }
                count++;

                temp1 = FPVector3.Cross(v1, v3);
                dot = FPVector3.Dot(temp1, localDirection);
                if (dot < FP._0)
                {

                    v2 = v3;
                    v2A = v3A;

                    n = FPVector3.Cross(v1, v3);
                    continue;
                }

                temp1 = FPVector3.Cross(v3, v2);
                dot = FPVector3.Dot(temp1, localDirection);
                if (dot < FP._0)
                {

                    v1 = v3;
                    v1A = v3A;

                    n = FPVector3.Cross(v2, v3);
                    continue;
                }
                break;
            }

            count = 0;
            while (true)
            {

                temp1 = v1 - v2;
                temp2 = v3 - v2;
                n = FPVector3.Cross(temp1, temp2);

                FPVector3 v4, v4A;
                GetSweptExtremePoint(shape, shapeB, ref localTransformB, ref sweep, ref n, out v4A, out v4);

                dot = FPVector3.Dot(n, v1);
                FP supportDot;
                supportDot = FPVector3.Dot(v4, n);

                if (supportDot - dot < rayCastSurfaceEpsilon || count > MPRToolbox.InnerIterationLimit)
                {
                    iterationLimitReached |= supportDot - dot >= rayCastSurfaceEpsilon && count > MPRToolbox.InnerIterationLimit;

                    if (n != FPVector3.Zero)
                    {
                        hit.Normal = FPVector3.Normalize(n);

                        dot = FPVector3.Dot(hit.Normal, localDirection);

                        supportDot = FPVector3.Dot(hit.Normal, v1);

                        hit.T = sweepLength - supportDot / dot;
                    }
                    else
                    {
                        hit.Normal = FPVector3.Normalize(localDirection);
                        hit.T = sweepLength;
                    }

                    if (hit.T < FP._0)
                        hit.T = FP._0;

                    return hit.T <= FP._1;
                }

                temp1 = FPVector3.Cross(v4, localDirection);
                dot = FPVector3.Dot(v1, temp1);
                if (dot >= FP._0)
                {
                    dot = FPVector3.Dot(v2, temp1);
                    if (dot >= FP._0)
                    {
                        v1 = v4;
                        v1A = v4A;
                    }
                    else
                    {
                        v3 = v4;
                        v3A = v4A;
                    }
                }
                else
                {
                    dot = FPVector3.Dot(v3, temp1);
                    if (dot >= FP._0)
                    {
                        v2 = v4;
                        v2A = v4A;
                    }
                    else
                    {
                        v1 = v4;
                        v1A = v4A;
                    }
                }

                count++;

            }
        }

        static void GetLocalPosition(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform localTransformB, ref FPVector3 minkowskiPosition, out FPVector3 position, ref bool iterationLimitReached, out bool positionAvailable)
        {
            positionAvailable = true;

            FPVector3 rayDirection;
            rayDirection = minkowskiPosition + localTransformB.Position;

            if (rayDirection == FPVector3.Zero)
            {

                positionAvailable = false;
                position = new FPVector3();

                return;
            }

            FPVector3 v0;
            v0 = -localTransformB.Position;

            FPVector3 n = rayDirection;
            FPVector3 v1;
            FPVector3 v1A, v1B;
            MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v1A, out v1B, out v1);

            FPVector3 v2;
            FPVector3 v2A, v2B;
            n = FPVector3.Cross(v1, v0);
            if (n == FPVector3.Zero)
            {

                FP dot = FPVector3.Dot(v1 - minkowskiPosition, rayDirection);

                FP dotv0 = FPVector3.Dot(v0 - minkowskiPosition, rayDirection);

                FP barycentricCoordinate = -dotv0 / (dot - dotv0);

                position = v1A * barycentricCoordinate;
                FPVector3 offset;
                offset = v1B - localTransformB.Position;
                return;
            }
            MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v2A, out v2B, out v2);

            FPVector3 temp;
            FPVector3 v0v1, v0v2;

            v0v1 = v1 - v0;
            v0v2 = v2 - v0;
            n = FPVector3.Cross(v0v1, v0v2);

            FPVector3 pointToV0;
            pointToV0 = v0 - minkowskiPosition;
            FPVector3 v3A, v3B, v3;
            int count = 0;
            while (true)
            {

                MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v3A, out v3B, out v3);

                if (count > MPRToolbox.OuterIterationLimit)
                {
                    iterationLimitReached = true;
                    break;
                }
                count++;

                FPVector3 v0v3;
                v0v1 = v1 - v0;
                v0v3 = v3 - v0;
                temp = FPVector3.Cross(v0v1, v0v3);
                FP dot;
                dot = FPVector3.Dot(temp, pointToV0);
                if (dot < FP._0)
                {

                    v2 = v3;
                    v2A = v3A;
                    v2B = v3B;

                    n = FPVector3.Cross(v0v1, v0v3);
                    continue;
                }

                v0v2 = v2 - v0;
                temp = FPVector3.Cross(v0v3, v0v2);
                dot = FPVector3.Dot(temp, pointToV0);
                if (dot < FP._0)
                {

                    v1 = v3;
                    v1A = v3A;
                    v1B = v3B;

                    n = FPVector3.Cross(v0v2, v0v3);
                    continue;
                }
                break;
            }

            while (true)
            {

                FPVector3 v2v3, v2v1;
                v2v3 = v3 - v2;
                v2v1 = v1 - v2;
                n = FPVector3.Cross(v2v3, v2v1);
                FP dot;
                FPVector3 pointToV1;
                pointToV1 = v1 - minkowskiPosition;
                dot = FPVector3.Dot(pointToV1, n);

                FPVector3 v4, v4A, v4B;
                MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref n, ref localTransformB, out v4A, out v4B, out v4);

                FP dot2;
                FPVector3 pointToV4;
                pointToV4 = v4 - minkowskiPosition;
                dot2 = FPVector3.Dot(pointToV4, n);

                if (dot2 - dot < rayCastSurfaceEpsilon || count > MPRToolbox.InnerIterationLimit)
                {
                    iterationLimitReached |= dot2 - dot >= rayCastSurfaceEpsilon && count > MPRToolbox.InnerIterationLimit;

                    FP weight1, weight2, weight3;
                    Toolbox.GetBarycentricCoordinates(ref minkowskiPosition, ref v1, ref v2, ref v3, out weight1, out weight2, out weight3);
                    position = v1A * weight1;
                    v2A = v2A * weight2;
                    v3A = v3A * weight3;
                    position = v2A + position;
                    position = v3A + position;

                    return;
                }
                count++;

                temp = FPVector3.Cross(pointToV4, pointToV0);
                dot = FPVector3.Dot(pointToV1, temp);
                if (dot >= FP._0)
                {
                    FPVector3 pointToV2;
                    pointToV2 = v2 - minkowskiPosition;
                    dot = FPVector3.Dot(pointToV2, temp);
                    if (dot >= FP._0)
                    {
                        v1 = v4;
                        v1A = v4A;
                        v1B = v4B;
                    }
                    else
                    {
                        v3 = v4;
                        v3A = v4A;
                        v3B = v4B;
                    }
                }
                else
                {
                    FPVector3 pointToV3;
                    pointToV3 = v3 - minkowskiPosition;
                    dot = FPVector3.Dot(pointToV3, temp);
                    if (dot >= FP._0)
                    {
                        v2 = v4;
                        v2A = v4A;
                        v2B = v4B;
                    }
                    else
                    {
                        v1 = v4;
                        v1A = v4A;
                        v1B = v4B;
                    }
                }

            }

        }

        static bool AreSweptShapesIntersecting(ConvexShape shapeA, ConvexShape shapeB, ref FPVector3 sweep, ref RigidTransform localTransformB, out FPVector3 position, ref bool iterationLimitReached)
        {

            if (localTransformB.Position == FPVector3.Zero)
            {
                position = new FPVector3();
                return true;
            }

            FPVector3 v0;
            v0 = -localTransformB.Position;

            FPVector3 n = localTransformB.Position;
            FPVector3 v1;
            FPVector3 v1A;

            GetSweptExtremePoint(shapeA, shapeB, ref localTransformB, ref sweep, ref n, out v1A, out v1);

            FPVector3 v2;
            FPVector3 v2A;
            n = FPVector3.Cross(v1, v0);
            if (n == FPVector3.Zero)
            {

                FP dot;
                dot = FPVector3.Dot(v1, localTransformB.Position);
                if (dot < FP._0)
                {

                    position = new FPVector3();
                    return false;
                }

                FP dotv0;

                dotv0 = FPVector3.Dot(v0, localTransformB.Position);
                FP barycentricCoordinate = -dotv0 / (dot - dotv0);

                position = v1A * barycentricCoordinate;
                return true;
            }

            GetSweptExtremePoint(shapeA, shapeB, ref localTransformB, ref sweep, ref n, out v2A, out v2);

            FPVector3 temp1, temp2;

            temp1 = v1 - v0;
            temp2 = v2 - v0;
            n = FPVector3.Cross(temp1, temp2);

            FPVector3 v3A, v3;
            int count = 0;
            while (true)
            {

                GetSweptExtremePoint(shapeA, shapeB, ref localTransformB, ref sweep, ref n, out v3A, out v3);

                if (count > MPRToolbox.OuterIterationLimit)
                {
                    iterationLimitReached = true;
                    break;
                }
                count++;

                temp1 = FPVector3.Cross(v1, v3);
                FP dot;
                dot = FPVector3.Dot(temp1, v0);
                if (dot < FP._0)
                {

                    v2 = v3;
                    v2A = v3A;

                    temp1 = v1 - v0;
                    temp2 = v3 - v0;
                    n = FPVector3.Cross(temp1, temp2);
                    continue;
                }

                temp1 = FPVector3.Cross(v3, v2);
                dot = FPVector3.Dot(temp1, v0);
                if (dot < FP._0)
                {

                    v1 = v3;
                    v1A = v3A;

                    temp1 = v2 - v0;
                    temp2 = v3 - v0;
                    n = FPVector3.Cross(temp1, temp2);
                    continue;
                }
                break;
            }

            while (true)
            {

                temp1 = v3 - v2;
                temp2 = v1 - v2;
                n = FPVector3.Cross(temp1, temp2);
                FP dot;
                dot = FPVector3.Dot(n, v1);
                if (dot >= FP._0)
                {
                    FPVector3 temp3;

                    temp1 = v1 - v0;
                    temp2 = v2 - v0;
                    temp3 = v3 - v0;

                    FPVector3 cross;
                    cross = FPVector3.Cross(temp1, temp2);
                    FP v0v1v2v3volume;
                    v0v1v2v3volume = FPVector3.Dot(cross, temp3);

                    cross = FPVector3.Cross(v1, v2);
                    FP ov1v2v3volume;
                    ov1v2v3volume = FPVector3.Dot(cross, v3);

                    cross = FPVector3.Cross(localTransformB.Position, temp2);
                    FP v0ov2v3volume;
                    v0ov2v3volume = FPVector3.Dot(cross, temp3);

                    cross = FPVector3.Cross(temp1, localTransformB.Position);
                    FP v0v1ov3volume;
                    v0v1ov3volume = FPVector3.Dot(cross, temp3);


                    FP v0Weight = ov1v2v3volume / v0v1v2v3volume;
                    FP v1Weight = v0ov2v3volume / v0v1v2v3volume;
                    FP v2Weight = v0v1ov3volume / v0v1v2v3volume;
                    FP v3Weight = FP._1 - v0Weight - v1Weight - v2Weight;
                    position = v1Weight * v1A + v2Weight * v2A + v3Weight * v3A;

                    return true;
                }

                FPVector3 v4, v4A;

                GetSweptExtremePoint(shapeA, shapeB, ref localTransformB, ref sweep, ref n, out v4A, out v4);

                FP dot2;
                dot2 = FPVector3.Dot(v4, n);
                if (dot2 < FP._0)
                {

                    position = new FPVector3();
                    return false;
                }

                if (dot2 - dot < surfaceEpsilon || count > MPRToolbox.InnerIterationLimit)
                {
                    iterationLimitReached |= dot2 - dot >= surfaceEpsilon && count > MPRToolbox.InnerIterationLimit;
                    position = new FPVector3();

                    return false;
                }
                count++;

                temp1 = FPVector3.Cross(v4, v0);
                dot = FPVector3.Dot(v1, temp1);
                if (dot >= FP._0)
                {
                    dot = FPVector3.Dot(v2, temp1);
                    if (dot >= FP._0)
                    {
                        v1 = v4;
                        v1A = v4A;
                    }
                    else
                    {
                        v3 = v4;
                        v3A = v4A;
                    }
                }
                else
                {
                    dot = FPVector3.Dot(v3, temp1);
                    if (dot >= FP._0)
                    {
                        v2 = v4;
                        v2A = v4A;
                    }
                    else
                    {
                        v1 = v4;
                        v1A = v4A;
                    }
                }

            }

        }

        static void GetSweptExtremePoint(ConvexShape shapeA, ConvexShape shapeB, ref RigidTransform localTransformB, ref FPVector3 sweep, ref FPVector3 extremePointDirection, out FPVector3 extremePointA, out FPVector3 extremePoint)
        {
            FPVector3 b;
            MinkowskiToolbox.GetLocalMinkowskiExtremePoint(shapeA, shapeB, ref extremePointDirection, ref localTransformB, out extremePointA, out b, out extremePoint);
            FP dot;
            dot = FPVector3.Dot(extremePointDirection, sweep);
            if (dot > FP._0)
            {
                extremePoint = extremePoint + sweep;
            }
        }

    }
}
