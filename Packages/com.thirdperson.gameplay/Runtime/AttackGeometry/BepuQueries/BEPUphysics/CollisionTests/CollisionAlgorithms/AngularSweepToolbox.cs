using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUutilities;
using Photon.Deterministic;

namespace BEPUphysics.CollisionTests.CollisionAlgorithms
{
    public readonly struct RigidSweepMotion
    {
        readonly RigidTransform m_Start;
        readonly RigidTransform m_End;
        readonly FPVector3 m_Axis;

        public RigidSweepMotion(RigidTransform start, RigidTransform end)
        {
            m_Start = start;
            m_End = end;
            FPQuaternion relative = end.Orientation * FPQuaternion.Conjugate(start.Orientation);
            if (relative.W < FP._0)
                relative = new FPQuaternion(-relative.X, -relative.Y, -relative.Z, -relative.W);
            FPVector3 axis = new FPVector3(relative.X, relative.Y, relative.Z);
            FPVector3 normalizedAxis = FPVector3.Normalize(axis, out FP axisLength);
            Angle = 2 * FPMath.Atan2(axisLength, relative.W);
            m_Axis = normalizedAxis;
        }

        public FPVector3 Translation => m_End.Position - m_Start.Position;
        public FP Angle { get; }

        public RigidTransform At(FP fraction)
        {
            if (fraction == FP._0)
                return m_Start;
            if (fraction == FP._1)
                return m_End;
            FPQuaternion orientation = m_Start.Orientation;
            if (Angle != FP._0)
            {
                FPQuaternion increment = FPQuaternion.RadianAxis(Angle * fraction, m_Axis);
                orientation = increment * orientation;
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

        public FPVector3 Translation => m_Root.Translation;
        public FP AngularBound(FP shapeRadius) =>
            m_Root.Angle * (Toolbox.Length(m_Local.Position) + shapeRadius);

        public RigidTransform At(FP fraction)
        {
            RigidTransform root = m_Root.At(fraction);
            FPQuaternion orientation = root.Orientation;
            FPVector3 offset = m_Local.Position;
            offset = orientation * offset;
            FPQuaternion local = m_Local.Orientation;
            FPQuaternion partOrientation = orientation * local;
            return new RigidTransform(root.Position + offset, partOrientation);
        }
    }

    public static class AngularSweepToolbox
    {
        public const int MaximumAdvancementIterations = 64;
        public static readonly FP ContactTolerance = FP.EN4;

        public static bool Sweep(
            ConvexShape source, ConvexShape target,
            in ConvexSweepMotion sourceMotion, in ConvexSweepMotion targetMotion,
            out RayHit hit, out bool iterationLimitReached, out bool initialOverlap)
        {
            hit = default;
            iterationLimitReached = false;
            initialOverlap = false;
            FP fraction = FP._0;
            FP angularBound = sourceMotion.AngularBound(source.MaximumRadius) +
                targetMotion.AngularBound(target.MaximumRadius);
            FPVector3 relativeTranslation = sourceMotion.Translation - targetMotion.Translation;

            for (int iteration = 0; iteration < MaximumAdvancementIterations; iteration++)
            {
                RigidTransform sourceTransform = sourceMotion.At(fraction);
                RigidTransform targetTransform = targetMotion.At(fraction);
                bool coresOverlap = GJKToolbox.GetClosestPoints(source, target,
                    ref sourceTransform, ref targetTransform, out FPVector3 pointSource, out FPVector3 pointTarget,
                    out iterationLimitReached);
                if (iterationLimitReached)
                    return false;
                FPVector3 offset = pointSource - pointTarget;
                FPVector3 normal = FPVector3.Normalize(offset, out FP coreDistance);
                FP margin = source.CollisionMargin + target.CollisionMargin;
                if (coresOverlap || coreDistance <= margin)
                {
                    initialOverlap = fraction == FP._0;
                    hit.T = fraction;
                    hit.Location = coresOverlap ? pointTarget :
                        pointTarget + offset * (target.CollisionMargin / margin);
                    return true;
                }
                if (coreDistance - margin <= ContactTolerance)
                {
                    hit.T = fraction;
                    hit.Location = pointTarget + normal * target.CollisionMargin;
                    hit.Normal = normal;
                    return true;
                }

                FPVector3 sourceDirection = -normal;
                source.GetExtremePoint(sourceDirection, ref sourceTransform, out FPVector3 sourceSupport);
                target.GetExtremePoint(normal, ref targetTransform, out FPVector3 targetSupport);
                FP gap = FPVector3.Dot(normal, sourceSupport - targetSupport);
                FP closingSpeed = -FPVector3.Dot(normal, relativeTranslation) + angularBound;
                if (closingSpeed <= FP._0)
                    return false;
                FP step = gap / closingSpeed;
                if (step <= FP._0)
                {
                    iterationLimitReached = true;
                    return false;
                }
                FP next = fraction + step;
                if (next > FP._1)
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
