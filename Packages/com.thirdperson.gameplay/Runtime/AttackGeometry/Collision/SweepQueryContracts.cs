using Photon.Deterministic;
using System;

namespace ThirdPersonSimulation.Collision
{
    [Flags]
    public enum SweepShapeKind : byte
    {
        Sphere = 1,
        Capsule = 2,
        Box = 4,
        ConvexHull = 8,
        ConvexMesh = 16
    }

    [Flags]
    public enum SweepMotionModel : byte
    {
        PointPath = 1,
        ShapeTranslation = 2,
        RigidRotation = 4
    }

    [Flags]
    public enum SweepTargetModel : byte
    {
        StationaryPose = 2,
        LinearTranslation = 4,
        RigidRotation = 8
    }

    public enum SweepContactKind : byte
    {
        InitialOverlap = 1,
        Sweep = 2
    }

    [Flags]
    public enum SweepContactFields : byte
    {
        None = 0,
        Time = 1,
        Point = 2,
        Normal = 4
    }

    public enum SweepQueryStatus : byte
    {
        Available = 0,
        Writing = 1,
        Complete = 2,
        CapacityExceeded = 3,
        UnsupportedShape = 4,
        UnsupportedMotion = 5,
        TargetTimeMismatch = 6,
        NativeIterationLimit = 8
    }

    public readonly struct SweepPose
    {
        public SweepPose(FPVector3 position, FPQuaternion orientation)
        {
            Position = position;
            Orientation = orientation;
        }
        public FPVector3 Position { get; }
        public FPQuaternion Orientation { get; }
    }

    public readonly struct SweepShape
    {
        readonly FPVector3[] m_Vertices;
        readonly SweepConvexPart[] m_Parts;

        SweepShape(ulong id, SweepShapeKind kind, FP radius, FP halfLength, FPVector3 halfExtents,
            FPVector3[] vertices = null, SweepConvexPart[] parts = null)
        {
            Id = id;
            Kind = kind;
            Radius = radius;
            HalfLength = halfLength;
            HalfExtents = halfExtents;
            m_Vertices = vertices;
            m_Parts = parts;
        }
        public ulong Id { get; }
        public SweepShapeKind Kind { get; }
        public FP Radius { get; }
        public FP HalfLength { get; }
        public FPVector3 HalfExtents { get; }
        public ReadOnlySpan<FPVector3> Vertices => m_Vertices;
        public ReadOnlySpan<SweepConvexPart> Parts => m_Parts;
        public static SweepShape Sphere(ulong id, FP radius) => new SweepShape(id, SweepShapeKind.Sphere, radius, default, default);
        public static SweepShape Capsule(ulong id, FP radius, FP halfLength) => new SweepShape(id, SweepShapeKind.Capsule, radius, halfLength, default);
        public static SweepShape Box(ulong id, FPVector3 halfExtents) => new SweepShape(id, SweepShapeKind.Box, default, default, halfExtents);
        public static SweepShape ConvexHull(ulong id, ReadOnlySpan<FPVector3> vertices) =>
            new SweepShape(id, SweepShapeKind.ConvexHull, default, default, default, vertices.ToArray());
        public static SweepShape ConvexMesh(ulong id, ReadOnlySpan<SweepConvexPart> parts) =>
            new SweepShape(id, SweepShapeKind.ConvexMesh, default, default, default, parts: parts.ToArray());
    }

    public readonly struct SweepConvexPart
    {
        public SweepConvexPart(int shapeIndex, SweepPose localPose)
        {
            ShapeIndex = shapeIndex;
            LocalPose = localPose;
        }

        public int ShapeIndex { get; }
        public SweepPose LocalPose { get; }
    }

    public readonly struct SweepTrack
    {
        public SweepTrack(ulong id, int shapeIndex, SweepMotionModel motion, string samplingSource)
        {
            Id = id;
            ShapeIndex = shapeIndex;
            Motion = motion;
            SamplingSource = samplingSource;
        }
        public ulong Id { get; }
        public int ShapeIndex { get; }
        public SweepMotionModel Motion { get; }
        public string SamplingSource { get; }
    }

    public readonly struct SweepSample
    {
        public SweepSample(FP time, SweepPose pose) { Time = time; Pose = pose; }
        public FP Time { get; }
        public SweepPose Pose { get; }
    }

    public readonly struct SweepSegment
    {
        public SweepSegment(SweepTrack track, SweepSample start, SweepSample end)
        {
            Track = track;
            Start = start;
            End = end;
        }
        public SweepTrack Track { get; }
        public SweepSample Start { get; }
        public SweepSample End { get; }
    }

    public readonly struct SweepTargetIdentity
    {
        public SweepTargetIdentity(ulong targetId, ulong shapeId) { TargetId = targetId; ShapeId = shapeId; }
        public ulong TargetId { get; }
        public ulong ShapeId { get; }
    }

    public readonly struct SweepTarget
    {
        public SweepTarget(SweepTargetIdentity identity, int shapeIndex, int layer, bool isTrigger, SweepSample start, SweepSample end)
        {
            Identity = identity;
            ShapeIndex = shapeIndex;
            Layer = layer;
            IsTrigger = isTrigger;
            Start = start;
            End = end;
        }
        public SweepTargetIdentity Identity { get; }
        public int ShapeIndex { get; }
        public int Layer { get; }
        public bool IsTrigger { get; }
        public SweepSample Start { get; }
        public SweepSample End { get; }
    }

    public readonly struct SweepFilter
    {
        public SweepFilter(int layerMask, bool includeTriggers, ulong excludedTargetId)
        {
            LayerMask = layerMask;
            IncludeTriggers = includeTriggers;
            ExcludedTargetId = excludedTargetId;
        }
        public int LayerMask { get; }
        public bool IncludeTriggers { get; }
        public ulong ExcludedTargetId { get; }
        public bool Includes(ulong targetId, int layer, bool isTrigger) =>
            targetId != ExcludedTargetId && (LayerMask & (1 << layer)) != 0 && (IncludeTriggers || !isTrigger);
    }

    public readonly struct SweepBackendDescriptor
    {
        public SweepBackendDescriptor(string implementationId, SimulationNumericProfile numericProfile, SweepShapeKind shapes, SweepMotionModel motions, SweepTargetModel targets, bool deterministic)
        {
            ImplementationId = implementationId;
            NumericProfile = numericProfile;
            Shapes = shapes;
            Motions = motions;
            Targets = targets;
            Deterministic = deterministic;
        }
        public string ImplementationId { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public SweepShapeKind Shapes { get; }
        public SweepMotionModel Motions { get; }
        public SweepTargetModel Targets { get; }
        public bool Deterministic { get; }
    }

    public readonly ref struct SweepQuery<TSource> where TSource : struct
    {
        public SweepQuery(in TSource source, ReadOnlySpan<SweepSegment> segments, ReadOnlySpan<SweepTarget> targets, SweepTargetModel targetModel, FP targetPoseTime, SweepFilter filter)
        {
            Source = source;
            Segments = segments;
            Targets = targets;
            TargetModel = targetModel;
            TargetPoseTime = targetPoseTime;
            Filter = filter;
        }
        public TSource Source { get; }
        public ReadOnlySpan<SweepSegment> Segments { get; }
        public ReadOnlySpan<SweepTarget> Targets { get; }
        public SweepTargetModel TargetModel { get; }
        public FP TargetPoseTime { get; }
        public SweepFilter Filter { get; }
    }

    public readonly struct SweepCandidate
    {
        public SweepCandidate(SweepTargetIdentity target, ulong trackId, ulong shapeId, int segmentIndex, SweepMotionModel motion, SweepContactKind kind, SweepContactFields fields, FP time, FP fraction, FP targetPoseTime, FPVector3 point, FPVector3 normal, int sourcePartIndex = 0, int targetPartIndex = 0)
        {
            Target = target;
            TrackId = trackId;
            ShapeId = shapeId;
            SegmentIndex = segmentIndex;
            Motion = motion;
            Kind = kind;
            Fields = fields;
            Time = time;
            Fraction = fraction;
            TargetPoseTime = targetPoseTime;
            Point = point;
            Normal = normal;
            SourcePartIndex = sourcePartIndex;
            TargetPartIndex = targetPartIndex;
        }
        public SweepTargetIdentity Target { get; }
        public ulong TrackId { get; }
        public ulong ShapeId { get; }
        public int SegmentIndex { get; }
        public int SourcePartIndex { get; }
        public int TargetPartIndex { get; }
        public SweepMotionModel Motion { get; }
        public SweepContactKind Kind { get; }
        public SweepContactFields Fields { get; }
        public FP Time { get; }
        public FP Fraction { get; }
        public FP TargetPoseTime { get; }
        public FPVector3 Point { get; }
        public FPVector3 Normal { get; }
    }
}
