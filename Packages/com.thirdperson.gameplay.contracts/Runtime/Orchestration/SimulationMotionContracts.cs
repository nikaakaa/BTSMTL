namespace ThirdPersonSimulation
{
    public enum SimulationMotionChannel
    {
        Locomotion = 0,
        Action = 1,
        GameplayResult = 2
    }

    public enum SimulationMotionBlendMode
    {
        Additive = 0,
        WeightedBlend = 1,
        Override = 2
    }

    public enum SimulationMotionContributionSpace
    {
        ActorLocal = 0,
        World = 1
    }
}
