namespace BTSMTL.Orchestration
{
    // 领域自有的状态种类使用未列出的数值声明，由领域的 ProgramStateSchema 负责编码与校验。
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
        Identity = 13
    }
}
