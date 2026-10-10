namespace ThirdPersonSimulation
{
    public readonly struct EquipmentLifecycleFact
    {
        public EquipmentLifecycleFact(EquipmentSlotId slotId, EquipmentId beforeEquipmentId,
            EquipmentId equipmentId, ulong beforeRevision, ulong revision, EquipmentChangeId changeId,
            PendingEquipmentChangeState state)
        {
            SlotId = slotId;
            BeforeEquipmentId = beforeEquipmentId;
            EquipmentId = equipmentId;
            BeforeRevision = beforeRevision;
            Revision = revision;
            ChangeId = changeId;
            State = state;
        }

        public EquipmentSlotId SlotId { get; }
        public EquipmentId BeforeEquipmentId { get; }
        public EquipmentId EquipmentId { get; }
        public ulong BeforeRevision { get; }
        public ulong Revision { get; }
        public EquipmentChangeId ChangeId { get; }
        public PendingEquipmentChangeState State { get; }
        public string StateName => State switch
        {
            PendingEquipmentChangeState.Pending => nameof(PendingEquipmentChangeState.Pending),
            PendingEquipmentChangeState.Committed => nameof(PendingEquipmentChangeState.Committed),
            PendingEquipmentChangeState.Cancelled => nameof(PendingEquipmentChangeState.Cancelled),
            _ => throw new System.ArgumentOutOfRangeException(nameof(State))
        };
    }
}
