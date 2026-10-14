using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUutilities;
using FixMath.NET;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms
{
    public readonly struct RigidSweepMotion
    {
        readonly RigidTransform m_Start;
        readonly RigidTransform m_End;
        readonly Vector3 m_Axis;

        public RigidSweepMotion(RigidTransform start, RigidTransform end)
        {
            m_Start = start;
            m_End = end;
            Quaternion.GetRelativeRotation(ref start.Orientation, ref end.Orientation, out Quaternion relative);
            if (relative.W < Fix64.Zero)
                relative = new Quaternion(-relative.X, -relative.Y, -relative.Z, -relative.W);
            Vector3 axis = new Vector3(relative.X, relative.Y, relative.Z);
            Fix64 axisLength = axis.Length();
            Angle = 2 * Fix64.Atan2(axisLength, relative.W);
            m_Axis = axisLength == Fix64.Zero ? Vector3.Zero : axis / axisLength;
        }

        public Vector3 Translation => m_End.Position - m_Start.Position;
        public Fix64 Angle { get; }

        public RigidTransform At(Fix64 fraction)
        {
            if (fraction == Fix64.Zero)
                return m_Start;
            if (fraction == Fix64.One)
                return m_End;
            Quaternion orientation = m_Start.Orientation;
            if (Angle != Fix64.Zero)
            {
                Quaternion increment = Quaternion.CreateFromAxisAngle(m_Axis, Angle * fraction);
                Quaternion.Concatenate(ref orientation, ref increment, out orientation);
            }
            return new RigidTransform(m_Start.Position + Translation * fraction, orientation);
        }
    }

    public readonly struct ConvexSweepMotion
    {
        readonly RigidSweepMotion m_Root;
        readonly RigidTransform m_Local;

        public ConvexSweepMotion(RigidTransform start, RigidTransform end, RigidTransform local)
        {
            m_Root = new RigidSweepMotion(start, end);
            m_Local = local;
        }

        public Vector3 Translation => m_Root.Translation;
        public Fix64 AngularBound(Fix64 shapeRadius) =>
            m_Root.Angle * (m_Local.Position.Length() + shapeRadius);

        public RigidTransform At(Fix64 fraction)
        {
            RigidTransform root = m_Root.At(fraction);
            Quaternion orientation = root.Orientation;
            Vector3 offset = m_Local.Position;
            Quaternion.Transform(ref offset, ref orientation, out offset);
            Quaternion local = m_Local.Orientation;
            Quaternion.Concatenate(ref local, ref orientation, out Quaternion partOrientation);
            return new RigidTransform(root.Position + offset, partOrientation);
        }
    }

    public static class AngularSweepToolbox
    {
        public const int MaximumAdvancementIterations = 64;
        public static readonly Fix64 ContactTolerance = (Fix64)0.0001m;

        public static bool Sweep(
            ConvexShape source, ConvexShape target,
            in ConvexSweepMotion sourceMotion, in ConvexSweepMotion targetMotion,
            out RayHit hit, out bool iterationLimitReached, out bool initialOverlap)
        {
            hit = default;
            iterationLimitReached = false;
            initialOverlap = false;
            Fix64 fraction = Fix64.Zero;
            Fix64 angularBound = sourceMotion.AngularBound(source.MaximumRadius) +
                targetMotion.AngularBound(target.MaximumRadius);
            Vector3 relativeTranslation = sourceMotion.Translation - targetMotion.Translation;

            for (int iteration = 0; iteration < MaximumAdvancementIterations; iteration++)
            {
                RigidTransform sourceTransform = sourceMotion.At(fraction);
                RigidTransform targetTransform = targetMotion.At(fraction);
                bool coresOverlap = GJKToolbox.GetClosestPoints(source, target,
                    ref sourceTransform, ref targetTransform, out Vector3 pointSource, out Vector3 pointTarget,
                    out iterationLimitReached);
                if (iterationLimitReached)
                    return false;
                Vector3 offset = pointSource - pointTarget;
                Fix64 coreDistance = offset.Length();
                Fix64 margin = source.CollisionMargin + target.CollisionMargin;
                if (coresOverlap || coreDistance <= margin)
                {
                    initialOverlap = fraction == Fix64.Zero;
                    hit.T = fraction;
                    hit.Location = coresOverlap ? pointTarget :
                        pointTarget + offset * (target.CollisionMargin / margin);
                    return true;
                }
                Vector3 normal = offset / coreDistance;
                if (coreDistance - margin <= ContactTolerance)
                {
                    hit.T = fraction;
                    hit.Location = pointTarget + normal * target.CollisionMargin;
                    hit.Normal = normal;
                    return true;
                }

                Vector3 sourceDirection = -normal;
                source.GetExtremePoint(sourceDirection, ref sourceTransform, out Vector3 sourceSupport);
                target.GetExtremePoint(normal, ref targetTransform, out Vector3 targetSupport);
                Fix64 gap = Vector3.Dot(normal, sourceSupport - targetSupport);
                Fix64 closingSpeed = -Vector3.Dot(normal, relativeTranslation) + angularBound;
                if (closingSpeed <= Fix64.Zero)
                    return false;
                Fix64 step = gap / closingSpeed;
                if (step <= Fix64.Zero)
                {
                    iterationLimitReached = true;
                    return false;
                }
                Fix64 next = fraction + step;
                if (next > Fix64.One)
                    return false;
                if (next == fraction)
                {
                    iterationLimitReached = true;
                    return false;
                }
                fraction = next;
            }
            iterationLimitReached = true;
            return false;
        }
    }
}
