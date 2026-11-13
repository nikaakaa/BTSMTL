using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using Field = BTSMTL.Orchestration.OrchestrationExtensionContentField;

namespace ThirdPersonGameplay.Orchestration
{
    public static partial class GameplayMotionExtensionContracts
    {
        public const string DomainId = "gameplay.motion";
        public const string MotionCurveSampleOperationId = "motion-curve.sample";
        public const string RootMotionDependencyKind = "timeline.motion-curve";

        public static class CharacterStateFields
        {
            public static readonly Field FieldId = Field.Identity("FieldId", "character-state-field");
            public static readonly Field ProviderOwner = GameplayOrchestrationPayloadContract.ProviderOwner;
            public static readonly ReadOnlyMemory<Field> All = new[] { FieldId, ProviderOwner };
        }

        public static class LocomotionDecisionFields
        {
            public static readonly Field Query = Field.Enum<BtsmtlSkillLocomotionDecisionQuery>("Query");
            public static readonly Field Mode = Field.Enum<CharacterLocomotionMode>("Mode");
            public static readonly ReadOnlyMemory<Field> All = new[] { Query, Mode };
        }

        public static class LocomotionInputMotionFields
        {
            public static readonly Field ExecutionMode = Field.Enum<LocomotionInputMotionExecutionMode>("ExecutionMode");
            public static readonly Field DisplacementMode = Field.Enum<LocomotionInputMotionDisplacementMode>("DisplacementMode");
            public static readonly Field CameraRelative = Field.Value("CameraRelative", OrchestrationValueKind.Boolean);
            public static readonly Field TurnSpeedDegrees = Field.Value("TurnSpeedDegrees", OrchestrationValueKind.Scalar);
            public static readonly Field DurationSeconds = Field.Value("DurationSeconds", OrchestrationValueKind.Scalar);
            public static readonly Field MoveSpeed = Field.Value("MoveSpeed", OrchestrationValueKind.Scalar, false);
            public static readonly Field ActionMotionPositionX = Field.Data("ActionMotionPositionX", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field ActionMotionPositionZ = Field.Data("ActionMotionPositionZ", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field ActionMotionDuration = Field.Value("ActionMotionDuration", OrchestrationValueKind.Scalar, false);
            public static readonly ReadOnlyMemory<Field> All = new[]
            {
                ExecutionMode, DisplacementMode, CameraRelative, TurnSpeedDegrees, DurationSeconds,
                MoveSpeed, ActionMotionPositionX, ActionMotionPositionZ, ActionMotionDuration
            };
        }

        public static class MotionWarpFields
        {
            public static readonly Field SourceMotionClipId = Field.Identity("SourceMotionClipId", "timeline-clip");
            public static readonly Field WindowEndTime = Field.Value("WindowEndTime", OrchestrationValueKind.Scalar);
            public static readonly Field TranslationMode = Field.Enum<ProgramMotionWarpTranslationMode>("TranslationMode");
            public static readonly Field PositionAxes = Field.Enum<ProgramMotionWarpPositionAxes>("PositionAxes");
            public static readonly Field TargetOffsetSpace = Field.Enum<ProgramMotionWarpTargetOffsetSpace>("TargetOffsetSpace");
            public static readonly Field RotationMode = Field.Enum<ProgramMotionWarpRotationMode>("RotationMode");
            public static readonly Field RotationMethod = Field.Enum<ProgramMotionWarpRotationMethod>("RotationMethod");
            public static readonly Field LimitPolicy = Field.Enum<ProgramMotionWarpLimitPolicy>("LimitPolicy");
            public static readonly Field TargetOffset = Field.Value("TargetOffset", OrchestrationValueKind.Vector3);
            public static readonly Field TargetYawOffsetDegrees = Field.Value("TargetYawOffsetDegrees", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumPositionCorrection = Field.Value("MaximumPositionCorrection", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumVerticalCorrection = Field.Value("MaximumVerticalCorrection", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumYawCorrectionDegrees = Field.Value("MaximumYawCorrectionDegrees", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumYawRateDegreesPerSecond = Field.Value("MaximumYawRateDegreesPerSecond", OrchestrationValueKind.Scalar);
            public static readonly Field PositionProgressCurve = Field.Data("PositionProgressCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field YawProgressCurve = Field.Data("YawProgressCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field YawResponseCurve = Field.Data("YawResponseCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field InputYawResponseCurve = Field.Data("InputYawResponseCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly ReadOnlyMemory<Field> All = new[]
            {
                SourceMotionClipId, WindowEndTime, TranslationMode, PositionAxes, TargetOffsetSpace, RotationMode, RotationMethod,
                LimitPolicy, TargetOffset, TargetYawOffsetDegrees, MaximumPositionCorrection, MaximumVerticalCorrection,
                MaximumYawCorrectionDegrees, MaximumYawRateDegreesPerSecond,
                PositionProgressCurve, YawProgressCurve, YawResponseCurve, InputYawResponseCurve
            };
        }

        public readonly struct MoveFacingAngle
        {
            static readonly OrchestrationExtensionPorts<MoveFacingAngle> Ports = new();
            public static readonly OrchestrationExtensionInputPort<MoveFacingAngle> MoveInput = Ports.Input("move-input", OrchestrationValueKind.Vector2);
            public static readonly OrchestrationExtensionOutputPort<MoveFacingAngle> Angle = Ports.Output("angle", OrchestrationValueKind.Scalar);

            public static readonly OrchestrationExtensionDefinition<MoveFacingAngle> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-move-facing-angle"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { CharacterStateFields.ProviderOwner }));
        }

        public readonly struct CharacterStateVector3
        {
            static readonly OrchestrationExtensionPorts<CharacterStateVector3> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<CharacterStateVector3> Value = Ports.Output("value", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionDefinition<CharacterStateVector3> Definition = CharacterState("character-state-vector3", Ports);
        }

        public readonly struct CharacterStateScalar
        {
            static readonly OrchestrationExtensionPorts<CharacterStateScalar> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<CharacterStateScalar> Value = Ports.Output("value", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionDefinition<CharacterStateScalar> Definition = CharacterState("character-state-scalar", Ports);
        }

        public readonly struct CharacterStateYaw
        {
            static readonly OrchestrationExtensionPorts<CharacterStateYaw> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<CharacterStateYaw> Value = Ports.Output("value", OrchestrationValueKind.Yaw);
            public static readonly OrchestrationExtensionDefinition<CharacterStateYaw> Definition = CharacterState("character-state-yaw", Ports);
        }

        public readonly struct CharacterStateBoolean
        {
            static readonly OrchestrationExtensionPorts<CharacterStateBoolean> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<CharacterStateBoolean> Value = Ports.Output("value", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<CharacterStateBoolean> Definition = CharacterState("character-state-bool", Ports);
        }

        static OrchestrationExtensionDefinition<TOperation> CharacterState<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports) where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, ports.Seal(), CharacterStateFields.All));

        public readonly struct LocomotionDecision
        {
            static readonly OrchestrationExtensionPorts<LocomotionDecision> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<LocomotionDecision> Result = Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<LocomotionDecision> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "locomotion-decision"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), LocomotionDecisionFields.All));
        }

        public readonly struct LocomotionInputMotion
        {
            static readonly OrchestrationExtensionPorts<LocomotionInputMotion> Ports = new();
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> MoveInput = Ports.Input("move-input", OrchestrationValueKind.Vector2);
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> ElapsedTicks = Ports.Input("elapsed-ticks", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> ContinuationStateOperation = Ports.Input("continuation-state-operation", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionOutputPort<LocomotionInputMotion> ElapsedTicksOutput = Ports.Output("elapsed-ticks", OrchestrationValueKind.Int32);

            public static readonly OrchestrationExtensionDefinition<LocomotionInputMotion> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "locomotion-input-motion"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Resumable,
                    Ports.Seal(), LocomotionInputMotionFields.All));
        }

        public readonly struct MotionWarpAction
        {
            static readonly OrchestrationExtensionPorts<MotionWarpAction> Ports = new();
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> Enabled = Ports.Input("enabled", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> HasTarget = Ports.Input("has-target", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> TargetPosition = Ports.Input("target-position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> TargetYaw = Ports.Input("target-yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> InputDirection = Ports.Input("input-direction", OrchestrationValueKind.Vector2);

            public static readonly OrchestrationExtensionDefinition<MotionWarpAction> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "motion-warp"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), MotionWarpFields.All));
        }
    }
}
