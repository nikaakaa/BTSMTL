using System;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum WorldFeature
    {
        None = 0,
        Ground = 1 << 0,
        Slope = 1 << 1,
        Step = 1 << 2,
        WallSlide = 1 << 3,
        DynamicObstacle = 1 << 4,
        ActorCollision = 1 << 5,
        NavigationSurface = 1 << 6,
        ObservedKinematicActorContact = 1 << 7
    }
}
