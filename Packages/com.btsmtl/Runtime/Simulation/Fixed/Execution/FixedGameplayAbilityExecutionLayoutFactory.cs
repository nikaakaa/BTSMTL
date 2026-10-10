using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal static class FixedGameplayAbilityExecutionLayoutFactory
    {
        public static GameplayAbilityExecutionLayout Create(
            FixedGameplayAbilityExecutionData data)
        {
            return new GameplayAbilityExecutionLayout(
                data.AbilityId,
                data.TickRate,
                data.Operations,
                data.Constants,
                data.ConstantInputBindings,
                data.ControlFlow,
                data.References,
                data.GraphCallFrames,
                data.StateSlots,
                data.Scopes,
                data.CatalogEntries,
                data.SourceMap,
                data.Producers,
                data.CatalogIndex,
                data.Topology);
        }
    }
}
