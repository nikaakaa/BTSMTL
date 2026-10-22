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
            public static readonly OrchestrationExtensionInputPort<MoveFacingAngle> MoveInput = new("move-input", OrchestrationValueKind.Vector2, 0);
            public static readonly OrchestrationExtensionOutputPort<MoveFacingAngle> Output = new("angle", OrchestrationValueKind.Scalar, 0);

            public static readonly OrchestrationExtensionDefinition<MoveFacingAngle> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-move-facing-angle"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        MoveInput.Declaration,
                        Output.Declaration
                    }, new[] { CharacterStateFields.ProviderOwner }));
        }

        public readonly struct CharacterStateVector3
        {
            public static readonly OrchestrationExtensionOutputPort<CharacterStateVector3> Output = new("value", OrchestrationValueKind.Vector3, 0);

            public static readonly OrchestrationExtensionDefinition<CharacterStateVector3> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-state-vector3"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Output.Declaration
                    }, CharacterStateFields.All));
        }

        public readonly struct CharacterStateScalar
        {
            public static readonly OrchestrationExtensionOutputPort<CharacterStateScalar> Output = new("value", OrchestrationValueKind.Scalar, 0);

            public static readonly OrchestrationExtensionDefinition<CharacterStateScalar> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-state-scalar"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Output.Declaration
                    }, CharacterStateFields.All));
        }

        public readonly struct CharacterStateYaw
        {
            public static readonly OrchestrationExtensionOutputPort<CharacterStateYaw> Output = new("value", OrchestrationValueKind.Yaw, 0);

            public static readonly OrchestrationExtensionDefinition<CharacterStateYaw> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-state-yaw"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Output.Declaration
                    }, CharacterStateFields.All));
        }

        public readonly struct CharacterStateBoolean
        {
            public static readonly OrchestrationExtensionOutputPort<CharacterStateBoolean> Output = new("value", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<CharacterStateBoolean> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "character-state-bool"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Output.Declaration
                    }, CharacterStateFields.All));
        }

        public readonly struct LocomotionDecision
        {
            public static readonly OrchestrationExtensionOutputPort<LocomotionDecision> Output = new("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<LocomotionDecision> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "locomotion-decision"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Output.Declaration
                    }, LocomotionDecisionFields.All));
        }

        public readonly struct LocomotionInputMotion
        {
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> MoveInput = new("move-input", OrchestrationValueKind.Vector2, 0);
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> ElapsedTicks = new("elapsed-ticks", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionInputPort<LocomotionInputMotion> ContinuationStateOperation = new("continuation-state-operation", OrchestrationValueKind.Int32, 2);
            public static readonly OrchestrationExtensionOutputPort<LocomotionInputMotion> ElapsedTicksOutput = new("elapsed-ticks", OrchestrationValueKind.Int32, 0);

            public static readonly OrchestrationExtensionDefinition<LocomotionInputMotion> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "locomotion-input-motion"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Resumable,
                    new[]
                    {
                        MoveInput.Declaration,
                        ElapsedTicks.Declaration,
                        ContinuationStateOperation.Declaration,
                        ElapsedTicksOutput.Declaration
                    }, LocomotionInputMotionFields.All));
        }

        public readonly struct MotionWarpAction
        {
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> Enabled = new("enabled", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> HasTarget = new("has-target", OrchestrationValueKind.Boolean, 1);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> TargetPosition = new("target-position", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> TargetYaw = new("target-yaw", OrchestrationValueKind.Scalar, 3);
            public static readonly OrchestrationExtensionInputPort<MotionWarpAction> InputDirection = new("input-direction", OrchestrationValueKind.Vector2, 4);

            public static readonly OrchestrationExtensionDefinition<MotionWarpAction> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "motion-warp"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Enabled.Declaration,
                        HasTarget.Declaration,
                        TargetPosition.Declaration,
                        TargetYaw.Declaration,
                        InputDirection.Declaration
                    }, MotionWarpFields.All));
        }
    }
}
