using System;
using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUphysics.CollisionTests.CollisionAlgorithms;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUutilities;
using FixMath.NET;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedSweepQueryBackend : ISweepQueryBackend<FixedScalar>
    {
        public const string ImplementationId = "bepuphysics1int/9237daa68c3014fd7c2e93c6a99326ba5248d60b/mpr-gjk-angular-query-v3";
        readonly SweepShape<FixedScalar>[] m_Shapes;
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

        public FixedSweepQueryBackend(ReadOnlySpan<SweepShape<FixedScalar>> shapes)
        {
            m_Shapes = shapes.ToArray();
            m_Parts = new NativePart[shapes.Length][];
            for (int i = 0; i < shapes.Length; i++)
                m_Parts[i] = PrepareShape(i);
        }

        public FixedScalar AngularContactTolerance => FromNative(AngularSweepToolbox.ContactTolerance);
        public SweepBackendDescriptor Descriptor { get; } = new SweepBackendDescriptor(
            ImplementationId, FixedSimulationNumericProfile.Value,
            SweepShapeKind.Sphere | SweepShapeKind.Capsule | SweepShapeKind.Box |
                SweepShapeKind.ConvexHull | SweepShapeKind.ConvexMesh,
            SweepMotionModel.PointPath | SweepMotionModel.ShapeTranslation | SweepMotionModel.RigidRotation,
            SweepTargetModel.StationaryPose | SweepTargetModel.LinearTranslation | SweepTargetModel.RigidRotation,
            true);

        NativePart[] PrepareShape(int index)
        {
            ref readonly SweepShape<FixedScalar> shape = ref m_Shapes[index];
            RigidTransform local = new RigidTransform(Vector3.Zero, Quaternion.Identity);
            ConvexShape native;
            switch (shape.Kind)
            {
                case SweepShapeKind.Sphere:
                    native = new SphereShape(ToNative(shape.Radius));
                    break;
                case SweepShapeKind.Capsule:
                    native = new CapsuleShape(ToNative(shape.HalfLength) * 2, ToNative(shape.Radius));
                    break;
                case SweepShapeKind.Box:
                    native = new BoxShape(ToNative(shape.HalfExtents.X) * 2,
                        ToNative(shape.HalfExtents.Y) * 2, ToNative(shape.HalfExtents.Z) * 2);
                    native.CollisionMargin = Fix64.Zero;
                    break;
                case SweepShapeKind.ConvexHull:
                {
                    ReadOnlySpan<SweepVector3<FixedScalar>> points = shape.Vertices;
                    if (points.Length < 4)
                        throw new ArgumentException("Sweep convex hull requires at least four volume vertices.");
                    var vertices = new Vector3[points.Length];
                    Vector3 center = Vector3.Zero;
                    for (int i = 0; i < points.Length; i++)
                    {
                        vertices[i] = ToNative(points[i]);
                        center += vertices[i];
                    }
                    center /= (Fix64)points.Length;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i] -= center;
                    native = new QueryConvexHullShape(vertices);
                    local.Position = center;
                    break;
                }
                case SweepShapeKind.ConvexMesh:
                {
                    ReadOnlySpan<SweepConvexPart<FixedScalar>> parts = shape.Parts;
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
                        RigidTransform partPose = ToNative(parts[i].LocalPose);
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

        public SweepQueryStatus SweepAll(in SweepQuery<FixedScalar> query, SweepResultBuffer<FixedScalar> results)
        {
            results.Begin(in query, Descriptor);
            if (query.TargetModel != SweepTargetModel.StationaryPose &&
                query.TargetModel != SweepTargetModel.LinearTranslation &&
                query.TargetModel != SweepTargetModel.RigidRotation)
                return results.Fail(SweepQueryStatus.UnsupportedMotion, -1, default);

            for (int i = 0; i < query.Targets.Length; i++)
            {
                ref readonly SweepTarget<FixedScalar> target = ref query.Targets[i];
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
                ref readonly SweepSegment<FixedScalar> segment = ref query.Segments[segmentIndex];
                ref readonly SweepShape<FixedScalar> shape = ref m_Shapes[segment.Track.ShapeIndex];
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

                RigidTransform sourceStart = ToNative(segment.Start.Pose);
                RigidTransform sourceEnd = ToNative(segment.End.Pose);
                NativePart[] sourceParts = m_Parts[segment.Track.ShapeIndex];
                bool angular = segment.Track.Motion == SweepMotionModel.RigidRotation ||
                    query.TargetModel == SweepTargetModel.RigidRotation;
                for (int targetIndex = 0; targetIndex < query.Targets.Length; targetIndex++)
                {
                    ref readonly SweepTarget<FixedScalar> target = ref query.Targets[targetIndex];
                    if (!query.Filter.Includes(target.Identity.TargetId, target.Layer, target.IsTrigger))
                        continue;
                    RigidTransform targetStart;
                    RigidTransform targetEnd;
                    if (movingTarget)
                    {
                        if (segment.Start.Time < target.Start.Time || segment.End.Time > target.End.Time)
                            return results.Fail(SweepQueryStatus.TargetTimeMismatch, segmentIndex, target.Identity);
                        var targetMotion = new RigidSweepMotion(ToNative(target.Start.Pose), ToNative(target.End.Pose));
                        targetStart = PoseAt(in target, in targetMotion, segment.Start.Time);
                        targetEnd = PoseAt(in target, in targetMotion, segment.End.Time);
                    }
                    else
                    {
                        targetStart = ToNative(target.Start.Pose);
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
                        Vector3 sourcePoint = default;
                        Vector3 targetPoint = default;
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
                                    Vector3 sourceSweep = sourceEnd.Position - sourceStart.Position;
                                    Vector3 targetSweep = targetEnd.Position - targetStart.Position;
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
                        FixedScalar fraction = FromNative(hit.T);
                        FixedScalar time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * fraction;
                        SweepContactFields fields = SweepContactFields.Time;
                        if (positionAvailable)
                            fields |= SweepContactFields.Point;
                        if (!initialOverlap && hit.Normal != Vector3.Zero)
                            fields |= SweepContactFields.Normal;
                        var candidate = new SweepCandidate<FixedScalar>(target.Identity, segment.Track.Id,
                            shape.Id, segmentIndex, segment.Track.Motion,
                            initialOverlap ? SweepContactKind.InitialOverlap : SweepContactKind.Sweep,
                            fields, time, fraction, movingTarget ? time : query.TargetPoseTime,
                            positionAvailable ? FromNative(hit.Location) : default, FromNative(hit.Normal),
                            sourcePartIndex, targetPartIndex);
                        if (!results.Add(in candidate))
                            return results.Fail(SweepQueryStatus.CapacityExceeded, segmentIndex, target.Identity);
                    }
                }
            }
            return results.Complete();
        }

        static RigidTransform PoseAt(in SweepTarget<FixedScalar> target, in RigidSweepMotion motion, FixedScalar time)
        {
            Fix64 fraction = ToNative((time - target.Start.Time) / (target.End.Time - target.Start.Time));
            return motion.At(fraction);
        }

        static RigidTransform Compose(RigidTransform local, RigidTransform parent)
        {
            Quaternion.Transform(ref local.Position, ref parent.Orientation, out Vector3 offset);
            Quaternion.Concatenate(ref local.Orientation, ref parent.Orientation, out Quaternion orientation);
            return new RigidTransform(parent.Position + offset, orientation);
        }

        static bool SameOrientation(SweepQuaternion<FixedScalar> left, SweepQuaternion<FixedScalar> right) =>
            left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W ||
            left.X == -right.X && left.Y == -right.Y && left.Z == -right.Z && left.W == -right.W;

        static Fix64 ToNative(FixedScalar value) => Fix64.FromRaw(value.Raw);
        static FixedScalar FromNative(Fix64 value) => FixedScalar.FromRaw(value.RawValue);
        static Vector3 ToNative(SweepVector3<FixedScalar> value) => new Vector3(ToNative(value.X), ToNative(value.Y), ToNative(value.Z));
        static SweepVector3<FixedScalar> FromNative(Vector3 value) => new SweepVector3<FixedScalar>(FromNative(value.X), FromNative(value.Y), FromNative(value.Z));
        static RigidTransform ToNative(SweepPose<FixedScalar> pose) => new RigidTransform(ToNative(pose.Position),
            new Quaternion(ToNative(pose.Orientation.X), ToNative(pose.Orientation.Y), ToNative(pose.Orientation.Z), ToNative(pose.Orientation.W)));
    }
}
