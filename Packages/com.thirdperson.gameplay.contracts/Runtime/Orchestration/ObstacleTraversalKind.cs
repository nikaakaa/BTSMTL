namespace ThirdPersonSimulation
{
    public enum ObstacleTraversalWarpTarget : byte { FrontLedge, BackLedge, BackFloor }
    public enum ObstacleTraversalBlendOutCondition : byte { Always, MovementInput, Falling }

    public enum ObstacleTraversalKind : byte
    {
        Hurdle = 1,
        Vault = 2,
        Mantle = 3
    }
}
