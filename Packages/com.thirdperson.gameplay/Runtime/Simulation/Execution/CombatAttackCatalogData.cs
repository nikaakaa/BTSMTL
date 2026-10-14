using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct CombatAttackGeometryRotation
    {
        public CombatAttackGeometryRotation(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct CombatAttackGeometrySample
    {
        public CombatAttackGeometrySample(float time, CombatVector3 position, CombatAttackGeometryRotation rotation)
        {
            Time = time;
            Position = position;
            Rotation = rotation;
        }

        public float Time { get; }
        public CombatVector3 Position { get; }
        public CombatAttackGeometryRotation Rotation { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackGeometryPartContent
    {
        public CombatAttackGeometryPartContent(string id, int kind, CombatVector3 halfExtents, float radius,
            float halfLength, IReadOnlyList<CombatVector3> vertices, IReadOnlyList<CombatAttackGeometrySample> samples)
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
        public int Kind { get; }
        public CombatVector3 HalfExtents { get; }
        public float Radius { get; }
        public float HalfLength { get; }
        public IReadOnlyList<CombatVector3> Vertices { get; }
        public IReadOnlyList<CombatAttackGeometrySample> Samples { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackGeometryContent
    {
        public CombatAttackGeometryContent(string reference, string preparedIdentity, int maximumSegmentsPerQuery,
            float interpolationError, IReadOnlyList<CombatAttackGeometryPartContent> parts)
        {
            Reference = reference;
            PreparedIdentity = preparedIdentity;
            MaximumSegmentsPerQuery = maximumSegmentsPerQuery;
            InterpolationError = interpolationError;
            Parts = parts;
        }

        public string Reference { get; }
        public string PreparedIdentity { get; }
        public int MaximumSegmentsPerQuery { get; }
        public float InterpolationError { get; }
        public IReadOnlyList<CombatAttackGeometryPartContent> Parts { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackCatalogData
    {
        public const int FormatVersion = 2;

        public CombatAttackCatalogData(int version,
            IReadOnlyList<CombatAttackContent> attacks, IReadOnlyList<CombatAttackGeometryContent> geometries)
        {
            Version = version;
            Attacks = attacks;
            Geometries = geometries;
        }

        public int Version { get; }
        public IReadOnlyList<CombatAttackContent> Attacks { get; }
        public IReadOnlyList<CombatAttackGeometryContent> Geometries { get; }
    }
}
