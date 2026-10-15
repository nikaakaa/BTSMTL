using Photon.Deterministic;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public sealed class QueryConvexHullShape : ConvexShape
    {
        readonly FPVector3[] m_Vertices;

        public QueryConvexHullShape(FPVector3[] centeredVertices)
        {
            m_Vertices = centeredVertices;
            collisionMargin = FP._0;
            FP radius = FP._0;
            for (int i = 0; i < centeredVertices.Length; i++)
            {
                FPVector3.Normalize(centeredVertices[i], out FP length);
                radius = FPMath.Max(radius, length);
            }
            MaximumRadius = radius;
        }

        public override void GetLocalExtremePointWithoutMargin(ref FPVector3 direction, out FPVector3 extremePoint)
        {
            extremePoint = m_Vertices[0];
            FP maximum = FPVector3.Dot(extremePoint, direction);
            for (int i = 1; i < m_Vertices.Length; i++)
            {
                FP projection = FPVector3.Dot(m_Vertices[i], direction);
                if (projection > maximum)
                {
                    maximum = projection;
                    extremePoint = m_Vertices[i];
                }
            }
        }
    }
}
