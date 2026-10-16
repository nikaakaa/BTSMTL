using System;
using System.Collections.Generic;
using Photon.Deterministic;
using ThirdPersonSimulation.Collision;
using ThirdPersonSimulation.QuantumNumerics;

namespace ThirdPersonSimulation.AttackGeometry
{
    public static class FPAttackGeometryPreparation
    {
        public static FPPreparedAttackPart[] PrepareParts(AttackSweepGeometryData geometry, List<SweepShape> shapes)
        {
            var parts = new FPPreparedAttackPart[geometry.Parts.Length];
            for (int part = 0; part < geometry.Parts.Length; part++)
            {
                AttackSweepPartData data = geometry.Parts[part];
                int indexInShapes = shapes.Count;
                ulong id = (ulong)indexInShapes + 1;
                SweepShape shape = data.Kind switch
                {
                    SweepShapeKind.Box => SweepShape.Box(id, data.HalfExtents),
                    SweepShapeKind.Sphere => SweepShape.Sphere(id, data.Radius),
                    SweepShapeKind.Capsule => SweepShape.Capsule(id, data.Radius, data.HalfLength),
                    SweepShapeKind.ConvexHull => SweepShape.ConvexHull(id, data.Vertices),
                    _ => throw new InvalidOperationException("Prepared attack geometry kind is unsupported.")
                };
                shapes.Add(shape);
                parts[part] = new FPPreparedAttackPart(indexInShapes, data);
            }
            return parts;
        }

        public static int AddBox(List<SweepShape> shapes, FP width, FP height, FP depth)
        {
            if (width <= FP._0 || height <= FP._0 || depth <= FP._0)
                throw new InvalidOperationException("Attack Box dimensions must be positive.");
            int index = shapes.Count;
            FP two = default(QuantumSimulationMath).FromInt64(2);
            shapes.Add(SweepShape.Box((ulong)index + 1, new FPVector3(
                width / two, height / two, depth / two)));
            return index;
        }

        public static int AddFan(List<SweepShape> shapes, FP fanAngle, FP radius, FP height)
        {
            if (fanAngle <= FP._0 || fanAngle > default(QuantumSimulationMath).FromInt64(360) ||
                radius <= FP._0 || height <= FP._0)
                throw new InvalidOperationException("Attack Fan angle, radius and height are invalid.");
            int partCount = checked((int)((fanAngle.RawValue + 90L * FP.Raw._1 - 1) / (90L * FP.Raw._1)));
            FP partAngle = fanAngle / default(QuantumSimulationMath).FromInt64(partCount);
            FP halfHeight = height / default(QuantumSimulationMath).FromInt64(2);
            var parts = new SweepConvexPart[partCount];
            FP start = -fanAngle / default(QuantumSimulationMath).FromInt64(2);
            for (int part = 0; part < partCount; part++)
            {
                int arcCount = checked((int)((partAngle.RawValue + 5L * FP.Raw._1 - 1) / (5L * FP.Raw._1)));
                int ringCount = arcCount + 2;
                var vertices = new FPVector3[ringCount * 2];
                vertices[0] = new FPVector3(FP._0, -halfHeight, FP._0);
                vertices[ringCount] = new FPVector3(FP._0, halfHeight, FP._0);
                for (int point = 0; point <= arcCount; point++)
                {
                    FP angle = start + partAngle * default(QuantumSimulationMath).FromInt64(point) / default(QuantumSimulationMath).FromInt64(arcCount);
                    FPMath.SinCos(default(QuantumSimulationMath).Yaw(angle) * FP.Deg2Rad, out FP sine, out FP cosine);
                    FP x = sine * radius;
                    FP z = cosine * radius;
                    vertices[point + 1] = new FPVector3(x, -halfHeight, z);
                    vertices[ringCount + point + 1] = new FPVector3(x, halfHeight, z);
                }
                int shapeIndex = shapes.Count;
                shapes.Add(SweepShape.ConvexHull((ulong)shapeIndex + 1, vertices));
                parts[part] = new SweepConvexPart(shapeIndex, IdentityPose());
                start += partAngle;
            }
            int meshIndex = shapes.Count;
            shapes.Add(SweepShape.ConvexMesh((ulong)meshIndex + 1, parts));
            return meshIndex;
        }

        public static SweepPose IdentityPose() => new SweepPose(default, FPQuaternion.Identity);

        public static SweepPose Pose(FPVector3 position, FP yaw, FPVector3 localOffset)
        {
            FPMath.SinCos(yaw * FP.Deg2Rad, out FP sine, out FP cosine);
            FPVector3 worldOffset = new FPVector3(
                localOffset.X * cosine + localOffset.Z * sine,
                localOffset.Y,
                -localOffset.X * sine + localOffset.Z * cosine);
            FPMath.SinCos(yaw * FP.Deg2Rad / FP._2, out FP halfSine, out FP halfCosine);
            FPVector3 center = position + worldOffset;
            return new SweepPose(center,
                new FPQuaternion(FP._0, halfSine, FP._0, halfCosine));
        }
    }
}
