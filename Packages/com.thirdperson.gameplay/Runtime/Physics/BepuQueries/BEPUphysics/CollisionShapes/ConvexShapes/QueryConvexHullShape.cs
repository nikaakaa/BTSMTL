using BEPUutilities;
using FixMath.NET;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    public sealed class QueryConvexHullShape : ConvexShape
    {
        readonly Vector3[] m_Vertices;

        public QueryConvexHullShape(Vector3[] centeredVertices)
        {
            m_Vertices = centeredVertices;
            collisionMargin = Fix64.Zero;
            Fix64 radiusSquared = Fix64.Zero;
            for (int i = 0; i < centeredVertices.Length; i++)
                radiusSquared = MathHelper.Max(radiusSquared, centeredVertices[i].LengthSquared());
            MaximumRadius = Fix64.Sqrt(radiusSquared);
        }

        public override void GetLocalExtremePointWithoutMargin(ref Vector3 direction, out Vector3 extremePoint)
        {
            extremePoint = m_Vertices[0];
            Fix64 maximum = Vector3.Dot(extremePoint, direction);
            for (int i = 1; i < m_Vertices.Length; i++)
            {
                Fix64 projection = Vector3.Dot(m_Vertices[i], direction);
                if (projection > maximum)
                {
                    maximum = projection;
                    extremePoint = m_Vertices[i];
                }
            }
        }
    }
}
