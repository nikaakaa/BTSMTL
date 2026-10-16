namespace ThirdPersonSimulation
{
    public enum GameplayEffectScopeKind : byte
    {
        Actor = 0,
        ActionInstance = 1
    }

    public enum GameplayEffectLifetimeKind : byte
    {
        Independent = 0,
        Action = 1,
        Invocation = 2
    }

    public enum GameplayEffectLifecycleKind : byte
    {
        Applied = 1,
        Confirmed = 2,
        Rejected = 3,
        StackChanged = 4,
        Inhibited = 5,
        Resumed = 6,
        PeriodExecuted = 7,
        Removed = 8,
        Expired = 9,
        Corrected = 10,
        Overflow = 11
    }

}
