namespace ThirdPersonSimulation
{
    public enum EquipmentChangeFailure : byte
    {
        None = 0,
        CapabilityDisabled = 1,
        UnknownSlot = 2,
        UnknownEquipment = 3,
        SlotMismatch = 4,
        RequiredSlotCannotBeEmpty = 5,
        PendingChangeExists = 6,
        PendingChangeMissing = 7,
        ChangeIdentityMismatch = 8,
        StaleRevision = 9,
        ActionConflict = 10,
        ContributionRejected = 11,
        CommandBatchCapacityExceeded = 12
    }
}
