using System;
using BEPUphysics.CollisionShapes.ConvexShapes;
using BEPUphysics.CollisionTests.CollisionAlgorithms;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUutilities;
using Photon.Deterministic;
using ThirdPersonPerformance.Instrumentation;
using ThirdPersonSimulation.QuantumNumerics;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.AttackGeometry
{
    public sealed class FPSweepQueryBackend
    {
        public const string ImplementationId = "bepuphysics1int/9237daa68c3014fd7c2e93c6a99326ba5248d60b/fp-q48.16-mpr-gjk-angular-query-v4";
        // 分离判定额外留出的距离，覆盖接触容差与定点舍入。
        static readonly FP SeparationMargin = FP._0_01;
        readonly SweepShape[] m_Shapes;
        readonly NativePart[][] m_Parts;
        // 每个形状所有部件相对根点的最大包络半径（部件偏移 + 最大半径 + 碰撞边距）。
        readonly FP[] m_Bounds;

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
            m_Bounds = new FP[shapes.Length];
            for (int i = 0; i < shapes.Length; i++)
            {
                m_Parts[i] = PrepareShape(i);
                m_Bounds[i] = Bound(m_Parts[i]);
            }
        }

        public void SetSphereRadius(int shapeIndex, FP radius)
        {
            m_Shapes[shapeIndex] = SweepShape.Sphere(m_Shapes[shapeIndex].Id, radius);
            ((SphereShape)m_Parts[shapeIndex][0].Shape).SetRadius(radius);
            m_Bounds[shapeIndex] = Bound(m_Parts[shapeIndex]);
        }

        static FP Bound(NativePart[] parts)
        {
            FP bound = FP._0;
            for (int i = 0; i < parts.Length; i++)
                bound = FPMath.Max(bound, Toolbox.Length(parts[i].LocalPose.Position) +
                    parts[i].Shape.MaximumRadius + parts[i].Shape.CollisionMargin);
            return bound;
        }

        // 两根点都沿直线运动、部件绕根点旋转不改变到根点的距离，所以整段相对根点轨迹到原点的最近距离
        // 超过双方包络半径之和时，任何时刻表面都不会进入接触容差，精确扫掠必然无候选。
        [PerformanceProbe("simulation.attack.sweep.separated")]
        static bool Separated(FPVector3 startOffset, FPVector3 endOffset, FP reach)
        {
            FPVector3 delta = endOffset - startOffset;
            FP lengthSquared = FPVector3.Dot(delta, delta);
            FPVector3 closest = startOffset;
            if (lengthSquared > FP._0)
            {
                FP t = FPMath.Clamp01(-FPVector3.Dot(startOffset, delta) / lengthSquared);
                closest = startOffset + delta * t;
            }
            return FPVector3.Dot(closest, closest) > reach * reach;
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
                var sourceRootMotion = angular ? new RigidSweepMotion(in sourceStart, in sourceEnd) : default;
                FP targetRangeStart = default;
                FP targetRangeEnd = default;
                FP startFraction = default;
                FP endFraction = default;
                bool targetRangePrepared = false;
                for (int targetIndex = 0; targetIndex < query.Targets.Length; targetIndex++)
                {
                    ref readonly SweepTarget target = ref query.Targets[targetIndex];
                    if (!query.Filter.Includes(target.Identity.TargetId, target.Layer, target.IsTrigger))
                        continue;
                    if (movingTarget)
                    {
                        if (segment.Start.Time < target.Start.Time || segment.End.Time > target.End.Time)
                            return results.Fail(SweepQueryStatus.TargetTimeMismatch, segmentIndex, target.Identity);
                        if (!targetRangePrepared || target.Start.Time != targetRangeStart || target.End.Time != targetRangeEnd)
                        {
                            targetRangeStart = target.Start.Time;
                            targetRangeEnd = target.End.Time;
                            FP duration = targetRangeEnd - targetRangeStart;
                            startFraction = (segment.Start.Time - targetRangeStart) / duration;
                            endFraction = (segment.End.Time - targetRangeStart) / duration;
                            targetRangePrepared = true;
                        }
                    }
                    FPVector3 targetStartPosition = RigidSweepMotion.PositionAt(
                        target.Start.Pose.Position, target.End.Pose.Position, startFraction);
                    FPVector3 targetEndPosition = RigidSweepMotion.PositionAt(
                        target.Start.Pose.Position, target.End.Pose.Position, endFraction);
                    if (Separated(sourceStart.Position - targetStartPosition, sourceEnd.Position - targetEndPosition,
                            m_Bounds[segment.Track.ShapeIndex] + m_Bounds[target.ShapeIndex] + SeparationMargin))
                        continue;
                    PrepareTargetMotion(in target, movingTarget, startFraction, endFraction, angular,
                        out RigidTransform targetStart, out RigidTransform targetEnd, out RigidSweepMotion targetRootMotion);
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
                            var sourceMotion = new ConvexSweepMotion(in sourceRootMotion, sourcePart.LocalPose);
                            var targetMotion = new ConvexSweepMotion(in targetRootMotion, targetPart.LocalPose);
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

        [PerformanceProbe("simulation.attack.sweep.target-motion")]
        static void PrepareTargetMotion(in SweepTarget target, bool movingTarget, FP startFraction, FP endFraction,
            bool angular, out RigidTransform targetStart, out RigidTransform targetEnd, out RigidSweepMotion targetRootMotion)
        {
            if (movingTarget)
            {
                RigidTransform before = ToGeometry(target.Start.Pose);
                RigidTransform after = ToGeometry(target.End.Pose);
                if ((startFraction == FP._0 || startFraction == FP._1) &&
                    (endFraction == FP._0 || endFraction == FP._1))
                {
                    targetStart = startFraction == FP._0 ? before : after;
                    targetEnd = endFraction == FP._0 ? before : after;
                }
                else
                {
                    var targetMotion = new RigidSweepMotion(in before, in after);
                    targetStart = targetMotion.At(startFraction);
                    targetEnd = targetMotion.At(endFraction);
                }
            }
            else
            {
                targetStart = ToGeometry(target.Start.Pose);
                targetEnd = targetStart;
            }
            targetRootMotion = angular ? new RigidSweepMotion(in targetStart, in targetEnd) : default;
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
