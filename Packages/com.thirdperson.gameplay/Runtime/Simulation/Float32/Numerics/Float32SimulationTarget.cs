using System;

namespace ThirdPersonSimulation
{
    public sealed class Float32SimulationTargetManifest
    {
        public Float32SimulationTargetManifest(
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

    public static class Float32SimulationTarget
    {
        static readonly Float32SimulationTargetManifest s_Manifest =
            new Float32SimulationTargetManifest(
                Float32SimulationNumericProfile.Value,
                nameof(Float32Scalar),
                nameof(Float32Vector2),
                nameof(Float32Vector3),
                nameof(Float32Yaw),
                new SimulationExecutionTargetManifest(
                    "character-execution/float32/v2",
                    Float32SimulationNumericProfile.Value,
                    GameplayAbilityOperationSet.Version,
                    GameplayAbilityOperationSet.Operations));

        public static Float32SimulationTargetManifest Manifest => s_Manifest;
    }
}
