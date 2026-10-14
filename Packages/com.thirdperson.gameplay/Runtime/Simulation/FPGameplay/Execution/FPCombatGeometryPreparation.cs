using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPPreparedAttack
    {
        public int Index;
        public int ActorIndex;
        public PortableAttackDefinition Definition;
        public string ContentId;
        public PortableAttackCollisionComponent Collision;
        public PortableAttackPropertyComponent Property;
        public PortableAttackCameraShakeComponent Camera;
        public int ShapeIndex;
        public FPPreparedAttackPart[] Parts;
        public int SegmentCapacity = 1;
        public FP InterpolationError;
        public StableHash IngressIdentity;
    }

    internal static class FPCombatGeometryPreparation
    {
        public static FPPreparedAttack Prepare(int index, int actorIndex,
            PortableAttackDefinition definition, List<SweepShape<FP>> shapes)
        {
            var result = new FPPreparedAttack { Index = index, ActorIndex = actorIndex, Definition = definition, ContentId = definition.Id };
            result.Collision = definition.Collision;
            result.Property = definition.Property;
            result.Camera = definition.Camera;
            PortableAttackCollisionComponent pattern = result.Collision;
            switch (pattern.Kind)
            {
                case PortableAttackCollisionKind.FanWithHeight:
                    result.ShapeIndex = AddFan(shapes, pattern);
                    break;
                case PortableAttackCollisionKind.GeometrySweep:
                    AttackSweepGeometryData<FP> geometry = pattern.Geometry;
                    result.Parts = new FPPreparedAttackPart[geometry.Parts.Length];
                    result.SegmentCapacity = geometry.SegmentCapacity;
                    result.InterpolationError = geometry.InterpolationError;
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
                        result.Parts[part] = new FPPreparedAttackPart(indexInShapes, data);
                    }
                    break;
                default:
                    result.ShapeIndex = AddBox(shapes, pattern);
                    break;
            }
            result.IngressIdentity = StableHash.Compute("combat-accepted-result/1", definition.Id,
                definition.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return result;
        }

        public static FPPreparedAttack Prepare(int index, int actorIndex,
            FPLocalEnemyDefinition enemy, List<SweepShape<FP>> shapes)
        {
            var collision = new PortableAttackCollisionComponent(PortableAttackCollisionKind.BoxContinuous,
                enemy.AttackCenter, enemy.AttackSize.X, enemy.AttackSize.Y, enemy.AttackSize.Z,
                FP._0, FP._0, FP._0, 0, 1, true);
            return new FPPreparedAttack
            {
                Index = index,
                ActorIndex = actorIndex,
                ContentId = enemy.ContentId,
                Collision = collision,
                Property = new PortableAttackPropertyComponent(enemy),
                ShapeIndex = AddBox(shapes, collision),
                IngressIdentity = StableHash.Compute("local-enemy-combat-result/2", enemy.ActorId.ToString(), enemy.ContentId)
            };
        }

        static int AddBox(List<SweepShape<FP>> shapes, PortableAttackCollisionComponent collision)
        {
            if (collision.Width <= FP._0 || collision.Height <= FP._0 || collision.Depth <= FP._0)
                throw new InvalidOperationException("Attack Box dimensions must be positive.");
            int index = shapes.Count;
            FP two = default(QuantumSimulationMath).FromInt64(2);
            shapes.Add(SweepShape<FP>.Box((ulong)index + 1, new SweepVector3<FP>(
                collision.Width / two, collision.Height / two, collision.Depth / two)));
            return index;
        }

        static int AddFan(List<SweepShape<FP>> shapes, PortableAttackCollisionComponent collision)
        {
            if (collision.FanAngle <= FP._0 || collision.FanAngle > default(QuantumSimulationMath).FromInt64(360) ||
                collision.Radius <= FP._0 || collision.Height <= FP._0)
                throw new InvalidOperationException("Attack Fan angle, radius and height are invalid.");
            int partCount = checked((int)((collision.FanAngle.RawValue + 90L * FP.Raw._1 - 1) / (90L * FP.Raw._1)));
            FP partAngle = collision.FanAngle / default(QuantumSimulationMath).FromInt64(partCount);
            FP halfHeight = collision.Height / default(QuantumSimulationMath).FromInt64(2);
            var parts = new SweepConvexPart<FP>[partCount];
            FP start = -collision.FanAngle / default(QuantumSimulationMath).FromInt64(2);
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
                    FP x = sine * collision.Radius;
                    FP z = cosine * collision.Radius;
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
