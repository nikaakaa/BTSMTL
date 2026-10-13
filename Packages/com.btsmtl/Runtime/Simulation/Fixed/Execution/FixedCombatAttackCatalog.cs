using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonGameplay.Configuration;
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
            FixedScalar invalidRadius,
            FixedScalar invalidAngle,
            int followDirectionType,
            int coreDistance,
            bool isSubtractive,
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
            InvalidRadius = invalidRadius;
            InvalidAngle = invalidAngle;
            FollowDirectionType = followDirectionType;
            CoreDistance = coreDistance;
            IsSubtractive = isSubtractive;
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
        public FixedScalar InvalidRadius { get; }
        public FixedScalar InvalidAngle { get; }
        public int FollowDirectionType { get; }
        public int CoreDistance { get; }
        public bool IsSubtractive { get; }
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
            SourceKey = enemy.ContentId;
            HitType = enemy.HitType;
            HitStrengthType = enemy.HitStrengthType;
            CombatTags = Array.Empty<string>();
            IsHeavyAttack = enemy.Heavy;
            FrameHalt = enemy.FrameHalt;
            AttackerFrameHalt = enemy.AttackerFrameHalt;
            GroundHitEffectId = enemy.HitEffectId;
            HitsEnemy = true;
            BanDamage = true;
        }

        public PortableAttackPropertyComponent(
            string sourceKey,
            int hitType,
            int hitStrengthType,
            string[] combatTags,
            PortableMagnitude damagePercentage,
            PortableMagnitude addedDamage,
            PortableMagnitude breakStunRatio,
            PortableMagnitude elementAccumulation,
            PortableMagnitude exhaustedAccumulation,
            PortableMagnitude exhaustedChase,
            int damageElement,
            int damageHitType,
            int damageBreakLevel,
            PortableMagnitude damageBreakLevelProbability,
            int triggerBuffLevel,
            int destructionClass,
            int destructionDurability,
            int overrideDamageStaggerLevel,
            int damageTextId,
            int damageTextWaitMilliseconds,
            int frameHalt,
            int attackerFrameHalt,
            uint groundHitEffectId,
            uint skyHitEffectId,
            uint downHitEffectId,
            string standardConfigKey,
            string abilityTargetKey,
            bool isCauseStun,
            bool isHeavyAttack,
            bool isCauseExhausted,
            bool isHeal,
            bool isIndirect,
            bool hitsEnemy,
            bool hitsAllied,
            bool hitsNeutral,
            bool useAbilityTargetKey,
            bool banDamage)
        {
            SourceKey = SimulationIdentity.Require(sourceKey, nameof(sourceKey));
            HitType = hitType;
            HitStrengthType = hitStrengthType;
            CombatTags = combatTags;
            DamagePercentage = damagePercentage;
            AddedDamage = addedDamage;
            BreakStunRatio = breakStunRatio;
            ElementAccumulation = elementAccumulation;
            ExhaustedAccumulation = exhaustedAccumulation;
            ExhaustedChase = exhaustedChase;
            DamageElement = damageElement;
            DamageHitType = damageHitType;
            DamageBreakLevel = damageBreakLevel;
            DamageBreakLevelProbability = damageBreakLevelProbability;
            TriggerBuffLevel = triggerBuffLevel;
            DestructionClass = destructionClass;
            DestructionDurability = destructionDurability;
            OverrideDamageStaggerLevel = overrideDamageStaggerLevel;
            DamageTextId = damageTextId;
            DamageTextWaitMilliseconds = damageTextWaitMilliseconds;
            FrameHalt = frameHalt;
            AttackerFrameHalt = attackerFrameHalt;
            GroundHitEffectId = groundHitEffectId;
            SkyHitEffectId = skyHitEffectId;
            DownHitEffectId = downHitEffectId;
            StandardConfigKey = standardConfigKey;
            AbilityTargetKey = abilityTargetKey;
            IsCauseStun = isCauseStun;
            IsHeavyAttack = isHeavyAttack;
            IsCauseExhausted = isCauseExhausted;
            IsHeal = isHeal;
            IsIndirect = isIndirect;
            HitsEnemy = hitsEnemy;
            HitsAllied = hitsAllied;
            HitsNeutral = hitsNeutral;
            UseAbilityTargetKey = useAbilityTargetKey;
            BanDamage = banDamage;
        }

        public string SourceKey { get; }
        public int HitType { get; }
        public int HitStrengthType { get; }
        public string[] CombatTags { get; }
        public PortableMagnitude DamagePercentage { get; }
        public PortableMagnitude AddedDamage { get; }
        public PortableMagnitude BreakStunRatio { get; }
        public PortableMagnitude ElementAccumulation { get; }
        public PortableMagnitude ExhaustedAccumulation { get; }
        public PortableMagnitude ExhaustedChase { get; }
        public int DamageElement { get; }
        public int DamageHitType { get; }
        public int DamageBreakLevel { get; }
        public PortableMagnitude DamageBreakLevelProbability { get; }
        public int TriggerBuffLevel { get; }
        public int DestructionClass { get; }
        public int DestructionDurability { get; }
        public int OverrideDamageStaggerLevel { get; }
        public int DamageTextId { get; }
        public int DamageTextWaitMilliseconds { get; }
        public int FrameHalt { get; }
        public int AttackerFrameHalt { get; }
        public uint GroundHitEffectId { get; }
        public uint SkyHitEffectId { get; }
        public uint DownHitEffectId { get; }
        public string StandardConfigKey { get; }
        public string AbilityTargetKey { get; }
        public bool IsCauseStun { get; }
        public bool IsHeavyAttack { get; }
        public bool IsCauseExhausted { get; }
        public bool IsHeal { get; }
        public bool IsIndirect { get; }
        public bool HitsEnemy { get; }
        public bool HitsAllied { get; }
        public bool HitsNeutral { get; }
        public bool UseAbilityTargetKey { get; }
        public bool BanDamage { get; }
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
            foreach (PublishedAttack attack in Content.Attacks)
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
        static FixedVector3 Vector(PublishedVector3 value) => new FixedVector3(Scalar(value.X), Scalar(value.Y), Scalar(value.Z));
        static SweepVector3<FixedScalar> SweepVector(PublishedVector3 value) =>
            new SweepVector3<FixedScalar>(Scalar(value.X), Scalar(value.Y), Scalar(value.Z));

        static PortableAttackCollisionComponent Collision(PublishedAttack attack,
            Dictionary<string, CombatAttackGeometryContent> geometries)
        {
        PublishedAttackCollision value = attack.Collision;
        var result = new PortableAttackCollisionComponent((PortableAttackCollisionKind)value.Kind, Vector(value.CenterOffset),
            Scalar(value.Width), Scalar(value.Height), Scalar(value.Depth), Scalar(value.FanAngle),
            Scalar(value.Radius), Scalar(value.InvalidRadius), Scalar(value.InvalidAngle), value.FollowDirectionType,
            value.CoreDistance, value.IsSubtractive, Scalar(value.HitInterval), value.AliveMaxHitCount,
            value.UnitMaxHitCount, value.FollowAttacker);
        if (value.Kind == CollisionKind.GeometrySweep)
            result.Geometry = Geometry(geometries[value.Geometry]);
        return result;
        }

        static PortableAttackPropertyComponent Property(PublishedAttack attack)
        {
        PublishedAttackProperty value = attack.Property;
        return new PortableAttackPropertyComponent(value.SourceKey, value.HitType, value.HitStrengthType,
            value.CombatTags.ToArray(), PortableMagnitude.FromConfiguration(value.DamagePercentage), PortableMagnitude.FromConfiguration(value.AddedDamage),
            PortableMagnitude.FromConfiguration(value.BreakStunRatio), PortableMagnitude.FromConfiguration(value.ElementAccumulation), PortableMagnitude.FromConfiguration(value.ExhaustedAccumulation),
            PortableMagnitude.FromConfiguration(value.ExhaustedChase), value.DamageElement, value.DamageHitType, value.DamageBreakLevel,
            PortableMagnitude.FromConfiguration(value.DamageBreakLevelProbability), value.TriggerBuffLevel, value.DestructionClass,
            value.DestructionDurability, value.OverrideDamageStaggerLevel, value.DamageTextId,
            value.DamageTextWaitMilliseconds, value.FrameHalt, value.AttackerFrameHalt, value.GroundHitEffectId,
            value.SkyHitEffectId, value.DownHitEffectId, value.StandardConfigKey, value.AbilityTargetKey,
            value.IsCauseStun, value.IsHeavyAttack, value.IsCauseExhausted, value.IsHeal, value.IsIndirect,
            value.HitsEnemy, value.HitsAllied, value.HitsNeutral, value.UseAbilityTargetKey, value.BanDamage);
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
