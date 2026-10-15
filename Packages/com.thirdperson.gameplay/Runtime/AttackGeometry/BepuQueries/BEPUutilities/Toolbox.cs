using Photon.Deterministic;

namespace BEPUutilities
{
    public static class Toolbox
    {
        public static readonly FP Epsilon = FP.SmallestNonZero;

        public static FP Length(FPVector3 value)
        {
            FPVector3.Normalize(value, out FP length);
            return length;
        }

        public static void GetBarycentricCoordinates(ref FPVector3 p, ref FPVector3 a, ref FPVector3 b, ref FPVector3 c, out FP aWeight, out FP bWeight, out FP cWeight)
        {
            FPVector3 ab, ac;
            ab = b - a;
            ac = c - a;
            FPVector3 triangleNormal;
            triangleNormal = FPVector3.Cross(ab, ac);
            FP x = triangleNormal.X < FP._0 ? -triangleNormal.X : triangleNormal.X;
            FP y = triangleNormal.Y < FP._0 ? -triangleNormal.Y : triangleNormal.Y;
            FP z = triangleNormal.Z < FP._0 ? -triangleNormal.Z : triangleNormal.Z;

            FP numeratorU, numeratorV, denominator;
            if (x >= y && x >= z)
            {

                numeratorU = (p.Y - b.Y) * (b.Z - c.Z) - (b.Y - c.Y) * (p.Z - b.Z);
                numeratorV = (p.Y - c.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (p.Z - c.Z);
                denominator = triangleNormal.X;
            }
            else if (y >= z)
            {

                numeratorU = (p.X - b.X) * (b.Z - c.Z) - (b.X - c.X) * (p.Z - b.Z);
                numeratorV = (p.X - c.X) * (c.Z - a.Z) - (c.X - a.X) * (p.Z - c.Z);
                denominator = -triangleNormal.Y;
            }
            else
            {

                numeratorU = (p.X - b.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - b.Y);
                numeratorV = (p.X - c.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - c.Y);
                denominator = triangleNormal.Z;
            }

            if (denominator < -FP.SmallestNonZero || denominator > FP.SmallestNonZero)
            {
                aWeight = numeratorU / denominator;
                bWeight = numeratorV / denominator;
                cWeight = FP._1 - aWeight - bWeight;
            }
            else
            {

				FP distance1, distance2, distance3;
                distance1 = FPVector3.DistanceSquared(p, a);
                distance2 = FPVector3.DistanceSquared(p, b);
                distance3 = FPVector3.DistanceSquared(p, c);
                if (distance1 < distance2 && distance1 < distance3)
                {
                    aWeight = FP._1;
                    bWeight = FP._0;
                    cWeight = FP._0;
                }
                else if (distance2 < distance3)
                {
                    aWeight = FP._0;
                    bWeight = FP._1;
                    cWeight = FP._0;
                }
                else
                {
                    aWeight = FP._0;
                    bWeight = FP._0;
                    cWeight = FP._1;
                }
            }

        }

    }
}
