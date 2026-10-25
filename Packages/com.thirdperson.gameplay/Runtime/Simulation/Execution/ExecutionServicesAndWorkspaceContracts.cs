namespace ThirdPersonSimulation
{
    public enum ActionAdmissionRejectReason : byte
    {
        None = 0,
        TargetBlocked = 1,
        ActiveSourceNotCancelable = 2,
        SourceActionStillActive = 3,
        TargetSnapshotRequired = 4,
        RequiredTagsMissing = 5,
        ActionCapacityExceeded = 6,
        ReplacementSourceMissing = 7,
        StartupRequirementsFailed = 8,
        ActivationRequestPending = 9,
        ActorAbilityBlocked = 10,
        SourceAbilityBlocked = 11,
        ReplacementSourceRequirementFailed = 12,
        OpportunityUnavailable = 13,
        ScenePointUnavailable = 14,
        ScenePointTagsRejected = 15,
        TargetOutOfRange = 16,
        TargetOccluded = 17
    }

}
