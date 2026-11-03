using BTSMTL.Authoring;

namespace ThirdPersonSimulation
{
    public enum PresentationVfxCommand : byte
    {
        [BtsmtlBusinessKeyKind("Vfx", "vfx-request")] Play = 1,
        [BtsmtlBusinessKeyKind("Vfx", "vfx-request")] Stop = 2,
        [BtsmtlBusinessKeyKind("Vfx", "vfx-request")] Destroy = 3,
        [BtsmtlBusinessKeyKind("Vfx", "vfx-state-modifier")] MaintainStateModifier = 4
    }

    public enum PresentationVfxTarget : byte
    {
        Owner = 1,
        ActionTarget = 2,
        Camera = 14
    }

    public enum PresentationVfxCoordinatesOption : byte
    {
        NotConfigured = 0,
        ReferTarget = 7
    }

    [ValueStorage(UseConstructor = true, RecordedState = true)]
    public readonly partial struct PresentationVfxRequest
    {
        public PresentationVfxRequest(
            PresentationVfxCommand command,
            [BtsmtlBusinessKey("Vfx", BtsmtlBusinessKeyRole.Reference, KindParameter = "command")] string requestId,
            [BtsmtlBusinessKey("Vfx", "vfx-resource", BtsmtlBusinessKeyRole.Reference)] string resourceId,
            PresentationVfxTarget target,
            [BtsmtlBusinessKey("Vfx", "vfx-anchor", BtsmtlBusinessKeyRole.Reference)] string anchorKey,
            bool hasPoseOverride,
            PresentationVfxCoordinatesOption coordinatesOption,
            float localPositionX, float localPositionY, float localPositionZ,
            float localEulerAnglesX, float localEulerAnglesY, float localEulerAnglesZ,
            float localScaleX, float localScaleY, float localScaleZ,
            bool useRotationY, bool useRotationXZ, bool needRaycastGround, bool useAttachPointRotation)
        {
            Command = command;
            RequestId = requestId;
            ResourceId = resourceId;
            Target = target;
            AnchorKey = anchorKey;
            HasPoseOverride = hasPoseOverride;
            CoordinatesOption = coordinatesOption;
            LocalPositionX = localPositionX;
            LocalPositionY = localPositionY;
            LocalPositionZ = localPositionZ;
            LocalEulerAnglesX = localEulerAnglesX;
            LocalEulerAnglesY = localEulerAnglesY;
            LocalEulerAnglesZ = localEulerAnglesZ;
            LocalScaleX = localScaleX;
            LocalScaleY = localScaleY;
            LocalScaleZ = localScaleZ;
            UseRotationY = useRotationY;
            UseRotationXZ = useRotationXZ;
            NeedRaycastGround = needRaycastGround;
            UseAttachPointRotation = useAttachPointRotation;
        }

        public PresentationVfxRequest WithPose(float positionX, float positionY, float positionZ,
            float eulerX, float eulerY, float eulerZ, float scaleX, float scaleY, float scaleZ) =>
            new PresentationVfxRequest(Command, RequestId, ResourceId, Target, AnchorKey,
                HasPoseOverride, CoordinatesOption, positionX, positionY, positionZ,
                eulerX, eulerY, eulerZ, scaleX, scaleY, scaleZ,
                UseRotationY, UseRotationXZ, NeedRaycastGround, UseAttachPointRotation);

        public PresentationVfxCommand Command { get; }
        public string RequestId { get; }
        public string ResourceId { get; }
        public PresentationVfxTarget Target { get; }
        public string AnchorKey { get; }
        public bool HasPoseOverride { get; }
        public PresentationVfxCoordinatesOption CoordinatesOption { get; }
        public float LocalPositionX { get; }
        public float LocalPositionY { get; }
        public float LocalPositionZ { get; }
        public float LocalEulerAnglesX { get; }
        public float LocalEulerAnglesY { get; }
        public float LocalEulerAnglesZ { get; }
        public float LocalScaleX { get; }
        public float LocalScaleY { get; }
        public float LocalScaleZ { get; }
        public bool UseRotationY { get; }
        public bool UseRotationXZ { get; }
        public bool NeedRaycastGround { get; }
        public bool UseAttachPointRotation { get; }
        public bool IsValid => Command != 0;
    }
}
