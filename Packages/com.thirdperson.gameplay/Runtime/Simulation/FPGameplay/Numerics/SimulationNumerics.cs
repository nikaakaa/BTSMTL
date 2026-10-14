using System;
using Photon.Deterministic;
using ThirdPersonSimulation.QuantumNumerics;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPSimulationTargetManifest
    {
        public FPSimulationTargetManifest(
            SimulationNumericProfile profile,
            string scalarType,
            string vector2Type,
            string vector3Type,
            string yawType,
            SimulationExecutionTargetManifest executionTarget)
        {
            Profile = profile;
            ScalarType = SimulationIdentity.Require(scalarType, nameof(scalarType));
            Vector2Type = SimulationIdentity.Require(vector2Type, nameof(vector2Type));
            Vector3Type = SimulationIdentity.Require(vector3Type, nameof(vector3Type));
            YawType = SimulationIdentity.Require(yawType, nameof(yawType));
            ExecutionTarget = executionTarget ?? throw new ArgumentNullException(nameof(executionTarget));
            if (executionTarget.NumericProfile != profile)
                throw new ArgumentException("Numeric Target and Execution Target profiles must match.", nameof(executionTarget));
        }

        public SimulationNumericProfile Profile { get; }
        public string ScalarType { get; }
        public string Vector2Type { get; }
        public string Vector3Type { get; }
        public string YawType { get; }
        public SimulationExecutionTargetManifest ExecutionTarget { get; }
    }

    public static class FPSimulationTarget
    {
        static readonly FPSimulationTargetManifest s_Manifest = new FPSimulationTargetManifest(
            QuantumSimulationNumericProfile.Value,
            typeof(FP).FullName,
            typeof(FPVector2).FullName,
            typeof(FPVector3).FullName,
            typeof(FP).FullName,
            new SimulationExecutionTargetManifest(
                "character-execution/quantum-fp/v1",
                QuantumSimulationNumericProfile.Value,
                GameplayAbilityOperationSet.Version,
                GameplayAbilityOperationSet.Operations));

        public static FPSimulationTargetManifest Manifest => s_Manifest;
    }
}
