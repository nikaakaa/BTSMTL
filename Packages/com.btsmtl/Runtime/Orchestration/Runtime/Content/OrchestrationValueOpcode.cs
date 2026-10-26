using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationValueOpcode : byte
    {
        Copy,
        BooleanNot,
        BooleanAnd,
        BooleanOr,
        Int32ToBoolean,
        UInt64ToBoolean,
        ScalarToBoolean,
        IdentityToBoolean,
        BooleanToScalar,
        Int32ToScalar,
        UInt64ToScalar,
        ScalarToInt32,
        Int32Equal,
        Int32NotEqual,
        Int32LessThan,
        Int32LessThanOrEqual,
        Int32GreaterThanOrEqual,
        Int32GreaterThan,
        ScalarEqual,
        ScalarNotEqual,
        ScalarLessThan,
        ScalarLessThanOrEqual,
        ScalarGreaterThanOrEqual,
        ScalarGreaterThan,
        ScalarMultiply,
        ScalarDivide,
        IdentityEqual,
        ScalarAbs,
        ScalarClamp,
        ScalarMoveTowards,
        Vector3Planar,
        Vector3Y,
        Vector2Magnitude,
        Vector2Normalize,
        Vector2Subtract,
        Vector2SignedAngle,
        QuaternionForward,
        QuaternionAngleAxis,
        Constant,
        HostInput,
        InputVector2Magnitude,
        InputRequest,
        BlackboardGet,
        GraphCallOutput,
        StateRootCompleted,
        StateExitCause,
        Cycle,
        TimelineTime,
        Extension,
        Vector2Dot,
        Vector2Cross,
        ScalarSubtract
    }

    public enum OrchestrationAssignmentOpcode : byte
    {
        Set,
        Int32Add,
        Int32Subtract,
        Int32Multiply,
        Int32Divide,
        ScalarAdd,
        ScalarSubtract,
        ScalarMultiply,
        ScalarDivide,
        Vector3Add,
        Vector3Subtract,
        Vector3Multiply,
        Vector3Divide
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationValueOperation
    {
        public OrchestrationValueOperation(OrchestrationValueOpcode code, ushort argument)
        {
            Code = code;
            Argument = argument;
        }

        public readonly OrchestrationValueOpcode Code;
        public readonly ushort Argument;
    }
}
