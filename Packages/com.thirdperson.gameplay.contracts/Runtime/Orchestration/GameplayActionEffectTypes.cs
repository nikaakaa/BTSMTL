namespace ThirdPersonSimulation
{
    public enum GameplayEffectQueryTarget : byte
    {
        Actor = 0,
        CurrentAction = 1,
        SelectedSource = 2
    }

    public enum AbilityLifecyclePhase : byte
    {
        Startup = 0,
        Active = 1,
        Recovery = 2,
        Cancel = 3,
        Ended = 4
    }
}

namespace ThirdPersonGameplay.Effects
{
    public enum GameplayEffectRemoveSelector : byte
    {
        Handle = 0,
        EffectId = 1,
        EffectTagQuery = 3
    }
}
