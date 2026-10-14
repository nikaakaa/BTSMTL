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
        AirborneVerticalMotion = 1UL << 6,
        FrameOwned = 1UL << 7
    }

    public enum ActionTargetRequirement : byte
    {
        None = 0,
        SnapshotRequired = 1,
        OptionalSnapshot = 2
    }

    [ValueUnionTemplate, ValueOperations]
    public readonly struct ProgramInputValue<TScalar, TVector2, TVector3, TYaw, TActionTargetSnapshot>
    {
        public readonly CanonicalTextValue InputId;
        [ValueUnionCase]
        public readonly bool Boolean;
        [ValueUnionCase]
        public readonly TScalar Scalar;
        [ValueUnionCase]
        public readonly TVector2 Vector2;
        [ValueUnionCase]
        public readonly TVector3 Vector3;
        [ValueUnionCase]
        public readonly TYaw Yaw;
        [ValueUnionCase]
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
        CanActivateAction = 84,
        ActionEventReceived = 85,
        AbilityActive = 86,
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
        GameplayEffectSourceAllowsCandidate = 117,
        GameplayEffectActive = 118,
        GameplayConfigurationRead = 119,
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
        public static readonly OperationSetVersion Version = new OperationSetVersion(Id + "/33");

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
                SimulationOperationCode.CanActivateAction,
                SimulationOperationCode.ActionEventReceived,
                SimulationOperationCode.AbilityActive,
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
                SimulationOperationCode.GameplayEffectSourceAllowsCandidate,
                SimulationOperationCode.GameplayEffectActive,
                SimulationOperationCode.GameplayConfigurationRead,
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
                    RequireFieldCount(operation, literals, 7);
                    break;
                case SimulationOperationCode.CameraEffectRequest:
                    RequireEnum(operation, operation.Integer1, 1, 6, "EffectKind");
                    RequireEnum(operation, checked((int)operation.Flags), 0, 1, "LockStartCapture");
                    RequireString(operation, literals, "RequestId", true);
                    RequireString(operation, literals, "ResourceId", true);
                    RequireUnit(operation, literals, "Weight");
                    RequireInt32(operation, literals, "Priority");
                    RequireString(operation, literals, "ActionContext", false);
                    RequireFieldCount(operation, literals, 5);
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
                    RequireFieldCount(operation, literals, 6);
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
                    RequireFieldCount(operation, literals, 11);
                    break;
                case SimulationOperationCode.CameraBasisRead:
                    RequireFlags(operation, 0);
                    if (operation.Integer1 != 0)
                        throw Invalid(operation, $"field Integer1 must be zero but is '{operation.Integer1}'");
                    RequireFieldCount(operation, literals, 0);
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
        public const int PayloadVersion = 3;
        public const string PositionInputPortId = "LocalPosition";
        public const string EulerInputPortId = "LocalEulerAngles";
        public const string ScaleInputPortId = "LocalScale";
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
            bool groundProjection = RequireBoolean(operation, literals, "NeedRaycastGround");
            if ((operation.Integer1 == (int)PresentationVfxCommand.Play ||
                 operation.Integer1 == (int)PresentationVfxCommand.MaintainStateModifier) &&
                (target != (int)PresentationVfxTarget.Owner || groundProjection))
                throw Invalid(operation, "currently supports Owner placement and accepted Combat world poses; target binding and ground projection are not implemented");
            RequireBoolean(operation, literals, "UseAttachPointRotation");
            RequireFieldCount(operation, literals, 10);
        }

        static InvalidOperationException Invalid(SemanticOperation operation, string detail) =>
            new InvalidOperationException(
                $"Vfx operation '{operation.TemplateIdentity}' ({operation.Code}) {detail}.");
    }

    internal static class PresentationProgramPayloadSchema
    {
        internal static void RequireFieldCount(SemanticOperation operation,
            IReadOnlyList<SemanticLiteral> literals, int expected)
        {
            string prefix = operation.TemplateIdentity + "/constant/";
            int count = 0;
            for (int i = 0; i < operation.LiteralReferences.Count; i++)
                if (literals[operation.LiteralReferences[i]].Identity.StartsWith(prefix, StringComparison.Ordinal))
                    count++;
            if (count != expected)
                throw Invalid(operation, $"requires exactly {expected} payload fields but contains {count}");
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
            if (requireValue && string.IsNullOrWhiteSpace(literal.String))
                throw Invalid(operation, $"field '{field}' is required");
            if (!string.Equals(literal.String, literal.String.Trim(), StringComparison.Ordinal))
                throw Invalid(operation, $"field '{field}' must not contain leading or trailing whitespace");
            return literal.String;
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
            return literal.Number.Value;
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

    public readonly partial struct ProgramEnumConstant
    {
        public ProgramEnumConstant(string typeIdentity, int value)
        {
            TypeIdentity = typeIdentity ?? string.Empty;
            Value = value;
        }

        public string TypeIdentity { get; }
        public int Value { get; }
    }

    public sealed partial class ProgramOperationLayout
    {
        readonly ReadOnlyCollection<int> m_Operands;
        readonly ReadOnlyCollection<int> m_StateSlots;

        public ProgramOperationLayout(OperationHandle handle, int definitionIndex, int parameterIndex,
            IEnumerable<int> operands, IEnumerable<int> stateSlots)
        {
            Handle = handle;
            DefinitionIndex = definitionIndex;
            ParameterIndex = parameterIndex;
            m_Operands = new List<int>(operands ?? Array.Empty<int>()).AsReadOnly();
            m_StateSlots = new List<int>(stateSlots ?? Array.Empty<int>()).AsReadOnly();
            RequireRecord();
        }

        public OperationHandle Handle { get; }
        public int DefinitionIndex { get; }
        public int ParameterIndex { get; }
        public IReadOnlyList<int> Operands => m_Operands;
        public IReadOnlyList<int> StateSlots => m_StateSlots;

        [ValueValidation]
        void RequireRecord()
        {
            if (!Handle.IsValid)
                throw new ArgumentException("Operation handle is invalid.");
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

    public sealed partial class ProgramControlFlowEdge
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
            Identity = identity.Trim();
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
            RequireRecord();
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

        [ValueValidation]
        void RequireRecord()
        {
            SimulationIdentity.Require(Identity, nameof(Identity));
            if (!Source.IsValid || !Target.IsValid || HasCondition && !Condition.IsValid)
                throw new ArgumentException("Control-flow edge contains an invalid operation handle.");
        }
    }

    public sealed partial class ProgramConstantInputBinding
    {
        public ProgramConstantInputBinding(
            OperationHandle targetOperation,
            string targetPort,
            int constantIndex,
            SemanticValueKind resolvedValueKind)
        {
            TargetOperation = targetOperation;
            TargetPort = targetPort.Trim();
            ConstantIndex = constantIndex;
            ResolvedValueKind = resolvedValueKind;
            RequireRecord();
        }

        public OperationHandle TargetOperation { get; }
        public string TargetPort { get; }
        public int ConstantIndex { get; }
        public SemanticValueKind ResolvedValueKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!TargetOperation.IsValid)
                throw new ArgumentException("Target operation is invalid.");
            if (ConstantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(ConstantIndex));
            SimulationIdentity.Require(TargetPort, nameof(TargetPort));
        }
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

    public sealed partial class ProgramReference
    {
        public ProgramReference(string identity, OperationHandle sourceOperation, ProgramReferenceKind kind, int targetIndex, string externalIdentity)
        {
            Identity = identity.Trim();
            SourceOperation = sourceOperation;
            Kind = kind;
            TargetIndex = targetIndex;
            ExternalIdentity = externalIdentity ?? string.Empty;
            RequireRecord();
        }
        public string Identity { get; }
        public OperationHandle SourceOperation { get; }
        public bool HasSourceOperation => SourceOperation.IsValid;
        public ProgramReferenceKind Kind { get; }
        public int TargetIndex { get; }
        public string ExternalIdentity { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (TargetIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(TargetIndex));
            SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    public enum ProgramGraphParameterDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public sealed partial class ProgramGraphParameterBinding
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
                !Enum.IsDefined(typeof(SemanticValueKind), valueKind))
                throw new ArgumentException("Program graph parameter binding is incomplete.");
            Direction = direction;
            ParameterName = parameterName.Trim();
            DeclarationIdentity = declarationIdentity.Trim();
            StateSlot = stateSlot;
            PortId = portId.Trim();
            ValueKind = valueKind;
            RequireRecord();
        }

        public ProgramGraphParameterDirection Direction { get; }
        public string ParameterName { get; }
        public string DeclarationIdentity { get; }
        public int StateSlot { get; }
        public string PortId { get; }
        public SemanticValueKind ValueKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (StateSlot < 0)
                throw new ArgumentException("Program graph parameter binding is incomplete.");
            SimulationIdentity.Require(ParameterName, nameof(ParameterName));
            SimulationIdentity.Require(DeclarationIdentity, nameof(DeclarationIdentity));
            SimulationIdentity.Require(PortId, nameof(PortId));
        }
    }

    [ValueStorage]
    public sealed partial class ProgramGraphCallFrame
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
            Index = index;
            Identity = identity.Trim();
            OwnerOperation = ownerOperation;
            EntryOperation = entryOperation;
            ChildGraphIdentity = childGraphIdentity.Trim();
            m_Inputs = Freeze(inputs);
            m_Outputs = Freeze(outputs);
            RequireRecord();
        }

        public int Index { get; }
        public string Identity { get; }
        public OperationHandle OwnerOperation { get; }
        public OperationHandle EntryOperation { get; }
        public string ChildGraphIdentity { get; }
        public IReadOnlyList<ProgramGraphParameterBinding> Inputs => m_Inputs;
        public IReadOnlyList<ProgramGraphParameterBinding> Outputs => m_Outputs;

        static ReadOnlyCollection<ProgramGraphParameterBinding> Freeze(
            IEnumerable<ProgramGraphParameterBinding> source)
        {
            var values = new List<ProgramGraphParameterBinding>(source ?? Array.Empty<ProgramGraphParameterBinding>());
            values.Sort((left, right) =>
            {
                int byParameter = string.CompareOrdinal(left.ParameterName, right.ParameterName);
                return byParameter != 0 ? byParameter : string.CompareOrdinal(left.PortId, right.PortId);
            });
            return values.AsReadOnly();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || !OwnerOperation.IsValid || !EntryOperation.IsValid)
                throw new ArgumentException("Program graph call frame identity is incomplete.");
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(ChildGraphIdentity, nameof(ChildGraphIdentity));
            RequireBindings(m_Inputs, ProgramGraphParameterDirection.Input, "input");
            RequireBindings(m_Outputs, ProgramGraphParameterDirection.Output, "output");
        }

        static void RequireBindings(IReadOnlyList<ProgramGraphParameterBinding> values,
            ProgramGraphParameterDirection direction, string label)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ports = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                ProgramGraphParameterBinding value = values[i];
                if (value.Direction != direction || !names.Add(value.ParameterName) || !ports.Add(value.PortId))
                    throw new ArgumentException($"Program graph call frame {label} bindings are duplicated or use the wrong direction.");
            }
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

    [ValueUnionTemplate, ValueOperations]
    public readonly struct ProgramStateValue<TScalar, TVector2, TVector3, TYaw, TQuaternion, TEnum, TActionTargetSnapshot>
    {
        [ValueUnionCase] public readonly bool Boolean;
        [ValueUnionCase] public readonly int Int32;
        [ValueUnionCase] public readonly ulong UInt64;
        [ValueUnionCase] public readonly TScalar Scalar;
        [ValueUnionCase] public readonly TVector2 Vector2;
        [ValueUnionCase] public readonly TVector3 Vector3;
        [ValueUnionCase] public readonly TYaw Yaw;
        [ValueUnionCase(NormalizeNull = true)] public readonly CanonicalTextValue Identity;
        [ValueUnionCase] public readonly BlackboardOwnerToken BlackboardOwnerToken;
        [ValueUnionCase] public readonly BlackboardWriteStamp BlackboardWriteStamp;
        [ValueUnionCase(DefaultMember = "Identity")] public readonly TQuaternion Quaternion;
        [ValueUnionCase] public readonly TEnum Enum;
        [ValueUnionCase(DefaultMember = "None")] public readonly TActionTargetSnapshot ActionTargetSnapshot;
    }

    public readonly partial struct ProgramEnumState : IEquatable<ProgramEnumState>
    {
        public ProgramEnumState(CanonicalTextValue typeIdentity, int value)
        {
            TypeIdentity = typeIdentity;
            Value = value;
        }

        public CanonicalTextValue TypeIdentity { get; }
        public int Value { get; }
        public bool Equals(ProgramEnumState other) => TypeIdentity.Equals(other.TypeIdentity) && Value == other.Value;
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

    public sealed partial class ProgramStateSlot
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
            Index = index;
            Identity = identity.Trim();
            ValueKind = valueKind;
            OwnerKind = ownerKind;
            Semantic = semantic;
            OwnerIdentity = ownerIdentity.Trim();
            DefaultConstantIndex = defaultConstantIndex;
            StateCodecIdentity = stateCodecIdentity == null
                ? ProgramStateSchema.CodecIdentity(valueKind)
                : stateCodecIdentity.Trim();
            RequireRecord();
        }
        public int Index { get; }
        public string Identity { get; }
        public ProgramStateValueKind ValueKind { get; }
        public ProgramStateOwnerKind OwnerKind { get; }
        public ProgramStateSemantic Semantic { get; }
        public string OwnerIdentity { get; }
        public int DefaultConstantIndex { get; }
        public string StateCodecIdentity { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || DefaultConstantIndex < -1)
                throw new ArgumentOutOfRangeException();
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(OwnerIdentity, nameof(OwnerIdentity));
            SimulationIdentity.Require(StateCodecIdentity, nameof(StateCodecIdentity));
            ProgramStateSchema.RequireSlot(ValueKind, OwnerKind, Semantic);
        }
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

    public readonly partial struct BlackboardOwnerToken : IEquatable<BlackboardOwnerToken>
    {
        public BlackboardOwnerToken(ProgramScopeKind scopeKind, int compiledOwnerIndex, ulong generation)
        {
            ScopeKind = scopeKind;
            CompiledOwnerIndex = compiledOwnerIndex;
            Generation = generation;
            RequirePopulated();
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

        [ValueValidation]
        void RequireCanonical()
        {
            if (!Equals(default))
                RequirePopulated();
        }

        void RequirePopulated()
        {
            if (!IsDefined(ScopeKind) || CompiledOwnerIndex < 0 || Generation == 0)
                throw new ArgumentException("Blackboard owner token is incomplete.");
        }

        static bool IsDefined(ProgramScopeKind kind) =>
            kind >= ProgramScopeKind.Character && kind <= ProgramScopeKind.Frame;
    }

    public readonly partial struct BlackboardWriteStamp : IEquatable<BlackboardWriteStamp>
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
            SourceOperation = sourceOperation;
            LogicTick = logicTick;
            SourceContext = sourceContext;
            TimelineOperation = timelineOperation;
            ClipOperation = clipOperation;
            Cycle = cycle;
            CommandSequence = commandSequence;
            RequirePopulated();
        }

        [ValueValidation]
        void RequireCanonical()
        {
            if (!Equals(default))
                RequirePopulated();
        }

        void RequirePopulated()
        {
            if (SourceOperation.IsValid == (CommandSequence != 0) || LogicTick == 0 ||
                TimelineOperation.IsValid != ClipOperation.IsValid || Cycle < 0 ||
                CommandSequence != 0 && (TimelineOperation.IsValid || Cycle != 0))
            {
                throw new ArgumentException("Blackboard write stamp is incomplete.");
            }
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

    [ValueStorage]
    public sealed partial class ProgramScopeLayout
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
            CompiledOwnerIndex = compiledOwnerIndex;
            Identity = identity.Trim();
            Kind = kind;
            OwnerIdentity = ownerIdentity.Trim();
            OwnerOperation = ownerOperation;
            var values = stateSlots == null ? new List<int>() : new List<int>(stateSlots);
            values.Sort();
            m_StateSlots = values.AsReadOnly();
            RequireRecord();
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (CompiledOwnerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(CompiledOwnerIndex));
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(OwnerIdentity, nameof(OwnerIdentity));
            for (int i = 0; i < m_StateSlots.Count; i++)
            {
                if (m_StateSlots[i] < 0 || i > 0 && m_StateSlots[i - 1] >= m_StateSlots[i])
                    throw new ArgumentException("Program scope state slots must be non-negative, sorted and unique.");
            }
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

    public sealed partial class ProgramOutputChannelLayout
    {
        public ProgramOutputChannelLayout(int index, string identity, ProgramOutputChannelKind kind)
        {
            Index = index;
            Identity = identity.Trim();
            Kind = kind;
            RequireRecord();
        }

        public int Index { get; }
        public string Identity { get; }
        public ProgramOutputChannelKind Kind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            SimulationIdentity.Require(Identity, nameof(Identity));
        }
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

	public sealed partial class ProgramExecutionSource
	{
		public ProgramExecutionSource(
			ProgramSourceTargetKind targetKind,
			int targetIndex,
			string graphId,
			string nodeId,
			string portId,
			string edgeId,
			string declarationId,
			string timelineId,
			string trackId,
			string clipId,
			string graphInvocationPath = "",
			string compiledPortId = "",
			ProgramValuePortDirection valuePortDirection = ProgramValuePortDirection.None,
			string sourceInvocationPath = "",
			string parentInvocationPath = "",
			ProgramInvocationCallerKind invocationCallerKind = ProgramInvocationCallerKind.None,
			string invocationCallerId = "",
			string invocationCallerClipId = "")
		{
			TargetKind = targetKind;
			TargetIndex = targetIndex;
			GraphId = graphId ?? string.Empty;
			NodeId = nodeId ?? string.Empty;
			PortId = portId ?? string.Empty;
			EdgeId = edgeId ?? string.Empty;
			DeclarationId = declarationId ?? string.Empty;
			TimelineId = timelineId ?? string.Empty;
			TrackId = trackId ?? string.Empty;
			ClipId = clipId ?? string.Empty;
			GraphInvocationPath = graphInvocationPath ?? string.Empty;
			CompiledPortId = compiledPortId ?? string.Empty;
			ValuePortDirection = valuePortDirection;
			SourceInvocationPath = sourceInvocationPath ?? string.Empty;
			ParentInvocationPath = parentInvocationPath ?? string.Empty;
			InvocationCallerKind = invocationCallerKind;
			InvocationCallerId = invocationCallerId ?? string.Empty;
			InvocationCallerClipId = invocationCallerClipId ?? string.Empty;
			RequireRecord();
		}

        [ValueValidation]
        void RequireRecord()
        {
            if (TargetIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(TargetIndex));
			if (GraphId.Length == 0 && TimelineId.Length == 0 && TargetKind != ProgramSourceTargetKind.BodyMotion)
				throw new ArgumentException("Source map entry requires a graph or timeline identity.");
        }

		public ProgramSourceTargetKind TargetKind { get; }
		public int TargetIndex { get; }
		public string GraphId { get; }
		public string NodeId { get; }
		public string PortId { get; }
		public string EdgeId { get; }
		public string DeclarationId { get; }
		public string TimelineId { get; }
		public string TrackId { get; }
		public string ClipId { get; }
		public string GraphInvocationPath { get; }
		public string CompiledPortId { get; }
		public ProgramValuePortDirection ValuePortDirection { get; }
		public string SourceInvocationPath { get; }
		public string ParentInvocationPath { get; }
		public ProgramInvocationCallerKind InvocationCallerKind { get; }
		public string InvocationCallerId { get; }
		public string InvocationCallerClipId { get; }
	}

    public sealed class ProgramExecutionSourceLayout
    {
        GraphExecutionChunk m_Chunk;

        public ProgramExecutionSourceLayout(IReadOnlyList<ProgramExecutionSource> entries, IGraphContentPublisher publisher)
        {
            Entries = entries;
            Publisher = publisher;
        }

        public IReadOnlyList<ProgramExecutionSource> Entries { get; }
        public IGraphContentPublisher Publisher { get; }
        public GraphExecutionChunk Chunk => m_Chunk ??= Publisher.Publish(this);
        public StableHash Hash => Chunk.Hash;

    }

    public sealed partial class ProgramSourceMapText
    {
        public ProgramSourceMapText(string sourceType, IReadOnlyList<string> displayPathSegments)
        {
            SourceType = sourceType;
            DisplayPathSegments = displayPathSegments;
        }

        public string SourceType { get; }
        public IReadOnlyList<string> DisplayPathSegments { get; }
    }

    [ValueStorage]
    public sealed partial class ProgramSourceMapEntry
    {
        public ProgramSourceMapEntry(ProgramExecutionSource source, string sourceType, string displayPath)
            : this(source, new ProgramSourceMapText(sourceType, displayPath.Split('/')))
        {
        }

        internal ProgramSourceMapEntry(ProgramExecutionSource source, ProgramSourceMapText text)
        {
            Source = source;
            Text = text;
            DisplayPath = string.Join("/", text.DisplayPathSegments);
        }

        public ProgramExecutionSource Source { get; }
        public ProgramSourceMapText Text { get; }
        public string SourceType => Text.SourceType;
        public string DisplayPath { get; }
    }

    public sealed class ProgramGraphSources
    {
        GraphExecutionChunk m_DiagnosticChunk;

        public ProgramGraphSources(ProgramExecutionSourceLayout layout, IReadOnlyList<ProgramSourceMapEntry> diagnostics,
            ProgramGraphSourceRevisionTable revisions)
        {
            Layout = layout;
            Diagnostics = diagnostics;
            Revisions = revisions;
        }

        public ProgramExecutionSourceLayout Layout { get; }
        public IReadOnlyList<ProgramSourceMapEntry> Diagnostics { get; }
        public ProgramGraphSourceRevisionTable Revisions { get; }
        public GraphExecutionChunk DiagnosticChunk => m_DiagnosticChunk ??= Layout.Publisher.Publish(this);

        public ProgramGraphSources WithRevisions(IEnumerable<ProgramGraphSourceRevision> revisions)
        {
            var sources = new ProgramGraphSources(Layout, Diagnostics, new ProgramGraphSourceRevisionTable(revisions));
            sources.m_DiagnosticChunk = DiagnosticChunk;
            return sources;
        }

        public static ProgramGraphSources Create(IEnumerable<ProgramSourceMapEntry> diagnostics,
            IEnumerable<ProgramGraphSourceRevision> revisions, IGraphContentPublisher publisher)
        {
            var entries = new List<ProgramSourceMapEntry>(diagnostics);
            entries.Sort(Compare);
            var sources = new ProgramExecutionSource[entries.Count];
            for (int i = 0; i < sources.Length; i++)
                sources[i] = entries[i].Source;
            return new ProgramGraphSources(new ProgramExecutionSourceLayout(Array.AsReadOnly(sources), publisher),
                entries.AsReadOnly(), new ProgramGraphSourceRevisionTable(revisions));
        }

        static int Compare(ProgramSourceMapEntry left, ProgramSourceMapEntry right)
        {
            ProgramExecutionSource a = left.Source;
            ProgramExecutionSource b = right.Source;
            int result = a.TargetKind.CompareTo(b.TargetKind);
            if (result != 0) return result;
            result = a.TargetIndex.CompareTo(b.TargetIndex);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.GraphId, b.GraphId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.GraphInvocationPath, b.GraphInvocationPath);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.NodeId, b.NodeId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.PortId, b.PortId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.CompiledPortId, b.CompiledPortId);
            if (result != 0) return result;
            return a.ValuePortDirection.CompareTo(b.ValuePortDirection);
        }
    }

    public readonly partial struct ProgramGraphSourceRevision
    {
        public ProgramGraphSourceRevision(string graphId, string revision)
        {
            GraphId = graphId;
            Revision = revision;
        }

        public string GraphId { get; }
        public string Revision { get; }
    }

    public sealed partial class ProgramGraphSourceRevisions
    {
        public ProgramGraphSourceRevisions(IReadOnlyList<ProgramGraphSourceRevision> sources) => Sources = sources;
        public IReadOnlyList<ProgramGraphSourceRevision> Sources { get; }
    }

    public sealed class ProgramGraphSourceRevisionTable
    {
        readonly Dictionary<string, string> m_Revisions = new(StringComparer.Ordinal);

        public ProgramGraphSourceRevisionTable(IEnumerable<ProgramGraphSourceRevision> sources)
        {
            var values = new List<ProgramGraphSourceRevision>(sources);
            values.Sort((left, right) => string.CompareOrdinal(left.GraphId, right.GraphId));
            foreach (ProgramGraphSourceRevision value in values)
                m_Revisions.Add(value.GraphId, value.Revision);
            Data = new ProgramGraphSourceRevisions(values.AsReadOnly());
        }

        internal ProgramGraphSourceRevisions Data { get; }
        public IReadOnlyList<ProgramGraphSourceRevision> Sources => Data.Sources;
        public string Require(string graphId) => m_Revisions[graphId];
    }

    public static class ProgramExecutionSourceCoverage
    {
        public static void Require(
            IReadOnlyList<ProgramExecutionSource> entries,
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
            var invocations = new Dictionary<string, ProgramExecutionSource>(StringComparer.Ordinal);
            var invocationSources = new Dictionary<string, List<ProgramExecutionSource>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                ProgramExecutionSource entry = entries[i] ??
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
                        if (!invocationSources.TryGetValue(entry.SourceInvocationPath, out List<ProgramExecutionSource> sourceInvocations))
                        {
                            sourceInvocations = new List<ProgramExecutionSource>();
                            invocationSources.Add(entry.SourceInvocationPath, sourceInvocations);
                        }
                        sourceInvocations.Add(entry);
                        break;
                    case ProgramSourceTargetKind.OptimizedAway:
                        if (string.IsNullOrEmpty(entry.GraphId) ||
                            string.IsNullOrEmpty(entry.NodeId))
                            throw new InvalidDataException("Program optimized source is incomplete.");
                        break;
                    case ProgramSourceTargetKind.StateSlot:
                        RequireTarget(entry.TargetIndex, stateSlots, "state slot");
                        break;
                }
            }

            foreach (KeyValuePair<string, List<ProgramExecutionSource>> source in invocationSources)
            {
                string parent = source.Value[0].ParentInvocationPath;
                for (int i = 1; i < source.Value.Count; i++)
                    if (!string.Equals(parent, source.Value[i].ParentInvocationPath, StringComparison.Ordinal))
                        throw new InvalidDataException("Program graph invocation hierarchy is invalid.");
                var visited = new HashSet<string>(StringComparer.Ordinal) { source.Key };
                while (!string.IsNullOrEmpty(parent))
                {
                    if (!visited.Add(parent) || !invocationSources.TryGetValue(parent, out List<ProgramExecutionSource> owners))
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

    public sealed partial class ProgramProducer
    {
        public ProgramProducer(
            int index,
            string identity,
            AnimationChannelId animationChannelId,
            string sourceIdentity,
            ProgramOutputChannelKind channelKind)
        {
            Index = index;
            Identity = identity.Trim();
            AnimationChannelId = animationChannelId;
            SourceIdentity = sourceIdentity.Trim();
            ChannelKind = channelKind;
            RequireRecord();
        }
        public int Index { get; }
        public string Identity { get; }
        public AnimationChannelId AnimationChannelId { get; }
        public string SourceIdentity { get; }
        public ProgramOutputChannelKind ChannelKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            if (!AnimationChannelId.IsValid)
                throw new ArgumentException("Program Producer Animation Channel identity is invalid.");
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(SourceIdentity, nameof(SourceIdentity));
        }
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
        AbilityMovementExitRule = 36,
        CombatAttack = 37,
        GameplayConfigurationField = 38
    }

    public enum ProgramCatalogFieldKind : byte
    {
        Constant = 1,
        Identity = 2
    }

    public sealed partial class ProgramCatalogField
    {
        public ProgramCatalogField(string name, ProgramCatalogFieldKind kind, int constantIndex, string identity)
        {
            Name = name.Trim();
            Kind = kind;
            ConstantIndex = constantIndex;
            Identity = kind == ProgramCatalogFieldKind.Identity
                ? identity.Trim()
                : string.Empty;
            RequireRecord();
        }
        public string Name { get; }
        public ProgramCatalogFieldKind Kind { get; }
        public int ConstantIndex { get; }
        public string Identity { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SimulationIdentity.Require(Name, nameof(Name));
            if (Kind == ProgramCatalogFieldKind.Constant && ConstantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(ConstantIndex));
            if (Kind == ProgramCatalogFieldKind.Identity)
                SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    [ValueStorage]
    public sealed partial class ProgramCatalogEntry
    {
        readonly ReadOnlyCollection<ProgramCatalogField> m_Fields;

        public ProgramCatalogEntry(int index, ProgramCatalogEntryKind kind, string identity, int revision, IEnumerable<ProgramCatalogField> fields)
        {
            Index = index;
            Kind = kind;
            Identity = identity.Trim();
            Revision = revision;
            var copied = fields == null ? new List<ProgramCatalogField>() : new List<ProgramCatalogField>(fields);
            copied.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            m_Fields = copied.AsReadOnly();
            RequireRecord();
        }
        public int Index { get; }
        public ProgramCatalogEntryKind Kind { get; }
        public string Identity { get; }
        public int Revision { get; }
        public IReadOnlyList<ProgramCatalogField> Fields => m_Fields;

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0 || Revision < 0)
                throw new ArgumentOutOfRangeException();
            SimulationIdentity.Require(Identity, nameof(Identity));
            for (int i = 1; i < m_Fields.Count; i++)
                if (string.CompareOrdinal(m_Fields[i - 1].Name, m_Fields[i].Name) >= 0)
                    throw new ArgumentException($"Catalog entry '{Identity}' fields must be sorted and unique.");
        }
    }

    [ValueStorage]
    public sealed partial class GameplayAbilityCapabilityManifest
    {
        readonly ReadOnlyCollection<string> m_GameplayCapabilities;

        public GameplayAbilityCapabilityManifest(IEnumerable<string> gameplayCapabilities)
        {
            var values = gameplayCapabilities == null ? new List<string>() : new List<string>(gameplayCapabilities);
            for (int i = 0; i < values.Count; i++)
                values[i] = SimulationIdentity.Require(values[i], nameof(gameplayCapabilities));
            values.Sort(StringComparer.Ordinal);
            m_GameplayCapabilities = values.AsReadOnly();
            RequireRecord();
        }

        [ValueValidation]
        void RequireRecord()
        {
            for (int i = 0; i < m_GameplayCapabilities.Count; i++)
            {
                SimulationIdentity.Require(m_GameplayCapabilities[i], nameof(GameplayCapabilities));
                if (i > 0 && string.CompareOrdinal(m_GameplayCapabilities[i - 1], m_GameplayCapabilities[i]) >= 0)
                    throw new ArgumentException("Gameplay capabilities must be sorted and unique.");
            }
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

    public sealed partial class GameplayAbilityExecutionManifest
    {
        public GameplayAbilityExecutionManifest(CharacterSkillId abilityId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate, ProgramRevision sourceRevision,
            SemanticHash semanticHash, SimulationNumericProfile numericProfile,
            GameplayAbilityCapabilityManifest capabilities, GameplayAbilityRootDescriptor root,
            string executionIdentity, StableHash stateSchemaHash, StableHash contentHash)
        {
            AbilityId = abilityId;
            CompilerVersion = compilerVersion;
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            SourceRevision = sourceRevision;
            SemanticHash = semanticHash;
            NumericProfile = numericProfile;
            Capabilities = capabilities;
            Root = root;
            ExecutionIdentity = executionIdentity;
            StateSchemaHash = stateSchemaHash;
            ContentHash = contentHash;
            RequireRecord();
        }

        public CharacterSkillId AbilityId { get; }
        public string CompilerVersion { get; private set; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public ProgramRevision SourceRevision { get; }
        public SemanticHash SemanticHash { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public GameplayAbilityCapabilityManifest Capabilities { get; }
        public GameplayAbilityRootDescriptor Root { get; }
        public string ExecutionIdentity { get; private set; }
        public StableHash StateSchemaHash { get; }
        public StableHash ContentHash { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!AbilityId.IsValid)
                throw new ArgumentException("Ability identity is invalid.");
            if (!Root.IsAbility)
                throw new InvalidOperationException("Ability execution data requires an Ability root.");
            CompilerVersion = SimulationIdentity.Require(CompilerVersion, nameof(CompilerVersion));
            ExecutionIdentity = SimulationIdentity.Require(ExecutionIdentity, nameof(ExecutionIdentity));
            if (Capabilities == null)
                throw new ArgumentNullException(nameof(Capabilities));
            if (!StateSchemaHash.IsValid)
                throw new ArgumentException("Ability state schema hash is invalid.");
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

    public readonly partial struct PresentationCameraTargetTiming
    {
        public PresentationCameraTargetTiming(float delaySeconds, float durationSeconds, bool ignoreTimeScale)
        {
            DelaySeconds = delaySeconds;
            DurationSeconds = durationSeconds;
            IgnoreTimeScale = ignoreTimeScale;
            RequireRecord();
        }

        public float DelaySeconds { get; }
        public float DurationSeconds { get; }
        public bool IgnoreTimeScale { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!float.IsFinite(DelaySeconds) || DelaySeconds < 0f)
                throw new ArgumentOutOfRangeException("DelaySeconds");
            if (!float.IsFinite(DurationSeconds) || DurationSeconds < 0f)
                throw new ArgumentOutOfRangeException("DurationSeconds");
        }
    }

    [ValueStorage(UseConstructor = true), ValueOperations]
    public readonly partial struct PresentationCameraRequest
    {
        PresentationCameraRequest(
            PresentationCameraRequestKind kind,
            PresentationCameraRequestLifecycle lifecycle,
            CanonicalTextValue requestId,
            CanonicalTextValue sequenceId,
            CanonicalTextValue resourceId,
            CanonicalTextValue targetKey,
            CanonicalTextValue anchorKey,
            CanonicalTextValue aimPointKey,
            CanonicalTextValue preferredBoneKey,
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
            PresentationCameraTargetTiming aimTiming = default,
            CanonicalTextValue resourceRevision = default,
            float sourceStartTime = 0f,
            float sourceEndTime = 0f)
        {
            Kind = kind;
            Lifecycle = lifecycle;
            RequestId = Normalize(requestId);
            SequenceId = Normalize(sequenceId);
            ResourceId = Normalize(resourceId);
            ResourceRevision = Normalize(resourceRevision);
            SourceStartTime = sourceStartTime;
            SourceEndTime = sourceEndTime;
            TargetKey = targetKey.IsNull ? string.Empty : targetKey.Trim();
            AnchorKey = Normalize(anchorKey);
            AimPointKey = Normalize(aimPointKey);
            PreferredBoneKey = Normalize(preferredBoneKey);
            Mode = mode;
            EffectKind = effectKind;
            InterruptPolicy = interruptPolicy;
            Priority = priority;
            Weight = weight;
            BlendInSeconds = blendInSeconds;
            BlendOutSeconds = blendOutSeconds;
            ManualOrbitWeight = manualOrbitWeight;
            PitchWeight = pitchWeight;
            YawWeight = yawWeight;
            CaptureLockStartOnSubmit = captureLockStartOnSubmit;
            FollowTiming = followTiming;
            AimTiming = aimTiming;
            RequireRecord();
        }

        public PresentationCameraRequestKind Kind { get; }
        public PresentationCameraRequestLifecycle Lifecycle { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue RequestId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue SequenceId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue ResourceId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue ResourceRevision { get; }
        public float SourceStartTime { get; }
        public float SourceEndTime { get; }
        [ValueTextRules(NullToEmpty = true, Trim = true)]
        public CanonicalTextValue TargetKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue AnchorKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue AimPointKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue PreferredBoneKey { get; }
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
        [ValuePresence]
        public bool IsValid => IsDefined(Kind) && IsDefined(Lifecycle);


        public PresentationCameraRequest WithActionTarget(CanonicalTextValue targetActorId) =>
            new PresentationCameraRequest(
                Kind, Lifecycle, RequestId, SequenceId, ResourceId,
                targetActorId, AnchorKey, AimPointKey, PreferredBoneKey, Mode, EffectKind,
                InterruptPolicy, Priority, Weight, BlendInSeconds, BlendOutSeconds,
                ManualOrbitWeight, PitchWeight, YawWeight, CaptureLockStartOnSubmit,
                FollowTiming, AimTiming, ResourceRevision, SourceStartTime, SourceEndTime);

        public static PresentationCameraRequest Sequence(
            PresentationCameraRequestLifecycle lifecycle,
            CanonicalTextValue sequenceId,
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
            CanonicalTextValue requestId,
            int effectKind,
            CanonicalTextValue resourceId,
            int priority,
            float weight,
            bool captureLockStartOnSubmit,
            CanonicalTextValue resourceRevision = default,
            float sourceStartTime = 0f,
            float sourceEndTime = 0f) =>
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
                captureLockStartOnSubmit,
                resourceRevision: resourceRevision, sourceStartTime: sourceStartTime, sourceEndTime: sourceEndTime);

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
            CanonicalTextValue anchorKey,
            CanonicalTextValue aimPointKey,
            CanonicalTextValue preferredBoneKey,
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

        static CanonicalTextValue Normalize(CanonicalTextValue value)
        {
            if (value.IsEmpty)
                return CanonicalTextValue.Empty;
            if (!value.Equals(value.Trim()))
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

        [ValueValidation]
        void RequireRecord()
        {
            if (!IsDefined(Kind))
                throw new ArgumentOutOfRangeException("Kind");
            if (!IsDefined(Lifecycle))
                throw new ArgumentOutOfRangeException("Lifecycle");
            RequireFiniteNonNegative(Weight, nameof(Weight));
            RequireFiniteNonNegative(BlendInSeconds, nameof(BlendInSeconds));
            RequireFiniteNonNegative(BlendOutSeconds, nameof(BlendOutSeconds));
            RequireUnit(ManualOrbitWeight, nameof(ManualOrbitWeight));
            RequireUnit(PitchWeight, nameof(PitchWeight));
            RequireUnit(YawWeight, nameof(YawWeight));
            Normalize(RequestId);
            Normalize(SequenceId);
            Normalize(ResourceId);
            Normalize(AnchorKey);
            Normalize(AimPointKey);
            Normalize(PreferredBoneKey);
        }
    }
}
