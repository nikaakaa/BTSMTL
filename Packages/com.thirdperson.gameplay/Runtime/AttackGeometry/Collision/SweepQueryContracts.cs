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
        ScenePose = 1,
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
        UnmappedCollider = 7,
        NativeIterationLimit = 8
    }

    public readonly struct SweepVector3<T> where T : struct
    {
        public SweepVector3(T x, T y, T z) { X = x; Y = y; Z = z; }
        public T X { get; }
        public T Y { get; }
        public T Z { get; }
    }

    public readonly struct SweepQuaternion<T> where T : struct
    {
        public SweepQuaternion(T x, T y, T z, T w) { X = x; Y = y; Z = z; W = w; }
        public T X { get; }
        public T Y { get; }
        public T Z { get; }
        public T W { get; }
    }

    public readonly struct SweepPose<T> where T : struct
    {
        public SweepPose(SweepVector3<T> position, SweepQuaternion<T> orientation)
        {
            Position = position;
            Orientation = orientation;
        }
        public SweepVector3<T> Position { get; }
        public SweepQuaternion<T> Orientation { get; }
    }

    public readonly struct SweepShape<T> where T : struct
    {
        readonly SweepVector3<T>[] m_Vertices;
        readonly SweepConvexPart<T>[] m_Parts;

        SweepShape(ulong id, SweepShapeKind kind, T radius, T halfLength, SweepVector3<T> halfExtents,
            SweepVector3<T>[] vertices = null, SweepConvexPart<T>[] parts = null)
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
        public T Radius { get; }
        public T HalfLength { get; }
        public SweepVector3<T> HalfExtents { get; }
        public ReadOnlySpan<SweepVector3<T>> Vertices => m_Vertices;
        public ReadOnlySpan<SweepConvexPart<T>> Parts => m_Parts;
        public static SweepShape<T> Sphere(ulong id, T radius) => new SweepShape<T>(id, SweepShapeKind.Sphere, radius, default, default);
        public static SweepShape<T> Capsule(ulong id, T radius, T halfLength) => new SweepShape<T>(id, SweepShapeKind.Capsule, radius, halfLength, default);
        public static SweepShape<T> Box(ulong id, SweepVector3<T> halfExtents) => new SweepShape<T>(id, SweepShapeKind.Box, default, default, halfExtents);
        public static SweepShape<T> ConvexHull(ulong id, ReadOnlySpan<SweepVector3<T>> vertices) =>
            new SweepShape<T>(id, SweepShapeKind.ConvexHull, default, default, default, vertices.ToArray());
        public static SweepShape<T> ConvexMesh(ulong id, ReadOnlySpan<SweepConvexPart<T>> parts) =>
            new SweepShape<T>(id, SweepShapeKind.ConvexMesh, default, default, default, parts: parts.ToArray());
    }

    public readonly struct SweepConvexPart<T> where T : struct
    {
        public SweepConvexPart(int shapeIndex, SweepPose<T> localPose)
        {
            ShapeIndex = shapeIndex;
            LocalPose = localPose;
        }

        public int ShapeIndex { get; }
        public SweepPose<T> LocalPose { get; }
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

    public readonly struct SweepSample<T> where T : struct
    {
        public SweepSample(T time, SweepPose<T> pose) { Time = time; Pose = pose; }
        public T Time { get; }
        public SweepPose<T> Pose { get; }
    }

    public readonly struct SweepSegment<T> where T : struct
    {
        public SweepSegment(SweepTrack track, SweepSample<T> start, SweepSample<T> end)
        {
            Track = track;
            Start = start;
            End = end;
        }
        public SweepTrack Track { get; }
        public SweepSample<T> Start { get; }
        public SweepSample<T> End { get; }
    }

    public readonly struct SweepTargetIdentity
    {
        public SweepTargetIdentity(ulong targetId, ulong shapeId) { TargetId = targetId; ShapeId = shapeId; }
        public ulong TargetId { get; }
        public ulong ShapeId { get; }
    }

    public readonly struct SweepTarget<T> where T : struct
    {
        public SweepTarget(SweepTargetIdentity identity, int shapeIndex, int layer, bool isTrigger, SweepSample<T> start, SweepSample<T> end)
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
        public SweepSample<T> Start { get; }
        public SweepSample<T> End { get; }
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

    public readonly ref struct SweepQuery<T, TSource> where T : struct where TSource : struct
    {
        public SweepQuery(in TSource source, ReadOnlySpan<SweepSegment<T>> segments, ReadOnlySpan<SweepTarget<T>> targets, SweepTargetModel targetModel, T targetPoseTime, SweepFilter filter)
        {
            Source = source;
            Segments = segments;
            Targets = targets;
            TargetModel = targetModel;
            TargetPoseTime = targetPoseTime;
            Filter = filter;
        }
        public TSource Source { get; }
        public ReadOnlySpan<SweepSegment<T>> Segments { get; }
        public ReadOnlySpan<SweepTarget<T>> Targets { get; }
        public SweepTargetModel TargetModel { get; }
        public T TargetPoseTime { get; }
        public SweepFilter Filter { get; }
    }

    public interface ISweepQueryBackend<T> where T : struct, IComparable<T>
    {
        SweepBackendDescriptor Descriptor { get; }
        SweepQueryStatus SweepAll<TSource>(in SweepQuery<T, TSource> query, SweepResultBuffer<T, TSource> results) where TSource : struct;
    }

    public readonly struct SweepCandidate<T> where T : struct
    {
        public SweepCandidate(SweepTargetIdentity target, ulong trackId, ulong shapeId, int segmentIndex, SweepMotionModel motion, SweepContactKind kind, SweepContactFields fields, T time, T fraction, T targetPoseTime, SweepVector3<T> point, SweepVector3<T> normal, int sourcePartIndex = 0, int targetPartIndex = 0)
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
        public T Time { get; }
        public T Fraction { get; }
        public T TargetPoseTime { get; }
        public SweepVector3<T> Point { get; }
        public SweepVector3<T> Normal { get; }
    }
}
