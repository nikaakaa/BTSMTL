using System;
using System.Collections.Generic;
using FixMath.NET;

namespace BEPUutilities
{
    //TODO: It would be nice to split and improve this monolith into individually superior, organized components.


    /// <summary>
    /// Helper class with many algorithms for intersection testing and 3D math.
    /// </summary>
    public static class Toolbox
    {
        /// <summary>
        /// Large tolerance value. Defaults to 1e-5f.
        /// </summary>
        public static readonly Fix64 BigEpsilon = F64.C1 / new Fix64(100000);

        /// <summary>
        /// Tolerance value. Defaults to 1e-7f.
        /// </summary>
        public static readonly Fix64 Epsilon = F64.C1 / new Fix64(10000000);

        /// <summary>
        /// Represents an invalid Vector3.
        /// </summary>
        public static readonly Vector3 NoVector = new Vector3(-Fix64.MaxValue, -Fix64.MaxValue, -Fix64.MaxValue);

        /// <summary>
        /// Reference for a vector with dimensions (0,0,1).
        /// </summary>
        public static Vector3 BackVector = Vector3.Backward;

        /// <summary>
        /// Reference for a vector with dimensions (0,-1,0).
        /// </summary>
        public static Vector3 DownVector = Vector3.Down;

        /// <summary>
        /// Reference for a vector with dimensions (0,0,-1).
        /// </summary>
        public static Vector3 ForwardVector = Vector3.Forward;

        /// <summary>
        /// Refers to the identity quaternion.
        /// </summary>
        public static Quaternion IdentityOrientation = Quaternion.Identity;

        /// <summary>
        /// Reference for a vector with dimensions (-1,0,0).
        /// </summary>
        public static Vector3 LeftVector = Vector3.Left;

        /// <summary>
        /// Reference for a vector with dimensions (1,0,0).
        /// </summary>
        public static Vector3 RightVector = Vector3.Right;

        /// <summary>
        /// Reference for a vector with dimensions (0,1,0).
        /// </summary>
        public static Vector3 UpVector = Vector3.Up;

        /// <summary>
        /// Matrix containing zeroes for every element.
        /// </summary>
        public static Matrix ZeroMatrix = new Matrix(F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0, F64.C0);

        /// <summary>
        /// Reference for a vector with dimensions (0,0,0).
        /// </summary>
        public static Vector3 ZeroVector = Vector3.Zero;

        /// <summary>
        /// Refers to the rigid identity transformation.
        /// </summary>
        public static RigidTransform RigidIdentity = RigidTransform.Identity;


        /// <summary>
        /// Gets the barycentric coordinates of the point with respect to a triangle's vertices.
        /// </summary>
        /// <param name="p">Point to compute the barycentric coordinates of.</param>
        /// <param name="a">First vertex in the triangle.</param>
        /// <param name="b">Second vertex in the triangle.</param>
        /// <param name="c">Third vertex in the triangle.</param>
        /// <param name="aWeight">Weight of the first vertex.</param>
        /// <param name="bWeight">Weight of the second vertex.</param>
        /// <param name="cWeight">Weight of the third vertex.</param>
        public static void GetBarycentricCoordinates(ref Vector3 p, ref Vector3 a, ref Vector3 b, ref Vector3 c, out Fix64 aWeight, out Fix64 bWeight, out Fix64 cWeight)
        {
            Vector3 ab, ac;
            Vector3.Subtract(ref b, ref a, out ab);
            Vector3.Subtract(ref c, ref a, out ac);
            Vector3 triangleNormal;
            Vector3.Cross(ref ab, ref ac, out triangleNormal);
            Fix64 x = triangleNormal.X < F64.C0 ? -triangleNormal.X : triangleNormal.X;
            Fix64 y = triangleNormal.Y < F64.C0 ? -triangleNormal.Y : triangleNormal.Y;
            Fix64 z = triangleNormal.Z < F64.C0 ? -triangleNormal.Z : triangleNormal.Z;

            Fix64 numeratorU, numeratorV, denominator;
            if (x >= y && x >= z)
            {
                //The projection of the triangle on the YZ plane is the largest.
                numeratorU = (p.Y - b.Y) * (b.Z - c.Z) - (b.Y - c.Y) * (p.Z - b.Z); //PBC
                numeratorV = (p.Y - c.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (p.Z - c.Z); //PCA
                denominator = triangleNormal.X;
            }
            else if (y >= z)
            {
                //The projection of the triangle on the XZ plane is the largest.
                numeratorU = (p.X - b.X) * (b.Z - c.Z) - (b.X - c.X) * (p.Z - b.Z); //PBC
                numeratorV = (p.X - c.X) * (c.Z - a.Z) - (c.X - a.X) * (p.Z - c.Z); //PCA
                denominator = -triangleNormal.Y;
            }
            else
            {
                //The projection of the triangle on the XY plane is the largest.
                numeratorU = (p.X - b.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - b.Y); //PBC
                numeratorV = (p.X - c.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - c.Y); //PCA
                denominator = triangleNormal.Z;
            }

            if (denominator < F64.Cm1em9 || denominator > F64.C1em9)
            {
                denominator = F64.C1 / denominator;
                aWeight = numeratorU * denominator;
                bWeight = numeratorV * denominator;
                cWeight = F64.C1 - aWeight - bWeight;
            }
            else
            {
				//It seems to be a degenerate triangle.
				//In that case, pick one of the closest vertices.
				//MOST of the time, this will happen when the vertices
				//are all very close together (all three points form a single point).
				//Sometimes, though, it could be that it's more of a line.
				//If it's a little inefficient, don't worry- this is a corner case anyway.

				Fix64 distance1, distance2, distance3;
                Vector3.DistanceSquared(ref p, ref a, out distance1);
                Vector3.DistanceSquared(ref p, ref b, out distance2);
                Vector3.DistanceSquared(ref p, ref c, out distance3);
                if (distance1 < distance2 && distance1 < distance3)
                {
                    aWeight = F64.C1;
                    bWeight = F64.C0;
                    cWeight = F64.C0;
                }
                else if (distance2 < distance3)
                {
                    aWeight = F64.C0;
                    bWeight = F64.C1;
                    cWeight = F64.C0;
                }
                else
                {
                    aWeight = F64.C0;
                    bWeight = F64.C0;
                    cWeight = F64.C1;
                }
            }


        }




    }
}
