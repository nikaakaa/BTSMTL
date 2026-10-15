using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation
{
    public sealed class AttackSweepPartData<T> where T : struct
    {
        public AttackSweepPartData(string id, SweepShapeKind kind, SweepVector3<T> halfExtents,
            T radius, T halfLength, SweepVector3<T>[] vertices, SweepSample<T>[] samples)
        {
            Id = id;
            Kind = kind;
            HalfExtents = halfExtents;
            Radius = radius;
            HalfLength = halfLength;
            Vertices = vertices;
            Samples = samples;
        }
        public string Id { get; }
        public SweepShapeKind Kind { get; }
        public SweepVector3<T> HalfExtents { get; }
        public T Radius { get; }
        public T HalfLength { get; }
        public SweepVector3<T>[] Vertices { get; }
        public SweepSample<T>[] Samples { get; }
    }

    public sealed class AttackSweepGeometryData<T> where T : struct
    {
        public AttackSweepGeometryData(string sourceIdentity, int segmentCapacity,
            T interpolationError, AttackSweepPartData<T>[] parts)
        {
            SourceIdentity = sourceIdentity;
            SegmentCapacity = segmentCapacity;
            InterpolationError = interpolationError;
            Parts = parts;
        }
        public string SourceIdentity { get; }
        public int SegmentCapacity { get; }
        public T InterpolationError { get; }
        public AttackSweepPartData<T>[] Parts { get; }
    }
}
