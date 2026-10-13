using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedPreparedAttack
    {
        public int Index;
        public int ActorIndex;
        public PortableAttackDefinition Definition;
        public string ContentId;
        public PortableAttackCollisionComponent Collision;
        public PortableAttackPropertyComponent Property;
        public PortableAttackCameraShakeComponent Camera;
        public int ShapeIndex;
        public FixedPreparedAttackPart[] Parts;
        public int SegmentCapacity = 1;
        public FixedScalar InterpolationError;
        public StableHash IngressIdentity;
    }

    internal static class FixedCombatGeometryPreparation
    {
        public static FixedPreparedAttack Prepare(int index, int actorIndex,
            PortableAttackDefinition definition, List<SweepShape<FixedScalar>> shapes)
        {
            var result = new FixedPreparedAttack { Index = index, ActorIndex = actorIndex, Definition = definition, ContentId = definition.Id };
            result.Collision = definition.Collision;
            result.Property = definition.Property;
            result.Camera = definition.Camera;
            PortableAttackCollisionComponent pattern = result.Collision;
            if (pattern.IsSubtractive || pattern.FollowDirectionType != 0 || pattern.CoreDistance != 0 ||
                pattern.InvalidRadius != FixedScalar.Zero || pattern.InvalidAngle != FixedScalar.Zero)
                throw new NotSupportedException($"Attack pattern '{definition.Id}' has unsupported subtraction, follow-direction or exclusion semantics.");
            switch (pattern.Kind)
            {
                case PortableAttackCollisionKind.FanWithHeight:
                    result.ShapeIndex = AddFan(shapes, pattern);
                    break;
                case PortableAttackCollisionKind.GeometrySweep:
                    AttackSweepGeometryData<FixedScalar> geometry = pattern.Geometry;
                    result.Parts = new FixedPreparedAttackPart[geometry.Parts.Length];
                    result.SegmentCapacity = geometry.SegmentCapacity;
                    result.InterpolationError = geometry.InterpolationError;
                    for (int part = 0; part < geometry.Parts.Length; part++)
                    {
                        AttackSweepPartData<FixedScalar> data = geometry.Parts[part];
                        int indexInShapes = shapes.Count;
                        ulong id = (ulong)indexInShapes + 1;
                        SweepShape<FixedScalar> shape = data.Kind switch
                        {
                            SweepShapeKind.Box => SweepShape<FixedScalar>.Box(id, data.HalfExtents),
                            SweepShapeKind.Sphere => SweepShape<FixedScalar>.Sphere(id, data.Radius),
                            SweepShapeKind.Capsule => SweepShape<FixedScalar>.Capsule(id, data.Radius, data.HalfLength),
                            SweepShapeKind.ConvexHull => SweepShape<FixedScalar>.ConvexHull(id, data.Vertices),
                            _ => throw new InvalidOperationException("Prepared attack geometry kind is unsupported.")
                        };
                        shapes.Add(shape);
                        result.Parts[part] = new FixedPreparedAttackPart(indexInShapes, data);
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

        public static FixedPreparedAttack Prepare(int index, int actorIndex,
            FixedLocalEnemyDefinition enemy, List<SweepShape<FixedScalar>> shapes)
        {
            var collision = new PortableAttackCollisionComponent(PortableAttackCollisionKind.BoxContinuous,
                enemy.AttackCenter, enemy.AttackSize.X, enemy.AttackSize.Y, enemy.AttackSize.Z,
                FixedScalar.Zero, FixedScalar.Zero, FixedScalar.Zero, FixedScalar.Zero,
                0, 0, false, FixedScalar.Zero, 0, 1, true);
            return new FixedPreparedAttack
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

        static int AddBox(List<SweepShape<FixedScalar>> shapes, PortableAttackCollisionComponent collision)
        {
            if (collision.Width <= FixedScalar.Zero || collision.Height <= FixedScalar.Zero || collision.Depth <= FixedScalar.Zero)
                throw new InvalidOperationException("Attack Box dimensions must be positive.");
            int index = shapes.Count;
            FixedScalar two = FixedScalar.FromInt64(2);
            shapes.Add(SweepShape<FixedScalar>.Box((ulong)index + 1, new SweepVector3<FixedScalar>(
                collision.Width / two, collision.Height / two, collision.Depth / two)));
            return index;
        }

        static int AddFan(List<SweepShape<FixedScalar>> shapes, PortableAttackCollisionComponent collision)
        {
            if (collision.FanAngle <= FixedScalar.Zero || collision.FanAngle > FixedScalar.FromInt64(360) ||
                collision.Radius <= FixedScalar.Zero || collision.Height <= FixedScalar.Zero)
                throw new InvalidOperationException("Attack Fan angle, radius and height are invalid.");
            int partCount = (int)Math.Ceiling(collision.FanAngle.ToDouble() / 90d);
            FixedScalar partAngle = collision.FanAngle / FixedScalar.FromInt64(partCount);
            FixedScalar halfHeight = collision.Height / FixedScalar.FromInt64(2);
            var parts = new SweepConvexPart<FixedScalar>[partCount];
            FixedScalar start = -collision.FanAngle / FixedScalar.FromInt64(2);
            for (int part = 0; part < partCount; part++)
            {
                int arcCount = (int)Math.Ceiling(partAngle.ToDouble() / 5d);
                int ringCount = arcCount + 2;
                var vertices = new SweepVector3<FixedScalar>[ringCount * 2];
                vertices[0] = new SweepVector3<FixedScalar>(FixedScalar.Zero, -halfHeight, FixedScalar.Zero);
                vertices[ringCount] = new SweepVector3<FixedScalar>(FixedScalar.Zero, halfHeight, FixedScalar.Zero);
                for (int point = 0; point <= arcCount; point++)
                {
                    FixedScalar angle = start + partAngle * FixedScalar.FromInt64(point) / FixedScalar.FromInt64(arcCount);
                    FixedAngle.SinCos(new FixedYaw(angle), out FixedScalar sine, out FixedScalar cosine);
                    FixedScalar x = sine * collision.Radius;
                    FixedScalar z = cosine * collision.Radius;
                    vertices[point + 1] = new SweepVector3<FixedScalar>(x, -halfHeight, z);
                    vertices[ringCount + point + 1] = new SweepVector3<FixedScalar>(x, halfHeight, z);
                }
                int shapeIndex = shapes.Count;
                shapes.Add(SweepShape<FixedScalar>.ConvexHull((ulong)shapeIndex + 1, vertices));
                parts[part] = new SweepConvexPart<FixedScalar>(shapeIndex, IdentityPose());
                start += partAngle;
            }
            int meshIndex = shapes.Count;
            shapes.Add(SweepShape<FixedScalar>.ConvexMesh((ulong)meshIndex + 1, parts));
            return meshIndex;
        }

        public static SweepPose<FixedScalar> IdentityPose() => new SweepPose<FixedScalar>(
            default, new SweepQuaternion<FixedScalar>(FixedScalar.Zero, FixedScalar.Zero, FixedScalar.Zero, FixedScalar.One));

        public static SweepPose<FixedScalar> Pose(FixedVector3 position, FixedYaw yaw, FixedVector3 localOffset)
        {
            FixedAngle.SinCos(yaw, out FixedScalar sine, out FixedScalar cosine);
            FixedVector3 worldOffset = new FixedVector3(
                localOffset.X * cosine + localOffset.Z * sine,
                localOffset.Y,
                -localOffset.X * sine + localOffset.Z * cosine);
            FixedAngle.SinCos(new FixedYaw(yaw.Degrees / FixedScalar.FromInt64(2)), out FixedScalar halfSine, out FixedScalar halfCosine);
            FixedVector3 center = position + worldOffset;
            return new SweepPose<FixedScalar>(new SweepVector3<FixedScalar>(center.X, center.Y, center.Z),
                new SweepQuaternion<FixedScalar>(FixedScalar.Zero, halfSine, FixedScalar.Zero, halfCosine));
        }
    }
}
