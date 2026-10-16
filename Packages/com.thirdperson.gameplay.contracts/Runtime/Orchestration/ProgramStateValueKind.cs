namespace ThirdPersonSimulation
{
    public enum ProgramStateValueKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Scalar = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        Quaternion = 8,
        Enum = 9,
        ActionTargetSnapshot = 10,
        BlackboardOwnerToken = 11,
        BlackboardWriteStamp = 12,
        Identity = 13
    }
}
