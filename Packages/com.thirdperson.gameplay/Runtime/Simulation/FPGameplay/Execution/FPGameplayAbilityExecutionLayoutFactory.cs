using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal static class FPGameplayAbilityExecutionLayoutFactory
    {
        public static GameplayAbilityExecutionLayout Create(
            FPGameplayAbilityExecutionData data)
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
