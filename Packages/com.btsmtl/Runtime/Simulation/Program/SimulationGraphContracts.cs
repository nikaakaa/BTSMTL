using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using static ThirdPersonSimulation.PresentationProgramPayloadSchema;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum WorldCapability : ulong
    {
        None = 0,
        BodyMotion = 1UL << 0,
        Grounding = 1UL << 1,
        Collision = 1UL << 2,
        Reconstructible = 1UL << 3,
        Snapshotable = 1UL << 4,
        DeterministicReplay = 1UL << 5,
        AirborneVerticalMotion = 1UL << 6
    }

    public enum ActionTargetRequirement : byte
    {
        None = 0,
        SnapshotRequired = 1,
        OptionalSnapshot = 2
    }

    [CanonicalUnionTemplate]
    public readonly struct ProgramInputValue<TScalar, TVector2, TVector3, TYaw, TActionTargetSnapshot>
    {
        public readonly CanonicalTextValue InputId;
        [CanonicalUnionCase]
        public readonly bool Boolean;
        [CanonicalUnionCase]
        public readonly TScalar Scalar;
        [CanonicalUnionCase]
        public readonly TVector2 Vector2;
        [CanonicalUnionCase]
        public readonly TVector3 Vector3;
        [CanonicalUnionCase]
        public readonly TYaw Yaw;
        [CanonicalUnionCase]
        public readonly TActionTargetSnapshot ActionTargetSnapshot;
    }

    public enum LocomotionInputMotionExecutionMode : byte
    {
        Once = 0,
        Timed = 1,
        Continuous = 2
    }

    public enum LocomotionInputMotionDisplacementMode : byte
    {
        ConstantSpeed = 0,
        ActionMotionCurve = 1
    }

    public enum ProgramMotionSourceBlendMode : byte
    {
        Additive = 0,
        WeightedBlend = 1,
        Override = 2
    }

    public enum ProgramMotionWarpTranslationMode : byte
    {
        Disabled = 0,
        ScaleToTarget = 1,
        SkewToTarget = 2,
        LinearToTarget = 3
    }

    public enum ProgramMotionWarpTargetOffsetSpace : byte
    {
        TargetLocal = 0,
        ApproachDirection = 1,
        ActorStartLocal = 2,
        World = 3
    }

    public enum ProgramMotionWarpRotationMode : byte
    {
        Disabled = 0,
        FaceTarget = 1,
        MatchTargetYaw = 2
    }

    public enum ProgramMotionWarpRotationMethod : byte
    {
        ProgressCurve = 0,
        ConstantRate = 1,
        ScaleSourceYaw = 2,
        TargetResponse = 3
    }

    public enum ProgramMotionWarpLimitPolicy : byte
    {
        ApplyClamped = 0,
        PreserveSource = 1
    }

    public enum ProgramMotionWarpLimitResult : byte
    {
        Applied = 0,
        AppliedClamped = 1,
        PreservedByLimitPolicy = 2
    }

    public enum SimulationOperationCode : ushort
    {
        Root = 1,
        Loop = 2,
        Parallel = 3,
        Sequence = 4,
        Selector = 5,
        Succeed = 6,
        SubGraph = 7,
        Branch = 8,
        Cycle = 9,
        StateMachine = 20,
        State = 21,
        StateEnter = 22,
        StateAny = 23,
        StateExit = 24,
        StateOnEnter = 25,
        StateOnExit = 26,
        StateRootCompleted = 27,
        StateExitCause = 28,
        Timeline = 40,
        TimelineEnter = 41,
        TimelineAnimation = 42,
        TimelineCameraState = 45,
        TimelineCameraResponse = 46,
        TimelineScenePresentationParameter = 50,
        TimelineClipExitRequest = 51,
        TimelineTime = 52,
        ActivationEntry = 53,
        BlackboardGet = 60,
        BlackboardSet = 61,
        InputBoolean = 70,
        InputScalar = 71,
        InputVector2 = 72,
        InputVector2Magnitude = 73,
        InputRequest = 74,
        MoveFacingAngle = 75,
        CharacterStateRead = 76,
        ActivateActionInstance = 80,
        ActionContextActive = 81,
        SubmitActionLifecycle = 82,
        ActionWindowActive = 83,
        CanActivateAction = 84,
        ActionEventReceived = 85,
        AbilityActive = 86,
        SelectedSourceWindowActive = 87,
        LocomotionDecision = 88,
        SelectedSourcePhase = 89,
        SetActionPhase = 91,
        LocomotionInputMotion = 90,
        ConditionResult = 100,
        Compare = 101,
        And = 102,
        Or = 103,
        Not = 104,
        Constant = 105,
        Calculate = 106,
        SelectValue = 107,
        HostInput = 108,
        IdentityValue = 109,
        GameplayEffectHasTag = 110,
        GameplayEffectMatchTags = 111,
        GameplayAttributeRead = 112,
        GameplayEffectApply = 113,
        GameplayEffectRemove = 114,
        AttackQuery = 115,
        AttackHitQuery = 116,
        CameraStateRequest = 120,
        CameraEffectRequest = 121,
        CameraResponse = 122,
        CameraTarget = 123,
        CameraBasisRead = 124,
        ReadEquipmentIdentity = 130,
        ReadEquipmentParameter = 131,
        RequestEquipmentChange = 132,
        BeginEquipmentChange = 133,
        CommitEquipmentChange = 134,
        CancelEquipmentChange = 135,
        ActionTargetRead = 136,
        MotionWarp = 137,
        CaptureActionTarget = 138,
        VfxRequest = 140,
        AudioRequest = 141,
    }

    public static class GameplayAbilityOperationSet
    {
        public const string Id = "character-gameplay-operations";
        public static readonly OperationSetVersion Version = new OperationSetVersion(Id + "/31");

        static readonly ReadOnlyCollection<SimulationOperationCode> s_Operations =
            Array.AsReadOnly(new[]
            {
                SimulationOperationCode.Root,
                SimulationOperationCode.Loop,
                SimulationOperationCode.Parallel,
                SimulationOperationCode.Sequence,
                SimulationOperationCode.Selector,
                SimulationOperationCode.Succeed,
                SimulationOperationCode.SubGraph,
                SimulationOperationCode.StateMachine,
                SimulationOperationCode.State,
                SimulationOperationCode.StateEnter,
                SimulationOperationCode.StateAny,
                SimulationOperationCode.StateExit,
                SimulationOperationCode.StateOnEnter,
                SimulationOperationCode.StateOnExit,
                SimulationOperationCode.StateRootCompleted,
                SimulationOperationCode.StateExitCause,
                SimulationOperationCode.Timeline,
                SimulationOperationCode.TimelineEnter,
                SimulationOperationCode.TimelineAnimation,
                SimulationOperationCode.TimelineCameraState,
                SimulationOperationCode.TimelineCameraResponse,
                SimulationOperationCode.TimelineClipExitRequest,
                SimulationOperationCode.TimelineTime,
                SimulationOperationCode.ActivationEntry,
                SimulationOperationCode.BlackboardGet,
                SimulationOperationCode.BlackboardSet,
                SimulationOperationCode.InputBoolean,
                SimulationOperationCode.InputScalar,
                SimulationOperationCode.InputVector2,
                SimulationOperationCode.InputVector2Magnitude,
                SimulationOperationCode.InputRequest,
                SimulationOperationCode.MoveFacingAngle,
                SimulationOperationCode.CharacterStateRead,
                SimulationOperationCode.ActivateActionInstance,
                SimulationOperationCode.ActionContextActive,
                SimulationOperationCode.SubmitActionLifecycle,
                SimulationOperationCode.ActionWindowActive,
                SimulationOperationCode.CanActivateAction,
                SimulationOperationCode.ActionEventReceived,
                SimulationOperationCode.AbilityActive,
                SimulationOperationCode.SelectedSourceWindowActive,
                SimulationOperationCode.LocomotionDecision,
                SimulationOperationCode.SelectedSourcePhase,
                SimulationOperationCode.SetActionPhase,
                SimulationOperationCode.LocomotionInputMotion,
                SimulationOperationCode.ConditionResult,
                SimulationOperationCode.Compare,
                SimulationOperationCode.And,
                SimulationOperationCode.Or,
                SimulationOperationCode.Not,
                SimulationOperationCode.Constant,
                SimulationOperationCode.GameplayEffectHasTag,
                SimulationOperationCode.GameplayEffectMatchTags,
                SimulationOperationCode.GameplayAttributeRead,
                SimulationOperationCode.GameplayEffectApply,
                SimulationOperationCode.GameplayEffectRemove,
                SimulationOperationCode.AttackQuery,
                SimulationOperationCode.AttackHitQuery,
                SimulationOperationCode.CameraStateRequest,
                SimulationOperationCode.CameraEffectRequest,
                SimulationOperationCode.CameraResponse,
                SimulationOperationCode.CameraTarget,
                SimulationOperationCode.CameraBasisRead,
                SimulationOperationCode.ReadEquipmentIdentity,
                SimulationOperationCode.ReadEquipmentParameter,
                SimulationOperationCode.RequestEquipmentChange,
                SimulationOperationCode.BeginEquipmentChange,
                SimulationOperationCode.CommitEquipmentChange,
                SimulationOperationCode.CancelEquipmentChange,
                SimulationOperationCode.ActionTargetRead,
                SimulationOperationCode.MotionWarp,
                SimulationOperationCode.CaptureActionTarget,
                SimulationOperationCode.VfxRequest,
                SimulationOperationCode.AudioRequest
            });

        public static IReadOnlyList<SimulationOperationCode> Operations => s_Operations;

        public static void RequireVersion(OperationSetVersion version)
        {
            if (!version.Equals(Version))
                throw new InvalidOperationException($"Operation set '{version.Value}' is not supported; expected '{Version.Value}'.");
        }

        public static void RequireOperation(SimulationOperationCode code)
        {
            if (!IsOperation(code))
                throw new InvalidOperationException($"Operation code '{(ushort)code}' is not supported by '{Version.Value}'.");
            GameplayAbilityValuePortContracts.Require(code);
        }

        public static bool IsOperation(SimulationOperationCode code)
        {
            for (int i = 0; i < s_Operations.Count; i++)
                if (s_Operations[i] == code)
                    return true;
            return false;
        }

        public static void RequireCompleteBackend(
            OperationSetVersion version,
            IReadOnlyList<SimulationOperationCode> supportedOperations,
            string backendIdentity)
        {
            RequireVersion(version);
            string identity = SimulationIdentity.Require(backendIdentity, nameof(backendIdentity));
            if (supportedOperations == null || supportedOperations.Count != s_Operations.Count)
                throw new InvalidOperationException($"Kernel backend '{identity}' does not implement the complete '{Version.Value}' operation set.");
            for (int i = 0; i < s_Operations.Count; i++)
            {
                if (supportedOperations[i] != s_Operations[i])
                {
                    throw new InvalidOperationException(
                        $"Kernel backend '{identity}' operation contract diverges at index {i}: expected '{s_Operations[i]}', received '{supportedOperations[i]}'.");
                }
            }
        }
    }

    public static class CameraProgramOperationSchema
    {
        public const int PayloadVersion = 2;
        public const int EffectPayloadVersion = 3;
        public const int TargetPayloadVersion = 3;
        public const uint PreserveLockStartOnSubmit = 1u;
        public const uint FollowIgnoreTimeScale = 1u;
        public const uint AimIgnoreTimeScale = 2u;
        public static readonly AnimationChannelId ChannelId = new AnimationChannelId("Camera");
        public const string OutputPortId = "Submitted";
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
        public const int TargetKeyMask = 1 << 0;
        public const int AnchorKeyMask = 1 << 1;
        public const int AimPointKeyMask = 1 << 2;
        public const int PreferredBoneKeyMask = 1 << 3;
        public const int AllTargetKeyMasks = TargetKeyMask | AnchorKeyMask | AimPointKeyMask | PreferredBoneKeyMask;

        public static bool IsCameraPresentationOperation(SimulationOperationCode code)
        {
            return code == SimulationOperationCode.CameraStateRequest ||
                   code == SimulationOperationCode.CameraEffectRequest ||
                   code == SimulationOperationCode.CameraResponse ||
                   code == SimulationOperationCode.CameraTarget;
        }

        public static bool IsCameraBasisOperation(SimulationOperationCode code) =>
            code == SimulationOperationCode.CameraBasisRead;

        public static bool IsCameraOperation(SimulationOperationCode code) =>
            IsCameraPresentationOperation(code) || IsCameraBasisOperation(code);

        public static bool IsCameraBasisInputId(string inputId)
        {
            return string.Equals(inputId, BasisValidInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisPlanarForwardInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisPlanarRightInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisLookDirectionInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisAimPointInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisYawInputId, StringComparison.Ordinal) ||
                   string.Equals(inputId, BasisPitchInputId, StringComparison.Ordinal);
        }

        public static bool IsCameraBasisOutputPort(string portId)
        {
            return string.Equals(portId, BasisValidPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisPlanarForwardPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisPlanarRightPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisLookDirectionPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisAimPointPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisYawPortId, StringComparison.Ordinal) ||
                   string.Equals(portId, BasisPitchPortId, StringComparison.Ordinal);
        }

        public static void RequireCameraBasisOutputPort(string portId, string sourceIdentity)
        {
            if (!IsCameraBasisOutputPort(portId))
                throw new InvalidOperationException($"Camera basis source '{sourceIdentity}' contains unknown output port '{portId}'.");
        }

        public static void Validate(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (literals == null)
                throw new ArgumentNullException(nameof(literals));
            if (!IsCameraOperation(operation.Code))
                return;
            int expectedPayloadVersion = operation.Code switch
            {
                SimulationOperationCode.CameraEffectRequest => EffectPayloadVersion,
                SimulationOperationCode.CameraTarget => TargetPayloadVersion,
                _ => PayloadVersion
            };
            if (operation.Integer0 != expectedPayloadVersion)
                throw Invalid(operation, $"payload version '{operation.Integer0}' is unsupported; expected '{expectedPayloadVersion}'");
            if (operation.Operands.Count != 0 || operation.Unsigned0 != 0 || operation.Number0 != 0d || operation.Text0.Length != 0)
                throw Invalid(operation, "contains fields outside the Camera payload schema");

            switch (operation.Code)
            {
                case SimulationOperationCode.CameraStateRequest:
                    RequireEnum(operation, operation.Integer1, 0, 4, "Mode");
                    RequireEnum(operation, checked((int)operation.Flags), 0, 2, "InterruptPolicy");
                    RequireInt32(operation, literals, "Priority");
                    RequireUnit(operation, literals, "Weight");
                    RequireNonNegative(operation, literals, "BlendInSeconds");
                    RequireNonNegative(operation, literals, "BlendOutSeconds");
                    RequireString(operation, literals, "TargetKey", false);
                    RequireString(operation, literals, "SequenceId", true);
                    RequireString(operation, literals, "ActionContext", false);
                    RequireFieldCount(operation, 7);
                    break;
                case SimulationOperationCode.CameraEffectRequest:
                    RequireEnum(operation, operation.Integer1, 1, 6, "EffectKind");
                    RequireEnum(operation, checked((int)operation.Flags), 0, 1, "LockStartCapture");
                    RequireString(operation, literals, "RequestId", true);
                    RequireString(operation, literals, "ResourceId", true);
                    RequireUnit(operation, literals, "Weight");
                    RequireInt32(operation, literals, "Priority");
                    RequireString(operation, literals, "ActionContext", false);
                    RequireFieldCount(operation, 5);
                    break;
                case SimulationOperationCode.CameraResponse:
                    RequireEnum(operation, operation.Integer1, 0, 2, "LookResponse");
                    RequireFlags(operation, 0);
                    RequireUnit(operation, literals, "ManualOrbitWeight");
                    RequireUnit(operation, literals, "PitchResponseWeight");
                    RequireUnit(operation, literals, "YawResponseWeight");
                    RequireInt32(operation, literals, "Priority");
                    RequireUnit(operation, literals, "Weight");
                    RequireString(operation, literals, "ActionContext", false);
                    RequireFieldCount(operation, 6);
                    break;
                case SimulationOperationCode.CameraTarget:
                    RequireEnum(operation, checked((int)operation.Flags), 0, 3, "TargetTimeScale");
                    if (operation.Integer1 <= 0 || (operation.Integer1 & ~AllTargetKeyMasks) != 0)
                        throw Invalid(operation, $"target key mask '{operation.Integer1}' is unknown or empty");
                    string targetKey = RequireString(operation, literals, "TargetKey", false);
                    string anchorKey = RequireString(operation, literals, "AnchorKey", false);
                    string aimPointKey = RequireString(operation, literals, "AimPointKey", false);
                    string preferredBoneKey = RequireString(operation, literals, "PreferredBoneKey", false);
                    int expectedMask = (targetKey.Length > 0 ? TargetKeyMask : 0) |
                                       (anchorKey.Length > 0 ? AnchorKeyMask : 0) |
                                       (aimPointKey.Length > 0 ? AimPointKeyMask : 0) |
                                       (preferredBoneKey.Length > 0 ? PreferredBoneKeyMask : 0);
                    if (operation.Integer1 != expectedMask)
                        throw Invalid(operation, $"target key mask '{operation.Integer1}' does not match configured target identities '{expectedMask}'");
                    RequireInt32(operation, literals, "Priority");
                    RequireUnit(operation, literals, "Weight");
                    RequireString(operation, literals, "ActionContext", false);
                    RequireNonNegative(operation, literals, "FollowDelaySeconds");
                    RequireNonNegative(operation, literals, "FollowDurationSeconds");
                    RequireNonNegative(operation, literals, "AimDelaySeconds");
                    RequireNonNegative(operation, literals, "AimDurationSeconds");
                    RequireFieldCount(operation, 11);
                    break;
                case SimulationOperationCode.CameraBasisRead:
                    RequireFlags(operation, 0);
                    if (operation.Integer1 != 0)
                        throw Invalid(operation, $"field Integer1 must be zero but is '{operation.Integer1}'");
                    RequireFieldCount(operation, 0);
                    break;
                default:
                    throw Invalid(operation, $"operation '{operation.Code}' is unsupported");
            }
        }

        static InvalidOperationException Invalid(SemanticOperation operation, string detail)
        {
            return new InvalidOperationException(
                $"Camera operation '{operation.TemplateIdentity}' ({operation.Code}) {detail}.");
        }
    }

    public static class VfxProgramOperationSchema
    {
        public const int PayloadVersion = 2;
        public static readonly AnimationChannelId ChannelId = new AnimationChannelId("Vfx");

        public static void Validate(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals)
        {
            if (operation.Code != SimulationOperationCode.VfxRequest)
                return;
            if (operation.Integer0 != PayloadVersion || operation.Flags != 0 ||
                operation.Operands.Count != 0 || operation.Unsigned0 != 0 ||
                operation.Number0 != 0d || operation.Text0.Length != 0)
                throw Invalid(operation, "contains fields outside the Vfx payload schema");
            RequireEnum(operation, operation.Integer1, (int)PresentationVfxCommand.Play,
                (int)PresentationVfxCommand.MaintainStateModifier, "Command");
            RequireString(operation, literals, "RequestId", true);
            RequireString(operation, literals, "ResourceId", true);
            RequireString(operation, literals, "AnchorKey", false);
            int target = RequireInt32(operation, literals, "VfxTarget");
            if (target != (int)PresentationVfxTarget.Owner &&
                target != (int)PresentationVfxTarget.ActionTarget &&
                target != (int)PresentationVfxTarget.Camera)
                throw Invalid(operation, $"contains unknown Vfx target '{target}'");
            bool hasPoseOverride = RequireBoolean(operation, literals, "HasPoseOverride");
            int coordinates = RequireInt32(operation, literals, "CoordinatesOption");
            int expectedCoordinates = hasPoseOverride
                ? (int)PresentationVfxCoordinatesOption.ReferTarget
                : (int)PresentationVfxCoordinatesOption.NotConfigured;
            if (coordinates != expectedCoordinates)
                throw Invalid(operation, $"coordinates '{coordinates}' do not match the explicit pose override");
            RequireBoolean(operation, literals, "UseRotationY");
            RequireBoolean(operation, literals, "UseRotationXZ");
            RequireBoolean(operation, literals, "NeedRaycastGround");
            RequireBoolean(operation, literals, "UseAttachPointRotation");
            RequireNumber(operation, literals, "LocalPositionX");
            RequireNumber(operation, literals, "LocalPositionY");
            RequireNumber(operation, literals, "LocalPositionZ");
            RequireNumber(operation, literals, "LocalEulerAnglesX");
            RequireNumber(operation, literals, "LocalEulerAnglesY");
            RequireNumber(operation, literals, "LocalEulerAnglesZ");
            RequireNumber(operation, literals, "LocalScaleX");
            RequireNumber(operation, literals, "LocalScaleY");
            RequireNumber(operation, literals, "LocalScaleZ");
            RequireFieldCount(operation, 19);
        }

        static InvalidOperationException Invalid(SemanticOperation operation, string detail) =>
            new InvalidOperationException(
                $"Vfx operation '{operation.TemplateIdentity}' ({operation.Code}) {detail}.");
    }

    internal static class PresentationProgramPayloadSchema
    {
        internal static void RequireFieldCount(SemanticOperation operation, int expected)
        {
            if (operation.LiteralReferences.Count != expected)
                throw Invalid(operation, $"requires exactly {expected} payload fields but contains {operation.LiteralReferences.Count}");
        }

        internal static void RequireEnum(SemanticOperation operation, int value, int minimum, int maximum, string field)
        {
            if (value < minimum || value > maximum)
                throw Invalid(operation, $"field '{field}' contains unknown enum value '{value}'");
        }

        internal static void RequireFlags(SemanticOperation operation, uint expected)
        {
            if (operation.Flags != expected)
                throw Invalid(operation, $"flags '{operation.Flags}' are unsupported");
        }

        internal static int RequireInt32(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals, string field)
        {
            SemanticLiteral literal = RequireLiteral(operation, literals, field);
            if (literal.Kind != SemanticLiteralKind.Int32)
                throw Invalid(operation, $"field '{field}' must be Int32");
            return literal.Int32;
        }

        internal static bool RequireBoolean(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals, string field)
        {
            SemanticLiteral literal = RequireLiteral(operation, literals, field);
            if (literal.Kind != SemanticLiteralKind.Boolean)
                throw Invalid(operation, $"field '{field}' must be Boolean");
            return literal.Boolean;
        }

        internal static string RequireString(
            SemanticOperation operation,
            IReadOnlyList<SemanticLiteral> literals,
            string field,
            bool requireValue)
        {
            SemanticLiteral literal = RequireLiteral(operation, literals, field);
            if (literal.Kind != SemanticLiteralKind.String)
                throw Invalid(operation, $"field '{field}' must be String");
            if (requireValue && string.IsNullOrWhiteSpace(literal.Text))
                throw Invalid(operation, $"field '{field}' is required");
            if (!string.Equals(literal.Text, literal.Text.Trim(), StringComparison.Ordinal))
                throw Invalid(operation, $"field '{field}' must not contain leading or trailing whitespace");
            return literal.Text;
        }

        internal static void RequireUnit(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals, string field)
        {
            double value = RequireNumber(operation, literals, field);
            if (value < 0d || value > 1d)
                throw Invalid(operation, $"field '{field}' must be in [0, 1]");
        }

        internal static void RequireNonNegative(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals, string field)
        {
            if (RequireNumber(operation, literals, field) < 0d)
                throw Invalid(operation, $"field '{field}' must be non-negative");
        }

        internal static double RequireNumber(SemanticOperation operation, IReadOnlyList<SemanticLiteral> literals, string field)
        {
            SemanticLiteral literal = RequireLiteral(operation, literals, field);
            if (literal.Kind != SemanticLiteralKind.Number)
                throw Invalid(operation, $"field '{field}' must be Number");
            return literal.X;
        }

        static SemanticLiteral RequireLiteral(
            SemanticOperation operation,
            IReadOnlyList<SemanticLiteral> literals,
            string field)
        {
            SemanticLiteral result = null;
            string suffix = "/constant/" + field;
            for (int i = 0; i < operation.LiteralReferences.Count; i++)
            {
                int index = operation.LiteralReferences[i];
                if (index < 0 || index >= literals.Count)
                    throw Invalid(operation, $"field '{field}' references literal '{index}' outside the table");
                SemanticLiteral literal = literals[index];
                if (!literal.Identity.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                if (result != null)
                    throw Invalid(operation, $"field '{field}' is duplicated");
                result = literal;
            }
            return result ?? throw Invalid(operation, $"field '{field}' is missing");
        }

        static InvalidOperationException Invalid(SemanticOperation operation, string detail)
        {
            return new InvalidOperationException(
                $"Presentation operation '{operation.TemplateIdentity}' ({operation.Code}) {detail}.");
        }
    }

    public enum ProgramControlFlowKind : byte
    {
        Child = 1,
        Transition = 2,
        Enter = 3,
        Exit = 4,
        Interrupt = 5,
        Value = 6
    }

    public enum ProgramAbortPolicy : byte
    {
        None = 0,
        Self = 1,
        LowerPriority = 2,
        Both = 3
    }

    public sealed class ProgramControlFlowEdge
    {
        public ProgramControlFlowEdge(
            string identity,
            OperationHandle source,
            OperationHandle target,
            string sourcePort,
            string targetPort,
            ProgramControlFlowKind kind,
            int order,
            int priority,
            ProgramAbortPolicy abortPolicy,
            bool hasCondition,
            OperationHandle condition)
        {
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            if (!source.IsValid || !target.IsValid || hasCondition && !condition.IsValid)
                throw new ArgumentException("Control-flow edge contains an invalid operation handle.");
            Source = source;
            Target = target;
            SourcePort = sourcePort ?? string.Empty;
            TargetPort = targetPort ?? string.Empty;
            Kind = kind;
            Order = order;
            Priority = priority;
            AbortPolicy = abortPolicy;
            HasCondition = hasCondition;
            Condition = condition;
        }

        public string Identity { get; }
        public OperationHandle Source { get; }
        public OperationHandle Target { get; }
        public string SourcePort { get; }
        public string TargetPort { get; }
        public ProgramControlFlowKind Kind { get; }
        public int Order { get; }
        public int Priority { get; }
        public ProgramAbortPolicy AbortPolicy { get; }
        public bool HasCondition { get; }
        public OperationHandle Condition { get; }
    }

    public sealed class ProgramConstantInputBinding
    {
        public ProgramConstantInputBinding(
            OperationHandle targetOperation,
            string targetPort,
            int constantIndex,
            SemanticValueKind resolvedValueKind)
        {
            if (!targetOperation.IsValid)
                throw new ArgumentException("Target operation is invalid.", nameof(targetOperation));
            if (constantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(constantIndex));
            TargetOperation = targetOperation;
            TargetPort = SimulationIdentity.Require(targetPort, nameof(targetPort));
            ConstantIndex = constantIndex;
            ResolvedValueKind = resolvedValueKind;
        }

        public OperationHandle TargetOperation { get; }
        public string TargetPort { get; }
        public int ConstantIndex { get; }
        public SemanticValueKind ResolvedValueKind { get; }
    }

    public enum ProgramReferenceKind : byte
    {
        Operation = 1,
        Constant = 2,
        StateSlot = 3,
        Scope = 4,
        OutputChannel = 6,
        Producer = 7,
        CatalogEntry = 8
    }

    public sealed class ProgramReference
    {
        public ProgramReference(string identity, OperationHandle sourceOperation, ProgramReferenceKind kind, int targetIndex, string externalIdentity)
        {
            if (targetIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(targetIndex));
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            SourceOperation = sourceOperation;
            Kind = kind;
            TargetIndex = targetIndex;
            ExternalIdentity = externalIdentity ?? string.Empty;
        }
        public string Identity { get; }
        public OperationHandle SourceOperation { get; }
        public bool HasSourceOperation => SourceOperation.IsValid;
        public ProgramReferenceKind Kind { get; }
        public int TargetIndex { get; }
        public string ExternalIdentity { get; }
    }

    public enum ProgramGraphParameterDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public sealed class ProgramGraphParameterBinding
    {
        public ProgramGraphParameterBinding(
            ProgramGraphParameterDirection direction,
            string parameterName,
            string declarationIdentity,
            int stateSlot,
            string portId,
            SemanticValueKind valueKind)
        {
            if (!Enum.IsDefined(typeof(ProgramGraphParameterDirection), direction) ||
                stateSlot < 0 ||
                !Enum.IsDefined(typeof(SemanticValueKind), valueKind))
            {
                throw new ArgumentException("Program graph parameter binding is incomplete.");
            }
            Direction = direction;
            ParameterName = SimulationIdentity.Require(parameterName, nameof(parameterName));
            DeclarationIdentity = SimulationIdentity.Require(declarationIdentity, nameof(declarationIdentity));
            StateSlot = stateSlot;
            PortId = SimulationIdentity.Require(portId, nameof(portId));
            ValueKind = valueKind;
        }

        public ProgramGraphParameterDirection Direction { get; }
        public string ParameterName { get; }
        public string DeclarationIdentity { get; }
        public int StateSlot { get; }
        public string PortId { get; }
        public SemanticValueKind ValueKind { get; }
    }

    public sealed class ProgramGraphCallFrame
    {
        readonly ReadOnlyCollection<ProgramGraphParameterBinding> m_Inputs;
        readonly ReadOnlyCollection<ProgramGraphParameterBinding> m_Outputs;

        public ProgramGraphCallFrame(
            int index,
            string identity,
            OperationHandle ownerOperation,
            OperationHandle entryOperation,
            string childGraphIdentity,
            IEnumerable<ProgramGraphParameterBinding> inputs,
            IEnumerable<ProgramGraphParameterBinding> outputs)
        {
            if (index < 0 || !ownerOperation.IsValid || !entryOperation.IsValid)
                throw new ArgumentException("Program graph call frame identity is incomplete.");
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            OwnerOperation = ownerOperation;
            EntryOperation = entryOperation;
            ChildGraphIdentity = SimulationIdentity.Require(childGraphIdentity, nameof(childGraphIdentity));
            m_Inputs = Freeze(inputs, ProgramGraphParameterDirection.Input, "input");
            m_Outputs = Freeze(outputs, ProgramGraphParameterDirection.Output, "output");
        }

        public int Index { get; }
        public string Identity { get; }
        public OperationHandle OwnerOperation { get; }
        public OperationHandle EntryOperation { get; }
        public string ChildGraphIdentity { get; }
        public IReadOnlyList<ProgramGraphParameterBinding> Inputs => m_Inputs;
        public IReadOnlyList<ProgramGraphParameterBinding> Outputs => m_Outputs;

        static ReadOnlyCollection<ProgramGraphParameterBinding> Freeze(
            IEnumerable<ProgramGraphParameterBinding> source,
            ProgramGraphParameterDirection direction,
            string label)
        {
            var values = new List<ProgramGraphParameterBinding>(source ?? Array.Empty<ProgramGraphParameterBinding>());
            values.Sort((left, right) =>
            {
                int byParameter = string.CompareOrdinal(left.ParameterName, right.ParameterName);
                return byParameter != 0 ? byParameter : string.CompareOrdinal(left.PortId, right.PortId);
            });
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ports = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                ProgramGraphParameterBinding value = values[i]
                    ?? throw new ArgumentException($"Program graph call frame {label} binding is missing.", nameof(source));
                if (value.Direction != direction || !names.Add(value.ParameterName) || !ports.Add(value.PortId))
                    throw new ArgumentException($"Program graph call frame {label} bindings are duplicated or use the wrong direction.", nameof(source));
            }
            return values.AsReadOnly();
        }
    }

    public static class ProgramGraphCallFrameContract
    {
        public static void ValidateCoverage(
            IReadOnlyList<SimulationOperationCode> operationCodes,
            IReadOnlyList<ProgramGraphCallFrame> frames)
        {
            if (operationCodes == null)
                throw new ArgumentNullException(nameof(operationCodes));
            if (frames == null)
                throw new ArgumentNullException(nameof(frames));
            var counts = new int[operationCodes.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                ProgramGraphCallFrame frame = frames[i]
                    ?? throw new ArgumentException("Graph call frame collection contains a missing frame.", nameof(frames));
                if (frame.OwnerOperation.Value < 0 || frame.OwnerOperation.Value >= operationCodes.Count ||
                    frame.EntryOperation.Value < 0 || frame.EntryOperation.Value >= operationCodes.Count)
                {
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' references an operation outside the table.");
                }
                Validate(
                    frame,
                    operationCodes[frame.OwnerOperation.Value],
                    operationCodes[frame.EntryOperation.Value]);
                counts[frame.OwnerOperation.Value]++;
            }
            for (int i = 0; i < operationCodes.Count; i++)
            {
                bool isSubGraph = operationCodes[i] == SimulationOperationCode.SubGraph;
                if (isSubGraph && counts[i] != 1)
                    throw new InvalidDataException($"SubGraph operation '{i}' requires exactly one graph call frame, received '{counts[i]}'.");
                if (!isSubGraph && counts[i] != 0)
                    throw new InvalidDataException($"Operation '{i}' is not a SubGraph but owns a graph call frame.");
            }
        }

        public static void Validate(
            ProgramGraphCallFrame frame,
            SimulationOperationCode ownerCode,
            SimulationOperationCode entryCode)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (ownerCode != SimulationOperationCode.SubGraph)
                throw new InvalidDataException($"Graph call frame '{frame.Identity}' owner operation must be SubGraph, received '{ownerCode}'.");
            if (entryCode != SimulationOperationCode.Root)
                throw new InvalidDataException($"Graph call frame '{frame.Identity}' entry operation must be Root, received '{entryCode}'.");
        }
    }

    public enum ProgramStateValueKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Scalar = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        Identity = 8,
        BlackboardOwnerToken = 9,
        BlackboardWriteStamp = 10,
        Quaternion = 11,
        Enum = 12,
        ActionTargetSnapshot = 24
    }

    public enum ProgramStateOwnerKind : byte
    {
        Runnable = 2,
        StateMachine = 3,
        Timeline = 4,
        Blackboard = 5,
        Action = 6,
        GameplayEffect = 7,
    }

    public enum ProgramStateSemantic : ushort
    {
        RunnableLifecycle = 1,
        RunnableChildCursor = 2,
        RunnableStopBarrier = 3,
        RunnableActivationGeneration = 4,
        LocomotionMotionElapsedTicks = 5,
        GameplayEffectAppliedHandle = 6,
        StateMachineActive = 20,
        StateMachinePending = 21,
        StateMachineExiting = 22,
        StateMachineTransition = 23,
        StateMachineExecutionPath = 24,
        TimelinePlayback = 40,
        TimelineLoop = 41,
        BlackboardValue = 60,
        BlackboardOwnerToken = 61,
        BlackboardLifetime = 63,
        BlackboardWriteStamp = 64,
        AIWaitElapsedTicks = 130
    }

    public sealed class ProgramStateSlot
    {
        public ProgramStateSlot(int index, string identity, ProgramStateValueKind valueKind, ProgramStateOwnerKind ownerKind, ProgramStateSemantic semantic, string ownerIdentity, int defaultConstantIndex)
            : this(index, identity, valueKind, ownerKind, semantic, ownerIdentity, null, defaultConstantIndex)
        {
        }

        public ProgramStateSlot(
            int index,
            string identity,
            ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            string stateCodecIdentity,
            int defaultConstantIndex)
        {
            if (index < 0 || defaultConstantIndex < -1)
                throw new ArgumentOutOfRangeException();
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            ValueKind = valueKind;
            OwnerKind = ownerKind;
            Semantic = semantic;
            OwnerIdentity = SimulationIdentity.Require(ownerIdentity, nameof(ownerIdentity));
            DefaultConstantIndex = defaultConstantIndex;
            StateCodecIdentity = stateCodecIdentity == null
                ? ProgramStateSchema.CodecIdentity(valueKind)
                : SimulationIdentity.Require(stateCodecIdentity, nameof(stateCodecIdentity));
            ProgramStateSchema.RequireSlot(valueKind, ownerKind, semantic);
        }
        public int Index { get; }
        public string Identity { get; }
        public ProgramStateValueKind ValueKind { get; }
        public ProgramStateOwnerKind OwnerKind { get; }
        public ProgramStateSemantic Semantic { get; }
        public string OwnerIdentity { get; }
        public int DefaultConstantIndex { get; }
        public string StateCodecIdentity { get; }
    }

    public static class ProgramStateSchema
    {
        public static string CodecIdentity(ProgramStateValueKind kind)
        {
            return kind switch
            {
                ProgramStateValueKind.Boolean => "state.boolean/v1",
                ProgramStateValueKind.Int32 => "state.int32/v1",
                ProgramStateValueKind.UInt64 => "state.uint64/v1",
                ProgramStateValueKind.Scalar => "state.float32-scalar/v1",
                ProgramStateValueKind.Vector2 => "state.float32-vector2/v1",
                ProgramStateValueKind.Vector3 => "state.float32-vector3/v1",
                ProgramStateValueKind.Quaternion => "state.float32-quaternion/v1",
                ProgramStateValueKind.Enum => "state.enum/v1",
                ProgramStateValueKind.Yaw => "state.float32-yaw/v1",
                ProgramStateValueKind.Identity => "state.identity/v1",
                ProgramStateValueKind.BlackboardOwnerToken => "state.blackboard-owner-token/v1",
                ProgramStateValueKind.BlackboardWriteStamp => "state.blackboard-write-stamp/v3",
                ProgramStateValueKind.ActionTargetSnapshot => "state.action-target-snapshot/v1",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        public static void RequireSlot(
            ProgramStateValueKind kind,
            ProgramStateOwnerKind owner,
            ProgramStateSemantic semantic)
        {
            bool valid = semantic switch
            {
                ProgramStateSemantic.RunnableLifecycle => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableChildCursor => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableStopBarrier => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.RunnableActivationGeneration => kind == ProgramStateValueKind.UInt64 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.GameplayEffectAppliedHandle => kind == ProgramStateValueKind.UInt64 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.LocomotionMotionElapsedTicks => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.AIWaitElapsedTicks => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Runnable,
                ProgramStateSemantic.StateMachineActive => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachinePending => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineExiting => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineTransition => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.StateMachineExecutionPath => kind == ProgramStateValueKind.Identity && owner == ProgramStateOwnerKind.StateMachine,
                ProgramStateSemantic.TimelinePlayback => kind == ProgramStateValueKind.UInt64 && owner == ProgramStateOwnerKind.Timeline,
                ProgramStateSemantic.TimelineLoop => kind == ProgramStateValueKind.Boolean && owner == ProgramStateOwnerKind.Timeline,
                ProgramStateSemantic.BlackboardValue => IsBlackboardValue(kind) && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardOwnerToken => kind == ProgramStateValueKind.BlackboardOwnerToken && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardLifetime => kind == ProgramStateValueKind.Int32 && owner == ProgramStateOwnerKind.Blackboard,
                ProgramStateSemantic.BlackboardWriteStamp => kind == ProgramStateValueKind.BlackboardWriteStamp && owner == ProgramStateOwnerKind.Blackboard,
                _ => false
            };
            if (!valid)
                throw new ArgumentException($"State semantic '{semantic}' cannot use kind '{kind}' with owner '{owner}'.");
        }

        static bool IsBlackboardValue(ProgramStateValueKind kind)
        {
            return kind == ProgramStateValueKind.Boolean ||
                   kind == ProgramStateValueKind.Int32 ||
                   kind == ProgramStateValueKind.UInt64 ||
                   kind == ProgramStateValueKind.Scalar ||
                   kind == ProgramStateValueKind.Vector2 ||
                   kind == ProgramStateValueKind.Vector3 ||
                   kind == ProgramStateValueKind.Yaw ||
                   kind == ProgramStateValueKind.Identity ||
                   kind == ProgramStateValueKind.Quaternion ||
                   kind == ProgramStateValueKind.Enum ||
                   kind == ProgramStateValueKind.ActionTargetSnapshot;
        }

    }

    public enum ProgramScopeKind : byte
    {
        Character = 1,
        Graph = 2,
        State = 3,
        ActionInstance = 4,
        Frame = 5
    }

    public readonly struct BlackboardOwnerToken : IEquatable<BlackboardOwnerToken>
    {
        public BlackboardOwnerToken(ProgramScopeKind scopeKind, int compiledOwnerIndex, ulong generation)
        {
            if (!IsDefined(scopeKind) || compiledOwnerIndex < 0 || generation == 0)
                throw new ArgumentException("Blackboard owner token is incomplete.");
            ScopeKind = scopeKind;
            CompiledOwnerIndex = compiledOwnerIndex;
            Generation = generation;
        }

        public ProgramScopeKind ScopeKind { get; }
        public int CompiledOwnerIndex { get; }
        public ulong Generation { get; }
        public bool IsValid => CompiledOwnerIndex >= 0 && Generation != 0 && IsDefined(ScopeKind);
        public bool Equals(BlackboardOwnerToken other) =>
            ScopeKind == other.ScopeKind &&
            CompiledOwnerIndex == other.CompiledOwnerIndex &&
            Generation == other.Generation;
        public override bool Equals(object obj) => obj is BlackboardOwnerToken other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)ScopeKind, CompiledOwnerIndex, Generation);
        public static bool operator ==(BlackboardOwnerToken left, BlackboardOwnerToken right) => left.Equals(right);
        public static bool operator !=(BlackboardOwnerToken left, BlackboardOwnerToken right) => !left.Equals(right);

        static bool IsDefined(ProgramScopeKind kind) =>
            kind >= ProgramScopeKind.Character && kind <= ProgramScopeKind.Frame;
    }

    public readonly struct BlackboardWriteStamp : IEquatable<BlackboardWriteStamp>
    {
        public BlackboardWriteStamp(
            OperationHandle sourceOperation,
            ulong logicTick,
            GameplayAbilitySourceContext sourceContext,
            OperationHandle timelineOperation,
            OperationHandle clipOperation,
            int cycle,
            ulong commandSequence = 0)
        {
            if (sourceOperation.IsValid == (commandSequence != 0) || logicTick == 0 ||
                timelineOperation.IsValid != clipOperation.IsValid || cycle < 0 ||
                commandSequence != 0 && (timelineOperation.IsValid || cycle != 0))
            {
                throw new ArgumentException("Blackboard write stamp is incomplete.");
            }
            SourceOperation = sourceOperation;
            LogicTick = logicTick;
            SourceContext = sourceContext;
            TimelineOperation = timelineOperation;
            ClipOperation = clipOperation;
            Cycle = cycle;
            CommandSequence = commandSequence;
        }

        public OperationHandle SourceOperation { get; }
        public ulong LogicTick { get; }
        public GameplayAbilitySourceContext SourceContext { get; }
        public ulong ActionInstanceId => SourceContext.InstanceId;
        public OperationHandle TimelineOperation { get; }
        public OperationHandle ClipOperation { get; }
        public int Cycle { get; }
        public ulong CommandSequence { get; }
        public bool IsValid => (SourceOperation.IsValid || CommandSequence != 0) && LogicTick != 0;
        public bool Equals(BlackboardWriteStamp other) =>
            SourceOperation.Equals(other.SourceOperation) &&
            LogicTick == other.LogicTick &&
            SourceContext.Equals(other.SourceContext) &&
            TimelineOperation.Equals(other.TimelineOperation) &&
            ClipOperation.Equals(other.ClipOperation) &&
            Cycle == other.Cycle &&
            CommandSequence == other.CommandSequence;
        public override bool Equals(object obj) => obj is BlackboardWriteStamp other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SourceOperation, LogicTick, SourceContext, TimelineOperation, ClipOperation, Cycle, CommandSequence);
    }

    public enum ProgramBlackboardLifetime : byte
    {
        Config = 0,
        Spawn = 1,
        StateEnterToExit = 2,
        ActionInstance = 3,
        Frame = 4,
        ManualClear = 5,
        GraphInstance = 6
    }

    public enum ProgramBlackboardFactProjectionKind : byte
    {
        ActionWindow = 0
    }
    public sealed class ProgramScopeLayout
    {
        readonly ReadOnlyCollection<int> m_StateSlots;

        public ProgramScopeLayout(
            int compiledOwnerIndex,
            string identity,
            ProgramScopeKind kind,
            string ownerIdentity,
            OperationHandle ownerOperation,
            IEnumerable<int> stateSlots)
        {
            if (compiledOwnerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(compiledOwnerIndex));
            CompiledOwnerIndex = compiledOwnerIndex;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Kind = kind;
            OwnerIdentity = SimulationIdentity.Require(ownerIdentity, nameof(ownerIdentity));
            OwnerOperation = ownerOperation;
            var values = stateSlots == null ? new List<int>() : new List<int>(stateSlots);
            values.Sort();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < 0 || i > 0 && values[i - 1] == values[i])
                    throw new ArgumentException("Program scope state slots must be non-negative and unique.", nameof(stateSlots));
            }
            m_StateSlots = values.AsReadOnly();
        }

        public int CompiledOwnerIndex { get; }
        public string Identity { get; }
        public ProgramScopeKind Kind { get; }
        public string OwnerIdentity { get; }
        public OperationHandle OwnerOperation { get; }
        public IReadOnlyList<int> StateSlots => m_StateSlots;
    }

    public enum ProgramOutputChannelKind : byte
    {
        GameplayFact = 1,
        Presentation = 2,
        Trace = 3
    }

    public sealed class ProgramOutputChannelLayout
    {
        public ProgramOutputChannelLayout(int index, string identity, ProgramOutputChannelKind kind)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Kind = kind;
        }

        public int Index { get; }
        public string Identity { get; }
        public ProgramOutputChannelKind Kind { get; }
    }

    public enum ProgramSourceTargetKind : byte
    {
        Operation = 1,
        Constant = 2,
        StateSlot = 3,
        Reference = 4,
        Producer = 5,
        CatalogEntry = 6,
        BodyMotion = 7,
        ControlModule = 8,
        ControlTransition = 10,
        OperationPort = 11,
        GraphInvocation = 12,
        OptimizedAway = 13
    }

    public enum ProgramValuePortDirection : byte
    {
        None,
        Input,
        Output
    }

    public enum ProgramInvocationCallerKind : byte
    {
        None,
        Node,
        Edge,
        TimelineClip,
        PresentationMarker,
        PresentationTreeClip,
        AbilityRule
    }

	public sealed class ProgramSourceMapEntry
	{
		public ProgramSourceMapEntry(
			ProgramSourceTargetKind targetKind,
			int targetIndex,
			string sourceType,
			string graphId,
			string nodeId,
			string portId,
			string edgeId,
			string declarationId,
			string timelineId,
			string trackId,
			string clipId,
			string displayPath,
			string contentHash,
			string graphInvocationPath = "",
			string compiledPortId = "",
			ProgramValuePortDirection valuePortDirection = ProgramValuePortDirection.None,
			string sourceInvocationPath = "",
			string parentInvocationPath = "",
			ProgramInvocationCallerKind invocationCallerKind = ProgramInvocationCallerKind.None,
			string invocationCallerId = "",
			string invocationCallerClipId = "")
		{
			if (targetIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(targetIndex));
			TargetKind = targetKind;
			TargetIndex = targetIndex;
			SourceType = SimulationIdentity.Require(sourceType, nameof(sourceType));
			GraphId = graphId ?? string.Empty;
			NodeId = nodeId ?? string.Empty;
			PortId = portId ?? string.Empty;
			EdgeId = edgeId ?? string.Empty;
			DeclarationId = declarationId ?? string.Empty;
			TimelineId = timelineId ?? string.Empty;
			TrackId = trackId ?? string.Empty;
			ClipId = clipId ?? string.Empty;
			DisplayPath = displayPath ?? string.Empty;
			ContentHash = contentHash ?? string.Empty;
			GraphInvocationPath = graphInvocationPath ?? string.Empty;
			CompiledPortId = compiledPortId ?? string.Empty;
			ValuePortDirection = valuePortDirection;
			SourceInvocationPath = sourceInvocationPath ?? string.Empty;
			ParentInvocationPath = parentInvocationPath ?? string.Empty;
			InvocationCallerKind = invocationCallerKind;
			InvocationCallerId = invocationCallerId ?? string.Empty;
			InvocationCallerClipId = invocationCallerClipId ?? string.Empty;
			if (GraphId.Length == 0 && TimelineId.Length == 0 && targetKind != ProgramSourceTargetKind.BodyMotion)
				throw new ArgumentException("Source map entry requires a graph or timeline identity.");
		}

		public ProgramSourceTargetKind TargetKind { get; }
		public int TargetIndex { get; }
		public string SourceType { get; }
		public string GraphId { get; }
		public string NodeId { get; }
		public string PortId { get; }
		public string EdgeId { get; }
		public string DeclarationId { get; }
		public string TimelineId { get; }
		public string TrackId { get; }
		public string ClipId { get; }
		public string DisplayPath { get; }
		public string ContentHash { get; }
		public string GraphInvocationPath { get; }
		public string CompiledPortId { get; }
		public ProgramValuePortDirection ValuePortDirection { get; }
		public string SourceInvocationPath { get; }
		public string ParentInvocationPath { get; }
		public ProgramInvocationCallerKind InvocationCallerKind { get; }
		public string InvocationCallerId { get; }
		public string InvocationCallerClipId { get; }
	}

    public static class ProgramSourceMapCoverage
    {
        public static void Require(
            IReadOnlyList<ProgramSourceMapEntry> entries,
            int operationCount,
            int stateSlotCount)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (operationCount < 0)
                throw new ArgumentOutOfRangeException(nameof(operationCount));
            if (stateSlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(stateSlotCount));

            var operations = new bool[operationCount];
            var stateSlots = new bool[stateSlotCount];
            var invocations = new Dictionary<string, ProgramSourceMapEntry>(StringComparer.Ordinal);
            var invocationSources = new Dictionary<string, List<ProgramSourceMapEntry>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramSourceMapEntry entry = entries[i] ??
                    throw new InvalidDataException("Program source map contains a missing entry.");
                switch (entry.TargetKind)
                {
                    case ProgramSourceTargetKind.Operation:
                        RequireTarget(entry.TargetIndex, operations, "operation");
                        break;
                    case ProgramSourceTargetKind.OperationPort:
                        if (entry.TargetIndex >= operationCount || string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.NodeId) || string.IsNullOrEmpty(entry.PortId) ||
                            string.IsNullOrEmpty(entry.SourceInvocationPath) ||
                            !Enum.IsDefined(typeof(ProgramValuePortDirection), entry.ValuePortDirection) ||
                            (entry.ValuePortDirection == ProgramValuePortDirection.None) != string.IsNullOrEmpty(entry.CompiledPortId))
                            throw new InvalidDataException("Program operation-port source is incomplete.");
                        break;
                    case ProgramSourceTargetKind.GraphInvocation:
                        if (entry.TargetIndex >= operationCount || string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.GraphInvocationPath) || string.IsNullOrEmpty(entry.SourceInvocationPath) ||
                            !invocations.TryAdd(entry.GraphInvocationPath, entry) ||
                            !Enum.IsDefined(typeof(ProgramInvocationCallerKind), entry.InvocationCallerKind) ||
                            (entry.InvocationCallerKind == ProgramInvocationCallerKind.None) != string.IsNullOrEmpty(entry.ParentInvocationPath) ||
                            (entry.InvocationCallerKind == ProgramInvocationCallerKind.None) != string.IsNullOrEmpty(entry.InvocationCallerId) ||
                            (entry.InvocationCallerKind == ProgramInvocationCallerKind.TimelineClip ||
                             entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationMarker ||
                             entry.InvocationCallerKind == ProgramInvocationCallerKind.PresentationTreeClip) != !string.IsNullOrEmpty(entry.InvocationCallerClipId))
                            throw new InvalidDataException("Program graph invocation source is incomplete.");
                        if (!invocationSources.TryGetValue(entry.SourceInvocationPath, out List<ProgramSourceMapEntry> sourceInvocations))
                        {
                            sourceInvocations = new List<ProgramSourceMapEntry>();
                            invocationSources.Add(entry.SourceInvocationPath, sourceInvocations);
                        }
                        sourceInvocations.Add(entry);
                        break;
                    case ProgramSourceTargetKind.OptimizedAway:
                        if (string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.NodeId) ||
                            string.IsNullOrEmpty(entry.ContentHash))
                            throw new InvalidDataException("Program optimized source is incomplete.");
                        break;
                    case ProgramSourceTargetKind.StateSlot:
                        RequireTarget(entry.TargetIndex, stateSlots, "state slot");
                        break;
                }
            }

            foreach (KeyValuePair<string, List<ProgramSourceMapEntry>> source in invocationSources)
            {
                string parent = source.Value[0].ParentInvocationPath;
                for (int i = 1; i < source.Value.Count; i++)
                    if (!string.Equals(parent, source.Value[i].ParentInvocationPath, StringComparison.Ordinal))
                        throw new InvalidDataException("Program graph invocation hierarchy is invalid.");
                var visited = new HashSet<string>(StringComparer.Ordinal) { source.Key };
                while (!string.IsNullOrEmpty(parent))
                {
                    if (!visited.Add(parent) || !invocationSources.TryGetValue(parent, out List<ProgramSourceMapEntry> owners))
                        throw new InvalidDataException("Program graph invocation hierarchy is invalid.");
                    parent = owners[0].ParentInvocationPath;
                }
            }

            for (int i = 0; i < operations.Length; i++)
            {
                if (!operations[i])
                    throw new InvalidDataException($"Program operation '{i}' is absent from the source map.");
            }
            for (int i = 0; i < stateSlots.Length; i++)
            {
                if (!stateSlots[i])
                    throw new InvalidDataException($"Program state slot '{i}' is absent from the source map.");
            }
        }

        static void RequireTarget(int index, bool[] targets, string kind)
        {
            if ((uint)index >= (uint)targets.Length)
                throw new InvalidDataException($"Program source map {kind} target '{index}' is outside the Program.");
            if (targets[index])
                throw new InvalidDataException($"Program source map {kind} target '{index}' is duplicated.");
            targets[index] = true;
        }
    }

    public sealed class ProgramProducer
    {
        public ProgramProducer(
            int index,
            string identity,
            AnimationChannelId animationChannelId,
            string sourceIdentity,
            ProgramOutputChannelKind channelKind)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (!animationChannelId.IsValid)
                throw new ArgumentException("Program Producer Animation Channel identity is invalid.", nameof(animationChannelId));
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            AnimationChannelId = animationChannelId;
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            ChannelKind = channelKind;
        }
        public int Index { get; }
        public string Identity { get; }
        public AnimationChannelId AnimationChannelId { get; }
        public string SourceIdentity { get; }
        public ProgramOutputChannelKind ChannelKind { get; }
    }

    public enum ProgramCatalogEntryKind : byte
    {
        InputValue = 1,
        InputRequest = 2,
        BlackboardDeclaration = 3,
        Action = 4,
        Behavior = 5,
        GameplayTag = 6,
        Attribute = 7,
        GameplayEffect = 8,
        Timeline = 9,
        TimelineTrack = 10,
        TimelineClip = 11,
        MotionCurve = 12,
        CompositionRoot = 20,
        EquipmentSlot = 21,
        EquipmentRoute = 22,
        EquipmentFeature = 23,
        EquipmentFeatureParameter = 24,
        EquipmentFeatureLocalState = 25,
        EquipmentRouteImplementation = 26,
        EquipmentDefinition = 27,
        EquipmentParameterValue = 28,
        EquipmentInitialLoadout = 29,
        EquipmentVisualBinding = 30,
        ControlModule = 31,
        AbilityProgram = 32,
        TimelineBinding = 33,
        CharacterState = 34,
        AbilityActivationRule = 35,
        AbilityMovementExitRule = 36
    }

    public enum ProgramCatalogFieldKind : byte
    {
        Constant = 1,
        Identity = 2
    }

    public sealed class ProgramCatalogField
    {
        public ProgramCatalogField(string name, ProgramCatalogFieldKind kind, int constantIndex, string identity)
        {
            Name = SimulationIdentity.Require(name, nameof(name));
            Kind = kind;
            if (kind == ProgramCatalogFieldKind.Constant && constantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(constantIndex));
            ConstantIndex = constantIndex;
            Identity = kind == ProgramCatalogFieldKind.Identity
                ? SimulationIdentity.Require(identity, nameof(identity))
                : string.Empty;
        }
        public string Name { get; }
        public ProgramCatalogFieldKind Kind { get; }
        public int ConstantIndex { get; }
        public string Identity { get; }
    }

    public sealed class ProgramCatalogEntry
    {
        readonly ReadOnlyCollection<ProgramCatalogField> m_Fields;

        public ProgramCatalogEntry(int index, ProgramCatalogEntryKind kind, string identity, int revision, IEnumerable<ProgramCatalogField> fields)
        {
            if (index < 0 || revision < 0)
                throw new ArgumentOutOfRangeException();
            Index = index;
            Kind = kind;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Revision = revision;
            var copied = fields == null ? new List<ProgramCatalogField>() : new List<ProgramCatalogField>(fields);
            for (int i = 0; i < copied.Count; i++)
            {
                if (copied[i] == null)
                    throw new ArgumentException($"Catalog entry '{identity}' fields must be non-null and unique.", nameof(fields));
            }
            copied.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            for (int i = 0; i < copied.Count; i++)
            {
                if (i > 0 && string.Equals(copied[i - 1].Name, copied[i].Name, StringComparison.Ordinal))
                    throw new ArgumentException($"Catalog entry '{identity}' fields must be non-null and unique.", nameof(fields));
            }
            m_Fields = copied.AsReadOnly();
        }
        public int Index { get; }
        public ProgramCatalogEntryKind Kind { get; }
        public string Identity { get; }
        public int Revision { get; }
        public IReadOnlyList<ProgramCatalogField> Fields => m_Fields;
    }

    public sealed class GameplayAbilityCapabilityManifest
    {
        readonly ReadOnlyCollection<string> m_GameplayCapabilities;

        public GameplayAbilityCapabilityManifest(IEnumerable<string> gameplayCapabilities)
        {
            var values = gameplayCapabilities == null ? new List<string>() : new List<string>(gameplayCapabilities);
            values.Sort(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                values[i] = SimulationIdentity.Require(values[i], nameof(gameplayCapabilities));
                if (i > 0 && string.Equals(values[i - 1], values[i], StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate gameplay capability '{values[i]}'.", nameof(gameplayCapabilities));
            }
            m_GameplayCapabilities = values.AsReadOnly();
        }
        public IReadOnlyList<string> GameplayCapabilities => m_GameplayCapabilities;
        public bool HasGameplayCapability(string capability)
        {
            if (string.IsNullOrEmpty(capability))
                return false;
            for (int i = 0; i < m_GameplayCapabilities.Count; i++)
            {
                if (string.Equals(m_GameplayCapabilities[i], capability, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    public enum PresentationCameraRequestKind : byte
    {
        Sequence = 1,
        Effect = 2,
        Response = 3,
        Target = 4
    }

    public enum PresentationCameraRequestLifecycle : byte
    {
        Activate = 1,
        Retire = 2
    }

    public readonly struct PresentationCameraTargetTiming
    {
        public PresentationCameraTargetTiming(float delaySeconds, float durationSeconds, bool ignoreTimeScale)
        {
            if (!float.IsFinite(delaySeconds) || delaySeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DelaySeconds = delaySeconds;
            DurationSeconds = durationSeconds;
            IgnoreTimeScale = ignoreTimeScale;
        }

        public float DelaySeconds { get; }
        public float DurationSeconds { get; }
        public bool IgnoreTimeScale { get; }
    }

    public readonly struct PresentationCameraRequest
    {
        PresentationCameraRequest(
            PresentationCameraRequestKind kind,
            PresentationCameraRequestLifecycle lifecycle,
            string requestId,
            string sequenceId,
            string resourceId,
            CanonicalTextValue targetKey,
            string anchorKey,
            string aimPointKey,
            string preferredBoneKey,
            int mode,
            int effectKind,
            int interruptPolicy,
            int priority,
            float weight,
            float blendInSeconds,
            float blendOutSeconds,
            float manualOrbitWeight,
            float pitchWeight,
            float yawWeight,
            bool captureLockStartOnSubmit,
            PresentationCameraTargetTiming followTiming = default,
            PresentationCameraTargetTiming aimTiming = default)
        {
            if (!IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!IsDefined(lifecycle))
                throw new ArgumentOutOfRangeException(nameof(lifecycle));
            Kind = kind;
            Lifecycle = lifecycle;
            RequestId = Normalize(requestId);
            SequenceId = Normalize(sequenceId);
            ResourceId = Normalize(resourceId);
            TargetKey = targetKey.IsNull ? string.Empty : targetKey.Trim();
            AnchorKey = Normalize(anchorKey);
            AimPointKey = Normalize(aimPointKey);
            PreferredBoneKey = Normalize(preferredBoneKey);
            Mode = mode;
            EffectKind = effectKind;
            InterruptPolicy = interruptPolicy;
            Priority = priority;
            Weight = RequireFiniteNonNegative(weight, nameof(weight));
            BlendInSeconds = RequireFiniteNonNegative(blendInSeconds, nameof(blendInSeconds));
            BlendOutSeconds = RequireFiniteNonNegative(blendOutSeconds, nameof(blendOutSeconds));
            ManualOrbitWeight = RequireUnit(manualOrbitWeight, nameof(manualOrbitWeight));
            PitchWeight = RequireUnit(pitchWeight, nameof(pitchWeight));
            YawWeight = RequireUnit(yawWeight, nameof(yawWeight));
            CaptureLockStartOnSubmit = captureLockStartOnSubmit;
            FollowTiming = followTiming;
            AimTiming = aimTiming;
        }

        public PresentationCameraRequestKind Kind { get; }
        public PresentationCameraRequestLifecycle Lifecycle { get; }
        public string RequestId { get; }
        public string SequenceId { get; }
        public string ResourceId { get; }
        public CanonicalTextValue TargetKey { get; }
        public string AnchorKey { get; }
        public string AimPointKey { get; }
        public string PreferredBoneKey { get; }
        public int Mode { get; }
        public int EffectKind { get; }
        public int InterruptPolicy { get; }
        public int Priority { get; }
        public float Weight { get; }
        public float BlendInSeconds { get; }
        public float BlendOutSeconds { get; }
        public float ManualOrbitWeight { get; }
        public float PitchWeight { get; }
        public float YawWeight { get; }
        public bool CaptureLockStartOnSubmit { get; }
        public PresentationCameraTargetTiming FollowTiming { get; }
        public PresentationCameraTargetTiming AimTiming { get; }
        public bool IsValid => IsDefined(Kind) && IsDefined(Lifecycle);

        public void Retain() => TargetKey.Retain();
        public void Release() => TargetKey.Release();

        public PresentationCameraRequest WithActionTarget(CanonicalTextValue targetActorId) =>
            new PresentationCameraRequest(
                Kind, Lifecycle, RequestId, SequenceId, ResourceId,
                targetActorId, AnchorKey, AimPointKey, PreferredBoneKey, Mode, EffectKind,
                InterruptPolicy, Priority, Weight, BlendInSeconds, BlendOutSeconds,
                ManualOrbitWeight, PitchWeight, YawWeight, CaptureLockStartOnSubmit,
                FollowTiming, AimTiming);

        public static PresentationCameraRequest Sequence(
            PresentationCameraRequestLifecycle lifecycle,
            string sequenceId,
            int mode,
            int interruptPolicy,
            int priority,
            float weight,
            float blendInSeconds,
            float blendOutSeconds,
            CanonicalTextValue targetKey) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Sequence,
                lifecycle,
                sequenceId,
                sequenceId,
                string.Empty,
                targetKey,
                string.Empty,
                string.Empty,
                string.Empty,
                mode,
                0,
                interruptPolicy,
                priority,
                weight,
                blendInSeconds,
                blendOutSeconds,
                1f,
                1f,
                1f,
                true);

        public static PresentationCameraRequest Effect(
            PresentationCameraRequestLifecycle lifecycle,
            string requestId,
            int effectKind,
            string resourceId,
            int priority,
            float weight,
            bool captureLockStartOnSubmit) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Effect,
                lifecycle,
                requestId,
                string.Empty,
                resourceId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                effectKind,
                0,
                priority,
                weight,
                0f,
                0f,
                1f,
                1f,
                1f,
                captureLockStartOnSubmit);

        public static PresentationCameraRequest Response(
            PresentationCameraRequestLifecycle lifecycle,
            int mode,
            float manualOrbitWeight,
            float pitchWeight,
            float yawWeight,
            int priority,
            float weight) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Response,
                lifecycle,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                mode,
                0,
                0,
                priority,
                weight,
                0f,
                0f,
                manualOrbitWeight,
                pitchWeight,
                yawWeight,
                true);

        public static PresentationCameraRequest Target(
            PresentationCameraRequestLifecycle lifecycle,
            CanonicalTextValue targetKey,
            string anchorKey,
            string aimPointKey,
            string preferredBoneKey,
            int priority,
            float weight,
            PresentationCameraTargetTiming followTiming,
            PresentationCameraTargetTiming aimTiming) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Target,
                lifecycle,
                string.Empty,
                string.Empty,
                string.Empty,
                targetKey,
                anchorKey,
                aimPointKey,
                preferredBoneKey,
                0,
                0,
                0,
                priority,
                weight,
                0f,
                0f,
                1f,
                1f,
                1f,
                true,
                followTiming,
                aimTiming);

        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("Camera request identity must be trimmed.");
            return value;
        }

        static bool IsDefined(PresentationCameraRequestKind kind) =>
            kind >= PresentationCameraRequestKind.Sequence &&
            kind <= PresentationCameraRequestKind.Target;

        static bool IsDefined(PresentationCameraRequestLifecycle lifecycle) =>
            lifecycle >= PresentationCameraRequestLifecycle.Activate &&
            lifecycle <= PresentationCameraRequestLifecycle.Retire;

        static float RequireFiniteNonNegative(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }

        static float RequireUnit(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }
}
