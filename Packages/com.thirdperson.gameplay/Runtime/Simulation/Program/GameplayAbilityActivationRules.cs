namespace ThirdPersonSimulation
{
    public enum GameplayAbilityRulePhase : byte
    {
        BeforeAbility = 0,
        AfterAbility = 1
    }

    public enum GameplayAbilityRuleContinuation : byte
    {
        Continue = 0,
        EndPhase = 1
    }

    public enum GameplayAbilityRuleTargetPolicy : byte
    {
        Grant = 0,
        SelectedSource = 1
    }

    public enum GameplayAbilityAvailabilityReason : byte
    {
        NoInput = 0,
        Allowed = 1,
        NoCandidate = 2,
        SourceMissing = 3,
        NotApplicable = 4,
        ConditionRejected = 5,
        AdmissionRejected = 6
    }

}
