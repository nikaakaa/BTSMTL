using System;
using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUphysics.CollisionTests.CollisionAlgorithms;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUutilities;
using Photon.Deterministic;
using ThirdPersonSimulation.QuantumNumerics;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.AttackGeometry
{
    public sealed class FPSweepQueryBackend
    {
        public const string ImplementationId = "bepuphysics1int/9237daa68c3014fd7c2e93c6a99326ba5248d60b/fp-q48.16-mpr-gjk-angular-query-v4";
        readonly SweepShape[] m_Shapes;
        readonly NativePart[][] m_Parts;

        readonly struct NativePart
        {
            public NativePart(ConvexShape shape, RigidTransform localPose)
            {
                Shape = shape;
                LocalPose = localPose;
            }
            public ConvexShape Shape { get; }
            public RigidTransform LocalPose { get; }
        }

        public FPSweepQueryBackend(ReadOnlySpan<SweepShape> shapes)
        {
            m_Shapes = shapes.ToArray();
            m_Parts = new NativePart[shapes.Length][];
            for (int i = 0; i < shapes.Length; i++)
                m_Parts[i] = PrepareShape(i);
        }

        public FP AngularContactTolerance => AngularSweepToolbox.ContactTolerance;
        public SweepBackendDescriptor Descriptor { get; } = new SweepBackendDescriptor(
            ImplementationId, QuantumSimulationNumericProfile.Value,
            SweepShapeKind.Sphere | SweepShapeKind.Capsule | SweepShapeKind.Box |
                SweepShapeKind.ConvexHull | SweepShapeKind.ConvexMesh,
            SweepMotionModel.PointPath | SweepMotionModel.ShapeTranslation | SweepMotionModel.RigidRotation,
            SweepTargetModel.StationaryPose | SweepTargetModel.LinearTranslation | SweepTargetModel.RigidRotation,
            true);

        NativePart[] PrepareShape(int index)
        {
            ref readonly SweepShape shape = ref m_Shapes[index];
            RigidTransform local = new RigidTransform(FPVector3.Zero, FPQuaternion.Identity);
            ConvexShape native;
            switch (shape.Kind)
            {
                case SweepShapeKind.Sphere:
                    native = new SphereShape(shape.Radius);
                    break;
                case SweepShapeKind.Capsule:
                    native = new CapsuleShape(shape.HalfLength * 2, shape.Radius);
                    break;
                case SweepShapeKind.Box:
                    native = new BoxShape(shape.HalfExtents.X * 2,
                        shape.HalfExtents.Y * 2, shape.HalfExtents.Z * 2);
                    break;
                case SweepShapeKind.ConvexHull:
                {
                    ReadOnlySpan<FPVector3> points = shape.Vertices;
                    if (points.Length < 4)
                        throw new ArgumentException("Sweep convex hull requires at least four volume vertices.");
                    var vertices = new FPVector3[points.Length];
                    FPVector3 center = FPVector3.Zero;
                    for (int i = 0; i < points.Length; i++)
                    {
                        vertices[i] = points[i];
                        center += vertices[i];
                    }
                    center /= (FP)points.Length;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i] -= center;
                    native = new QueryConvexHullShape(vertices);
                    local.Position = center;
                    break;
                }
                case SweepShapeKind.ConvexMesh:
                {
                    ReadOnlySpan<SweepConvexPart> parts = shape.Parts;
                    if (parts.Length == 0)
                        throw new ArgumentException("Sweep convex Mesh requires prepared convex parts.");
                    int count = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].ShapeIndex < 0 || parts[i].ShapeIndex >= index)
                            throw new ArgumentException("Sweep convex Mesh parts must reference earlier prepared shapes.");
                        count += m_Parts[parts[i].ShapeIndex].Length;
                    }
                    var output = new NativePart[count];
                    int cursor = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        RigidTransform partPose = ToGeometry(parts[i].LocalPose);
                        NativePart[] children = m_Parts[parts[i].ShapeIndex];
                        for (int child = 0; child < children.Length; child++)
                            output[cursor++] = new NativePart(children[child].Shape,
                                Compose(children[child].LocalPose, partPose));
                    }
                    return output;
                }
                default:
                    throw new NotSupportedException("BEPU sweep shape is not supported.");
            }
            return new[] { new NativePart(native, local) };
        }

        public SweepQueryStatus SweepAll<TSource>(in SweepQuery<TSource> query, SweepResultBuffer<TSource> results) where TSource : struct
        {
            results.Begin(in query, Descriptor);
            if (query.TargetModel != SweepTargetModel.StationaryPose &&
                query.TargetModel != SweepTargetModel.LinearTranslation &&
                query.TargetModel != SweepTargetModel.RigidRotation)
                return results.Fail(SweepQueryStatus.UnsupportedMotion, -1, default);

            for (int i = 0; i < query.Targets.Length; i++)
            {
                ref readonly SweepTarget target = ref query.Targets[i];
                if (query.TargetModel == SweepTargetModel.StationaryPose)
                {
                    if (target.Start.Time != query.TargetPoseTime)
                        return results.Fail(SweepQueryStatus.TargetTimeMismatch, -1, target.Identity);
                }
                else
                {
                    if (target.Start.Time >= target.End.Time)
                        return results.Fail(SweepQueryStatus.TargetTimeMismatch, -1, target.Identity);
                    if (query.TargetModel == SweepTargetModel.LinearTranslation &&
                        m_Shapes[target.ShapeIndex].Kind != SweepShapeKind.Sphere &&
                        !SameOrientation(target.Start.Pose.Orientation, target.End.Pose.Orientation))
                        return results.Fail(SweepQueryStatus.UnsupportedMotion, -1, target.Identity);
                }
            }

            bool movingTarget = query.TargetModel != SweepTargetModel.StationaryPose;
            for (int segmentIndex = 0; segmentIndex < query.Segments.Length; segmentIndex++)
            {
                ref readonly SweepSegment segment = ref query.Segments[segmentIndex];
                ref readonly SweepShape shape = ref m_Shapes[segment.Track.ShapeIndex];
                if (segment.Track.Motion == SweepMotionModel.PointPath)
                {
                    if (shape.Kind != SweepShapeKind.Sphere)
                        return results.Fail(SweepQueryStatus.UnsupportedShape, segmentIndex, default);
                }
                else if (segment.Track.Motion != SweepMotionModel.ShapeTranslation &&
                    segment.Track.Motion != SweepMotionModel.RigidRotation)
                    return results.Fail(SweepQueryStatus.UnsupportedMotion, segmentIndex, default);
                if (segment.Track.Motion != SweepMotionModel.RigidRotation &&
                    shape.Kind != SweepShapeKind.Sphere &&
                    !SameOrientation(segment.Start.Pose.Orientation, segment.End.Pose.Orientation))
                    return results.Fail(SweepQueryStatus.UnsupportedMotion, segmentIndex, default);

                RigidTransform sourceStart = ToGeometry(segment.Start.Pose);
                RigidTransform sourceEnd = ToGeometry(segment.End.Pose);
                NativePart[] sourceParts = m_Parts[segment.Track.ShapeIndex];
                bool angular = segment.Track.Motion == SweepMotionModel.RigidRotation ||
                    query.TargetModel == SweepTargetModel.RigidRotation;
                for (int targetIndex = 0; targetIndex < query.Targets.Length; targetIndex++)
                {
                    ref readonly SweepTarget target = ref query.Targets[targetIndex];
                    if (!query.Filter.Includes(target.Identity.TargetId, target.Layer, target.IsTrigger))
                        continue;
                    RigidTransform targetStart;
                    RigidTransform targetEnd;
                    if (movingTarget)
                    {
                        if (segment.Start.Time < target.Start.Time || segment.End.Time > target.End.Time)
                            return results.Fail(SweepQueryStatus.TargetTimeMismatch, segmentIndex, target.Identity);
                        var targetMotion = new RigidSweepMotion(ToGeometry(target.Start.Pose), ToGeometry(target.End.Pose));
                        targetStart = PoseAt(in target, in targetMotion, segment.Start.Time);
                        targetEnd = PoseAt(in target, in targetMotion, segment.End.Time);
                    }
                    else
                    {
                        targetStart = ToGeometry(target.Start.Pose);
                        targetEnd = targetStart;
                    }
                    NativePart[] targetParts = m_Parts[target.ShapeIndex];
                    for (int sourcePartIndex = 0; sourcePartIndex < sourceParts.Length; sourcePartIndex++)
                    for (int targetPartIndex = 0; targetPartIndex < targetParts.Length; targetPartIndex++)
                    {
                        NativePart sourcePart = sourceParts[sourcePartIndex];
                        NativePart targetPart = targetParts[targetPartIndex];
                        bool intersects;
                        bool initialOverlap;
                        bool iterationLimitReached;
                        bool positionAvailable;
                        bool coresOverlap = false;
                        FPVector3 sourcePoint = default;
                        FPVector3 targetPoint = default;
                        RayHit hit;
                        if (angular)
                        {
                            var sourceMotion = new ConvexSweepMotion(sourceStart, sourceEnd, sourcePart.LocalPose);
                            var targetMotion = new ConvexSweepMotion(targetStart, targetEnd, targetPart.LocalPose);
                            intersects = AngularSweepToolbox.Sweep(sourcePart.Shape, targetPart.Shape,
                                in sourceMotion, in targetMotion, out hit, out iterationLimitReached, out initialOverlap);
                            positionAvailable = intersects;
                        }
                        else
                        {
                            RigidTransform sourceTransform = Compose(sourcePart.LocalPose, sourceStart);
                            RigidTransform targetTransform = Compose(targetPart.LocalPose, targetStart);
                            initialOverlap = MPRToolbox.AreShapesOverlapping(sourcePart.Shape, targetPart.Shape,
                                ref sourceTransform, ref targetTransform, out iterationLimitReached);
                            hit = default;
                            positionAvailable = false;
                            intersects = initialOverlap;
                            if (!iterationLimitReached)
                            {
                                if (initialOverlap)
                                {
                                    coresOverlap = GJKToolbox.GetClosestPoints(sourcePart.Shape, targetPart.Shape,
                                        ref sourceTransform, ref targetTransform, out sourcePoint,
                                        out targetPoint, out iterationLimitReached);
                                    positionAvailable = true;
                                }
                                else
                                {
                                    FPVector3 sourceSweep = sourceEnd.Position - sourceStart.Position;
                                    FPVector3 targetSweep = targetEnd.Position - targetStart.Position;
                                    if (sourceSweep != targetSweep)
                                    {
                                        intersects = MPRToolbox.Sweep(sourcePart.Shape, targetPart.Shape,
                                            ref sourceSweep, ref targetSweep, ref sourceTransform, ref targetTransform,
                                            out hit, out iterationLimitReached, out positionAvailable);
                                        hit.Normal = -hit.Normal;
                                    }
                                }
                            }
                        }
                        if (iterationLimitReached)
                            return results.Fail(SweepQueryStatus.NativeIterationLimit, segmentIndex, target.Identity);
                        if (!intersects)
                            continue;
                        if (!angular && initialOverlap)
                            hit.Location = coresOverlap ? targetPoint : targetPoint + (sourcePoint - targetPoint) *
                                (targetPart.Shape.CollisionMargin /
                                (sourcePart.Shape.CollisionMargin + targetPart.Shape.CollisionMargin));
                        FP fraction = hit.T;
                        FP time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * fraction;
                        SweepContactFields fields = SweepContactFields.Time;
                        if (positionAvailable)
                            fields |= SweepContactFields.Point;
                        if (!initialOverlap && hit.Normal != FPVector3.Zero)
                            fields |= SweepContactFields.Normal;
                        var candidate = new SweepCandidate(target.Identity, segment.Track.Id,
                            shape.Id, segmentIndex, segment.Track.Motion,
                            initialOverlap ? SweepContactKind.InitialOverlap : SweepContactKind.Sweep,
                            fields, time, fraction, movingTarget ? time : query.TargetPoseTime,
                            positionAvailable ? hit.Location : default, hit.Normal,
                            sourcePartIndex, targetPartIndex);
                        if (!results.Add(in candidate))
                            return results.Fail(SweepQueryStatus.CapacityExceeded, segmentIndex, target.Identity);
                    }
                }
            }
            return results.Complete();
        }

        static RigidTransform PoseAt(in SweepTarget target, in RigidSweepMotion motion, FP time)
        {
            FP fraction = (time - target.Start.Time) / (target.End.Time - target.Start.Time);
            return motion.At(fraction);
        }

        static RigidTransform Compose(RigidTransform local, RigidTransform parent)
        {
            FPVector3 offset = parent.Orientation * local.Position;
            FPQuaternion orientation = parent.Orientation * local.Orientation;
            return new RigidTransform(parent.Position + offset, orientation);
        }

        static bool SameOrientation(FPQuaternion left, FPQuaternion right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W ||
            left.X == -right.X && left.Y == -right.Y && left.Z == -right.Z && left.W == -right.W;

        static RigidTransform ToGeometry(SweepPose pose) => new RigidTransform(pose.Position, pose.Orientation);
    }
}
