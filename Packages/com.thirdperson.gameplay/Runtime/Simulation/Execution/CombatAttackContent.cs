namespace ThirdPersonSimulation
{
    public enum CombatAttackCollisionKind : byte
    {
        Box = 0,
        BoxContinuous = 1,
        FanWithHeight = 2,
        GeometrySweep = 3
    }

    public static class CombatAttackRuntimeIdentity
    {
        public const string AttackPrefix = "attack:";

        public static string Attack(string value) => GameplayEffectRuntimeIdentity.Normalize(value, AttackPrefix, "CombatAttack");
    }
}
