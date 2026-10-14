using System;

namespace ThirdPersonSimulation
{
    public enum SimulationPipelineInitialStateMode : byte
    {
        CaptureActivatedDefaults = 1,
        RestoreProvidedSnapshot = 2
    }

    public sealed class SimulationPipelineInitialStateSource
    {
        static readonly SimulationPipelineInitialStateSource s_CaptureActivatedDefaults =
            new SimulationPipelineInitialStateSource(
                SimulationPipelineInitialStateMode.CaptureActivatedDefaults,
                null);

        SimulationPipelineInitialStateSource(
            SimulationPipelineInitialStateMode mode,
            SimulationPipelineStateSnapshot snapshot)
        {
            Mode = mode;
            Snapshot = snapshot;
        }

        public SimulationPipelineInitialStateMode Mode { get; }
        public SimulationPipelineStateSnapshot Snapshot { get; }
        public static SimulationPipelineInitialStateSource CaptureActivatedDefaults => s_CaptureActivatedDefaults;

        public static SimulationPipelineInitialStateSource Restore(SimulationPipelineStateSnapshot snapshot) =>
            new SimulationPipelineInitialStateSource(
                SimulationPipelineInitialStateMode.RestoreProvidedSnapshot,
                snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    }
}
