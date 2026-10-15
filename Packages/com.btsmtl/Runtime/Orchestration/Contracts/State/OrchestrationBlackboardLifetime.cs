namespace BTSMTL.Orchestration
{
    public enum OrchestrationBlackboardLifetime : byte
    {
        Config = 0,
        Spawn = 1,
        StateEnterToExit = 2,
        ActionInstance = 3,
        Frame = 4,
        ManualClear = 5,
        GraphInstance = 6
    }
}
