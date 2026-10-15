namespace ThirdPersonSimulation
{
    public enum LocomotionInputMotionExecutionMode : byte
    {
        Once = 0,
        Timed = 1,
        Continuous = 2
    }

    public enum LocomotionInputMotionDisplacementMode : byte
    {
        ConstantSpeed = 0,
        ActionMotionCurve = 1
    }
}
