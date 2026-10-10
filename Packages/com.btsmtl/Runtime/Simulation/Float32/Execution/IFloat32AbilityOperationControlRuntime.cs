namespace ThirdPersonSimulation
{
    internal interface IFloat32AbilityOperationControlRuntime
    {
        bool IsActive(OperationHandle operation);
        bool IsStopping(OperationHandle operation);
        OperationStopStatus RequestStop(OperationHandle operation, OperationStopContext context);
        OperationStopStatus ContinueStop(OperationHandle operation);
        void ForceStop(OperationHandle operation, OperationStopContext context);
        OperationRunnableStatus ReadStatus(OperationHandle operation);
        ulong ReadGeneration(OperationHandle operation);
        OperationExecutionResult Tick(OperationHandle operation);
    }
}
