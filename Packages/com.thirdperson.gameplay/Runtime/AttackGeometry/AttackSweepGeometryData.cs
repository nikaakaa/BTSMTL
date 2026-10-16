using Photon.Deterministic;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation
{
    public sealed class AttackSweepPartData
    {
        public AttackSweepPartData(string id, SweepShapeKind kind, FPVector3 halfExtents,
            FP radius, FP halfLength, FPVector3[] vertices, SweepSample[] samples)
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
        public FPVector3 HalfExtents { get; }
        public FP Radius { get; }
        public FP HalfLength { get; }
        public FPVector3[] Vertices { get; }
        public SweepSample[] Samples { get; }
    }

    public sealed class AttackSweepGeometryData
    {
        public AttackSweepGeometryData(string sourceIdentity, int segmentCapacity,
            FP interpolationError, AttackSweepPartData[] parts)
        {
            SourceIdentity = sourceIdentity;
            SegmentCapacity = segmentCapacity;
            InterpolationError = interpolationError;
            Parts = parts;
        }
        public string SourceIdentity { get; }
        public int SegmentCapacity { get; }
        public FP InterpolationError { get; }
        public AttackSweepPartData[] Parts { get; }
    }
}
