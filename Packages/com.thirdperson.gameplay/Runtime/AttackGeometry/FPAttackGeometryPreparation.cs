using System;
using System.Collections.Generic;
using Photon.Deterministic;
using ThirdPersonSimulation.Collision;
using ThirdPersonSimulation.QuantumNumerics;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class FPAttackGeometryPreparation
    {
        public static FPPreparedAttackPart[] PrepareParts(AttackSweepGeometryData<FP> geometry, List<SweepShape<FP>> shapes)
        {
            var parts = new FPPreparedAttackPart[geometry.Parts.Length];
            for (int part = 0; part < geometry.Parts.Length; part++)
            {
                AttackSweepPartData<FP> data = geometry.Parts[part];
                int indexInShapes = shapes.Count;
                ulong id = (ulong)indexInShapes + 1;
                SweepShape<FP> shape = data.Kind switch
                {
                    SweepShapeKind.Box => SweepShape<FP>.Box(id, data.HalfExtents),
                    SweepShapeKind.Sphere => SweepShape<FP>.Sphere(id, data.Radius),
                    SweepShapeKind.Capsule => SweepShape<FP>.Capsule(id, data.Radius, data.HalfLength),
                    SweepShapeKind.ConvexHull => SweepShape<FP>.ConvexHull(id, data.Vertices),
                    _ => throw new InvalidOperationException("Prepared attack geometry kind is unsupported.")
                };
                shapes.Add(shape);
                parts[part] = new FPPreparedAttackPart(indexInShapes, data);
            }
            return parts;
        }

        public static int AddBox(List<SweepShape<FP>> shapes, FP width, FP height, FP depth)
        {
            if (width <= FP._0 || height <= FP._0 || depth <= FP._0)
                throw new InvalidOperationException("Attack Box dimensions must be positive.");
            int index = shapes.Count;
            FP two = default(QuantumSimulationMath).FromInt64(2);
            shapes.Add(SweepShape<FP>.Box((ulong)index + 1, new SweepVector3<FP>(
                width / two, height / two, depth / two)));
            return index;
        }

        public static int AddFan(List<SweepShape<FP>> shapes, FP fanAngle, FP radius, FP height)
        {
            if (fanAngle <= FP._0 || fanAngle > default(QuantumSimulationMath).FromInt64(360) ||
                radius <= FP._0 || height <= FP._0)
                throw new InvalidOperationException("Attack Fan angle, radius and height are invalid.");
            int partCount = checked((int)((fanAngle.RawValue + 90L * FP.Raw._1 - 1) / (90L * FP.Raw._1)));
            FP partAngle = fanAngle / default(QuantumSimulationMath).FromInt64(partCount);
            FP halfHeight = height / default(QuantumSimulationMath).FromInt64(2);
            var parts = new SweepConvexPart<FP>[partCount];
            FP start = -fanAngle / default(QuantumSimulationMath).FromInt64(2);
            for (int part = 0; part < partCount; part++)
            {
                int arcCount = checked((int)((partAngle.RawValue + 5L * FP.Raw._1 - 1) / (5L * FP.Raw._1)));
                int ringCount = arcCount + 2;
                var vertices = new SweepVector3<FP>[ringCount * 2];
                vertices[0] = new SweepVector3<FP>(FP._0, -halfHeight, FP._0);
                vertices[ringCount] = new SweepVector3<FP>(FP._0, halfHeight, FP._0);
                for (int point = 0; point <= arcCount; point++)
                {
                    FP angle = start + partAngle * default(QuantumSimulationMath).FromInt64(point) / default(QuantumSimulationMath).FromInt64(arcCount);
                    FPMath.SinCos(default(QuantumSimulationMath).Yaw(angle) * FP.Deg2Rad, out FP sine, out FP cosine);
                    FP x = sine * radius;
                    FP z = cosine * radius;
                    vertices[point + 1] = new SweepVector3<FP>(x, -halfHeight, z);
                    vertices[ringCount + point + 1] = new SweepVector3<FP>(x, halfHeight, z);
                }
                int shapeIndex = shapes.Count;
                shapes.Add(SweepShape<FP>.ConvexHull((ulong)shapeIndex + 1, vertices));
                parts[part] = new SweepConvexPart<FP>(shapeIndex, IdentityPose());
                start += partAngle;
            }
            int meshIndex = shapes.Count;
            shapes.Add(SweepShape<FP>.ConvexMesh((ulong)meshIndex + 1, parts));
            return meshIndex;
        }

        public static SweepPose<FP> IdentityPose() => new SweepPose<FP>(
            default, new SweepQuaternion<FP>(FP._0, FP._0, FP._0, FP._1));

        public static SweepPose<FP> Pose(FPVector3 position, FP yaw, FPVector3 localOffset)
        {
            FPMath.SinCos(yaw * FP.Deg2Rad, out FP sine, out FP cosine);
            FPVector3 worldOffset = new FPVector3(
                localOffset.X * cosine + localOffset.Z * sine,
                localOffset.Y,
                -localOffset.X * sine + localOffset.Z * cosine);
            FPMath.SinCos(yaw * FP.Deg2Rad / FP._2, out FP halfSine, out FP halfCosine);
            FPVector3 center = position + worldOffset;
            return new SweepPose<FP>(new SweepVector3<FP>(center.X, center.Y, center.Z),
                new SweepQuaternion<FP>(FP._0, halfSine, FP._0, halfCosine));
        }
    }
}
