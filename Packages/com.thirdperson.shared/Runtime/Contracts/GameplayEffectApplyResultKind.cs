namespace ThirdPersonSimulation
{
    public enum GameplayEffectApplyResultKind : byte
    {
        Applied = 1,
        Rejected = 2,
        MissingDefinition = 3,
        InvalidContext = 4,
        InvalidPrediction = 5,
        MissingParameter = 6,
        UndeclaredParameter = 7,
        RequirementFailed = 8,
        OverflowRejected = 9,
        DefinitionRevisionMismatch = 10,
        OverflowEffectsApplied = 11,
        CapacityExceeded = 12,
        ExecutionFailed = 13
    }
}
