using Photon.Deterministic;
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
                    result.ShapeIndex = FPAttackGeometryPreparation.AddFan(shapes, pattern.FanAngle, pattern.Radius, pattern.Height);
                    break;
                case PortableAttackCollisionKind.GeometrySweep:
                    AttackSweepGeometryData<FP> geometry = pattern.Geometry;
                    result.Parts = FPAttackGeometryPreparation.PrepareParts(geometry, shapes);
                    result.SegmentCapacity = geometry.SegmentCapacity;
                    result.InterpolationError = geometry.InterpolationError;
                    break;
                default:
                    result.ShapeIndex = FPAttackGeometryPreparation.AddBox(shapes, pattern.Width, pattern.Height, pattern.Depth);
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
                ShapeIndex = FPAttackGeometryPreparation.AddBox(shapes, collision.Width, collision.Height, collision.Depth),
                IngressIdentity = StableHash.Compute("local-enemy-combat-result/2", enemy.ActorId.ToString(), enemy.ContentId)
            };
        }
    }
}
