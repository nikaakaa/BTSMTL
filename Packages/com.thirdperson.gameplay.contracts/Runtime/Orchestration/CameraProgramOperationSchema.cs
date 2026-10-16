using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class CameraProgramOperationSchema
    {
        public static readonly AnimationChannelId ChannelId = new AnimationChannelId("Camera");
        public const string BasisValidPortId = "Valid";
        public const string BasisPlanarForwardPortId = "Planar Forward";
        public const string BasisPlanarRightPortId = "Planar Right";
        public const string BasisLookDirectionPortId = "Look Direction";
        public const string BasisAimPointPortId = "Aim Point";
        public const string BasisYawPortId = "Yaw";
        public const string BasisPitchPortId = "Pitch";
        public const string BasisValidInputId = "camera-basis.valid";
        public const string BasisPlanarForwardInputId = "camera-basis.planar-forward";
        public const string BasisPlanarRightInputId = "camera-basis.planar-right";
        public const string BasisLookDirectionInputId = "camera-basis.look-direction";
        public const string BasisAimPointInputId = "camera-basis.aim-point";
        public const string BasisYawInputId = "camera-basis.yaw";
        public const string BasisPitchInputId = "camera-basis.pitch";

        public static bool IsCameraBasisInputId(string inputId)
        {
            for (int index = 0; index < BasisInputs.Count; index++)
                if (string.Equals(BasisInputs[index].InputId, inputId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static IReadOnlyList<GraphOrchestrationHostInputPort> BasisInputs { get; } = Array.AsReadOnly(new[]
        {
            new GraphOrchestrationHostInputPort(BasisValidPortId, BasisValidInputId,
                OrchestrationValueKind.Boolean, GraphValueTypeIdentity.Of(OrchestrationValueKind.Boolean)),
            new GraphOrchestrationHostInputPort(BasisPlanarForwardPortId, BasisPlanarForwardInputId,
                OrchestrationValueKind.Vector3, GraphValueTypeIdentity.Of(OrchestrationValueKind.Vector3)),
            new GraphOrchestrationHostInputPort(BasisPlanarRightPortId, BasisPlanarRightInputId,
                OrchestrationValueKind.Vector3, GraphValueTypeIdentity.Of(OrchestrationValueKind.Vector3)),
            new GraphOrchestrationHostInputPort(BasisLookDirectionPortId, BasisLookDirectionInputId,
                OrchestrationValueKind.Vector3, GraphValueTypeIdentity.Of(OrchestrationValueKind.Vector3)),
            new GraphOrchestrationHostInputPort(BasisAimPointPortId, BasisAimPointInputId,
                OrchestrationValueKind.Vector3, GraphValueTypeIdentity.Of(OrchestrationValueKind.Vector3)),
            new GraphOrchestrationHostInputPort(BasisYawPortId, BasisYawInputId,
                OrchestrationValueKind.Yaw, GraphValueTypeIdentity.Of(OrchestrationValueKind.Yaw)),
            new GraphOrchestrationHostInputPort(BasisPitchPortId, BasisPitchInputId,
                OrchestrationValueKind.Scalar, GraphValueTypeIdentity.Of(OrchestrationValueKind.Scalar))
        });
    }
}
