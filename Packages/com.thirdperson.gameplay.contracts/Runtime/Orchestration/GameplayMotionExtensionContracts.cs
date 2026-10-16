using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using Field = BTSMTL.Orchestration.OrchestrationExtensionContentField;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayMotionExtensionContracts
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
            public static readonly Field TargetOffsetSpace = Field.Enum<ProgramMotionWarpTargetOffsetSpace>("TargetOffsetSpace");
            public static readonly Field RotationMode = Field.Enum<ProgramMotionWarpRotationMode>("RotationMode");
            public static readonly Field RotationMethod = Field.Enum<ProgramMotionWarpRotationMethod>("RotationMethod");
            public static readonly Field LimitPolicy = Field.Enum<ProgramMotionWarpLimitPolicy>("LimitPolicy");
            public static readonly Field TargetPlanarOffset = Field.Value("TargetPlanarOffset", OrchestrationValueKind.Vector2);
            public static readonly Field TargetYawOffsetDegrees = Field.Value("TargetYawOffsetDegrees", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumPositionCorrection = Field.Value("MaximumPositionCorrection", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumYawCorrectionDegrees = Field.Value("MaximumYawCorrectionDegrees", OrchestrationValueKind.Scalar);
            public static readonly Field MaximumYawRateDegreesPerSecond = Field.Value("MaximumYawRateDegreesPerSecond", OrchestrationValueKind.Scalar);
            public static readonly Field PositionProgressCurve = Field.Data("PositionProgressCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field YawProgressCurve = Field.Data("YawProgressCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field YawResponseCurve = Field.Data("YawResponseCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly Field InputYawResponseCurve = Field.Data("InputYawResponseCurve", GameplayOrchestrationPayloadContract.CurveTypeIdentity, false);
            public static readonly ReadOnlyMemory<Field> All = new[]
            {
                SourceMotionClipId, WindowEndTime, TranslationMode, TargetOffsetSpace, RotationMode, RotationMethod,
                LimitPolicy, TargetPlanarOffset, TargetYawOffsetDegrees, MaximumPositionCorrection,
                MaximumYawCorrectionDegrees, MaximumYawRateDegreesPerSecond,
                PositionProgressCurve, YawProgressCurve, YawResponseCurve, InputYawResponseCurve
            };
        }

        public static class MoveFacingAnglePorts
        {
            public static readonly OrchestrationExtensionInputPort MoveInput = new("move-input", OrchestrationValueKind.Vector2, 0);
            public static readonly OrchestrationExtensionOutputPort Output = new("angle", OrchestrationValueKind.Scalar, 0);
        }

        public static class CharacterStateVector3Ports
        {
            public static readonly OrchestrationExtensionOutputPort Output = new("value", OrchestrationValueKind.Vector3, 0);
        }

        public static class CharacterStateScalarPorts
        {
            public static readonly OrchestrationExtensionOutputPort Output = new("value", OrchestrationValueKind.Scalar, 0);
        }

        public static class CharacterStateYawPorts
        {
            public static readonly OrchestrationExtensionOutputPort Output = new("value", OrchestrationValueKind.Yaw, 0);
        }

        public static class CharacterStateBooleanPorts
        {
            public static readonly OrchestrationExtensionOutputPort Output = new("value", OrchestrationValueKind.Boolean, 0);
        }

        public static class LocomotionDecisionPorts
        {
            public static readonly OrchestrationExtensionOutputPort Output = new("result", OrchestrationValueKind.Boolean, 0);
        }

        public static class LocomotionInputMotionPorts
        {
            public static readonly OrchestrationExtensionInputPort MoveInput = new("move-input", OrchestrationValueKind.Vector2, 0);
            public static readonly OrchestrationExtensionInputPort ElapsedTicks = new("elapsed-ticks", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionInputPort ContinuationStateOperation = new("continuation-state-operation", OrchestrationValueKind.Int32, 2);
            public static readonly OrchestrationExtensionOutputPort ElapsedTicksOutput = new("elapsed-ticks", OrchestrationValueKind.Int32, 0);
        }

        public static class MotionWarpActionPorts
        {
            public static readonly OrchestrationExtensionInputPort Enabled = new("enabled", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionInputPort HasTarget = new("has-target", OrchestrationValueKind.Boolean, 1);
            public static readonly OrchestrationExtensionInputPort TargetPosition = new("target-position", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionInputPort TargetYaw = new("target-yaw", OrchestrationValueKind.Scalar, 3);
            public static readonly OrchestrationExtensionInputPort InputDirection = new("input-direction", OrchestrationValueKind.Vector2, 4);
        }

        public static readonly OrchestrationExtensionDefinition MoveFacingAngle = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "character-move-facing-angle"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    MoveFacingAnglePorts.MoveInput.Declaration,
                    MoveFacingAnglePorts.Output.Declaration
                }, new[] { CharacterStateFields.ProviderOwner }));

        public static readonly OrchestrationExtensionDefinition CharacterStateVector3 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "character-state-vector3"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    CharacterStateVector3Ports.Output.Declaration
                }, CharacterStateFields.All));

        public static readonly OrchestrationExtensionDefinition CharacterStateScalar = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "character-state-scalar"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    CharacterStateScalarPorts.Output.Declaration
                }, CharacterStateFields.All));

        public static readonly OrchestrationExtensionDefinition CharacterStateYaw = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "character-state-yaw"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    CharacterStateYawPorts.Output.Declaration
                }, CharacterStateFields.All));

        public static readonly OrchestrationExtensionDefinition CharacterStateBoolean = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "character-state-bool"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    CharacterStateBooleanPorts.Output.Declaration
                }, CharacterStateFields.All));

        public static readonly OrchestrationExtensionDefinition LocomotionDecision = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "locomotion-decision"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    LocomotionDecisionPorts.Output.Declaration
                }, LocomotionDecisionFields.All));

        public static readonly OrchestrationExtensionDefinition LocomotionInputMotion = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "locomotion-input-motion"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    LocomotionInputMotionPorts.MoveInput.Declaration,
                    LocomotionInputMotionPorts.ElapsedTicks.Declaration,
                    LocomotionInputMotionPorts.ContinuationStateOperation.Declaration,
                    LocomotionInputMotionPorts.ElapsedTicksOutput.Declaration
                }, LocomotionInputMotionFields.All));

        public static readonly OrchestrationExtensionDefinition MotionWarpAction = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "motion-warp"),
            2, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    MotionWarpActionPorts.Enabled.Declaration,
                    MotionWarpActionPorts.HasTarget.Declaration,
                    MotionWarpActionPorts.TargetPosition.Declaration,
                    MotionWarpActionPorts.TargetYaw.Declaration,
                    MotionWarpActionPorts.InputDirection.Declaration
                }, MotionWarpFields.All));
    }
}
