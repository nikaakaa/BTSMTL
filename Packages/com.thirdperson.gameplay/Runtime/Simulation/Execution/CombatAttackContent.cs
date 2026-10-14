namespace ThirdPersonSimulation
{
    public enum CombatAttackCollisionKind : byte
    {
        Box = 0,
        BoxContinuous = 1,
        FanWithHeight = 2,
        GeometrySweep = 3
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct CombatVector3
    {
        public CombatVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackCollisionContent
    {
        public CombatAttackCollisionContent(CombatAttackCollisionKind kind, CombatVector3 centerOffset, float width, float height, float depth, float fanAngle, float radius, float hitInterval, int aliveMaxHitCount, int unitMaxHitCount, bool followAttacker, string geometry)
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
            Geometry = geometry;
        }

        public CombatAttackCollisionKind Kind { get; }
        public CombatVector3 CenterOffset { get; }
        public float Width { get; }
        public float Height { get; }
        public float Depth { get; }
        public float FanAngle { get; }
        public float Radius { get; }
        public float HitInterval { get; }
        public int AliveMaxHitCount { get; }
        public int UnitMaxHitCount { get; }
        public bool FollowAttacker { get; }
        public string Geometry { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackPropertyContent
    {
        public CombatAttackPropertyContent(int hitType, int hitStrengthType, int damageBreakLevel, int frameHalt, int attackerFrameHalt, uint groundHitEffectId, uint skyHitEffectId, bool isHeavyAttack, bool hitsEnemy, bool hitsAllied, bool hitsNeutral)
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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackCameraContent
    {
        public CombatAttackCameraContent(string resourceId, bool shakeOnNotHit)
        {
            ResourceId = resourceId;
            ShakeOnNotHit = shakeOnNotHit;
        }

        public string ResourceId { get; }
        public bool ShakeOnNotHit { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class CombatAttackContent
    {
        public CombatAttackContent(string attackId, uint definitionRevision, CombatAttackCollisionContent collision, CombatAttackPropertyContent property, CombatAttackCameraContent cameraShake)
        {
            AttackId = attackId;
            DefinitionRevision = definitionRevision;
            Collision = collision;
            Property = property;
            CameraShake = cameraShake;
        }

        public string AttackId { get; }
        public uint DefinitionRevision { get; }
        public CombatAttackCollisionContent Collision { get; }
        public CombatAttackPropertyContent Property { get; }
        public CombatAttackCameraContent CameraShake { get; }
    }
}
