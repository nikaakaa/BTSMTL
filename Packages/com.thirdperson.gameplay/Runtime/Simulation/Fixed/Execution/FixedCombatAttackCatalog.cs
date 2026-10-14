using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    internal enum PortableAttackCollisionKind : byte
    {
        Box = 0,
        BoxContinuous = 1,
        FanWithHeight = 2,
        GeometrySweep = 3
    }

    internal sealed class PortableAttackCameraShakeComponent
    {
        public PortableAttackCameraShakeComponent(string resourceId, bool shakeOnNotHit)
        {
            ResourceId = resourceId;
            ShakeOnNotHit = shakeOnNotHit;
        }

        public string ResourceId { get; }
        public bool ShakeOnNotHit { get; }
    }

    internal sealed class PortableAttackCollisionComponent
    {
        public PortableAttackCollisionComponent(
            PortableAttackCollisionKind kind,
            FixedVector3 centerOffset,
            FixedScalar width,
            FixedScalar height,
            FixedScalar depth,
            FixedScalar fanAngle,
            FixedScalar radius,
            FixedScalar hitInterval,
            int aliveMaxHitCount,
            int unitMaxHitCount,
            bool followAttacker)
        {
            Kind = kind;
            CenterOffset = centerOffset;
            Width = width;
            Height = height;
            Depth = depth;
            FanAngle = fanAngle;
            Radius = radius;
            HitInterval = hitInterval;
            AliveMaxHitCount = aliveMaxHitCount;
            UnitMaxHitCount = unitMaxHitCount;
            FollowAttacker = followAttacker;
        }

        public PortableAttackCollisionKind Kind { get; }
        public FixedVector3 CenterOffset { get; }
        public FixedScalar Width { get; }
        public FixedScalar Height { get; }
        public FixedScalar Depth { get; }
        public FixedScalar FanAngle { get; }
        public FixedScalar Radius { get; }
        public FixedScalar HitInterval { get; }
        public int AliveMaxHitCount { get; }
        public int UnitMaxHitCount { get; }
        public bool FollowAttacker { get; }
        public AttackSweepGeometryData<FixedScalar> Geometry { get; internal set; }
    }

    internal sealed class PortableAttackPropertyComponent
    {
        internal PortableAttackPropertyComponent(FixedLocalEnemyDefinition enemy)
        {
            HitType = enemy.HitType;
            HitStrengthType = enemy.HitStrengthType;
            IsHeavyAttack = enemy.Heavy;
            FrameHalt = enemy.FrameHalt;
            AttackerFrameHalt = enemy.AttackerFrameHalt;
            GroundHitEffectId = enemy.HitEffectId;
            HitsEnemy = true;
        }

        public PortableAttackPropertyComponent(int hitType, int hitStrengthType, int damageBreakLevel, int frameHalt, int attackerFrameHalt, uint groundHitEffectId, uint skyHitEffectId, bool isHeavyAttack, bool hitsEnemy, bool hitsAllied, bool hitsNeutral)
        {
            HitType = hitType;
            HitStrengthType = hitStrengthType;
            DamageBreakLevel = damageBreakLevel;
            FrameHalt = frameHalt;
            AttackerFrameHalt = attackerFrameHalt;
            GroundHitEffectId = groundHitEffectId;
            SkyHitEffectId = skyHitEffectId;
            IsHeavyAttack = isHeavyAttack;
            HitsEnemy = hitsEnemy;
            HitsAllied = hitsAllied;
            HitsNeutral = hitsNeutral;
        }

        public int HitType { get; }
        public int HitStrengthType { get; }
        public int DamageBreakLevel { get; }
        public int FrameHalt { get; }
        public int AttackerFrameHalt { get; }
        public uint GroundHitEffectId { get; }
        public uint SkyHitEffectId { get; }
        public bool IsHeavyAttack { get; }
        public bool HitsEnemy { get; }
        public bool HitsAllied { get; }
        public bool HitsNeutral { get; }
    }

    internal sealed class PortableAttackDefinition
    {
        public PortableAttackDefinition(string id, uint revision, PortableAttackCollisionComponent collision,
            PortableAttackPropertyComponent property, PortableAttackCameraShakeComponent camera)
        {
            Id = id;
            Revision = revision;
            Collision = collision;
            Property = property;
            Camera = camera;
        }

        public string Id { get; }
        public uint Revision { get; }
        public PortableAttackCollisionComponent Collision { get; }
        public PortableAttackPropertyComponent Property { get; }
        public PortableAttackCameraShakeComponent Camera { get; }
    }

    internal sealed class FixedCombatAttackCatalog
    {
        readonly Dictionary<string, PortableAttackDefinition> m_Attacks = new(StringComparer.Ordinal);
        readonly Dictionary<CanonicalTextValue, PortableAttackDefinition> m_TextAttacks = new();

        public FixedCombatAttackCatalog(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            CombatAttackCatalogData.CanonicalRequireSchema(reader);
            Content = CombatAttackCatalogData.CanonicalReadMaterialized(reader);
            reader.RequireComplete();
            if (Content.Version != CombatAttackCatalogData.FormatVersion)
                throw new InvalidDataException("Combat attack content version is unsupported.");
            var geometries = Content.Geometries.ToDictionary(value => value.Reference, StringComparer.Ordinal);
            foreach (CombatAttackContent attack in Content.Attacks)
            {
                var definition = new PortableAttackDefinition("attack:" + attack.AttackId, attack.DefinitionRevision,
                    Collision(attack, geometries), Property(attack),
                    new PortableAttackCameraShakeComponent(attack.CameraShake.ResourceId, attack.CameraShake.ShakeOnNotHit));
                m_Attacks.Add(definition.Id, definition);
                m_TextAttacks.Add(CanonicalTextValue.FromString(definition.Id), definition);
            }
        }

        public CombatAttackCatalogData Content { get; }
        public PortableAttackDefinition RequireAttack(string id) => m_Attacks[id.StartsWith("attack:", StringComparison.Ordinal) ? id : "attack:" + id];
        public PortableAttackDefinition RequireAttack(CanonicalTextValue id) => m_TextAttacks[id];

        static FixedScalar Scalar(float value) => FixedScalar.FromDouble(value);
        static FixedVector3 Vector(CombatVector3 value) => new FixedVector3(Scalar(value.X), Scalar(value.Y), Scalar(value.Z));
        static SweepVector3<FixedScalar> SweepVector(CombatVector3 value) =>
            new SweepVector3<FixedScalar>(Scalar(value.X), Scalar(value.Y), Scalar(value.Z));

        static PortableAttackCollisionComponent Collision(CombatAttackContent attack,
            Dictionary<string, CombatAttackGeometryContent> geometries)
        {
        CombatAttackCollisionContent value = attack.Collision;
        var result = new PortableAttackCollisionComponent((PortableAttackCollisionKind)value.Kind, Vector(value.CenterOffset),
            Scalar(value.Width), Scalar(value.Height), Scalar(value.Depth), Scalar(value.FanAngle),
            Scalar(value.Radius), Scalar(value.HitInterval), value.AliveMaxHitCount,
            value.UnitMaxHitCount, value.FollowAttacker);
        if (value.Kind == CombatAttackCollisionKind.GeometrySweep)
            result.Geometry = Geometry(geometries[value.Geometry]);
        return result;
        }

        static PortableAttackPropertyComponent Property(CombatAttackContent attack)
        {
        CombatAttackPropertyContent value = attack.Property;
        return new PortableAttackPropertyComponent(value.HitType, value.HitStrengthType, value.DamageBreakLevel, value.FrameHalt, value.AttackerFrameHalt, value.GroundHitEffectId, value.SkyHitEffectId, value.IsHeavyAttack, value.HitsEnemy, value.HitsAllied, value.HitsNeutral);
        }

        static AttackSweepGeometryData<FixedScalar> Geometry(CombatAttackGeometryContent source) =>
            new AttackSweepGeometryData<FixedScalar>(source.PreparedIdentity, source.MaximumSegmentsPerQuery,
                Scalar(source.InterpolationError), source.Parts.Select(part =>
        new AttackSweepPartData<FixedScalar>(part.Id, (SweepShapeKind)part.Kind, SweepVector(part.HalfExtents),
            Scalar(part.Radius), Scalar(part.HalfLength), part.Vertices.Select(SweepVector).ToArray(),
            part.Samples.Select(sample => new SweepSample<FixedScalar>(Scalar(sample.Time),
                new SweepPose<FixedScalar>(SweepVector(sample.Position),
                    new SweepQuaternion<FixedScalar>(Scalar(sample.Rotation.X), Scalar(sample.Rotation.Y),
                        Scalar(sample.Rotation.Z), Scalar(sample.Rotation.W))))).ToArray())).ToArray());
    }
}
