using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

// 归档按帧范围查询的通用记录。项目只负责从自己的归档读出这些记录；项目专有身份写进 Fields。
public sealed record PreviewArchivePage<T>(PreviewPage<T> Page, PreviewObservationAvailability[] Availability);

public sealed record PreviewArchiveDigest(string JobId, string InputMode, int FrameCount, string RawJson);

public sealed record PreviewLogicRecord(PreviewFrameSource Source, PreviewActor[] Actors, PreviewField[] Fields);

public sealed record PreviewTransform(float[] Position, float[] Rotation, float[] Scale);
public sealed record PreviewRigLayout(string ActorId, int RosterIndex, string RigId, string RigRevision,
    string[] Bones, int[] Parents, string DiagnosticsUnavailableReason);
public sealed record PreviewPoseLayout(string Coordinates, string Units, PreviewRigLayout[] Actors);
public sealed record PreviewActorPose(string ActorId, int RosterIndex, string SourceSequence,
    string CompletionIdentity, PreviewTransform WorldRoot, PreviewTransform LocalPoseRoot, PreviewTransform[] LocalBones);
public sealed record PreviewPresentationRecord(PreviewFrameSource Source, string PresentationSequence,
    string VerifiedHostFrame, string PreviousHostFrame, double PresentationTick, double ElapsedSeconds,
    float InterpolationFactor, PreviewActorPose[] Actors, PreviewField[] Fields);

// Identity 与 PreviewFrameImage.CameraIdentity 对应同一台相机；ActorId 为相机跟随的对象，没有时为空。
public sealed record PreviewCamera(PreviewFrameSource Source, string PresentationSequence, string Identity,
    string? ActorId, int Width, int Height, double PresentationTick, float InterpolationFactor,
    float[] WorldPosition, float[] WorldRotation, float[] WorldToCamera, float[] Projection,
    PreviewField[] Fields, string RawJson);

public sealed record PreviewUnavailableRecord(PreviewFrameSource Source, string PresentationSequence,
    string Channel, string Reason, int ActorRosterIndex, int BoneIndex);

public sealed record PreviewFrameImage(string JobId, string ArchiveJobId, int ImageIndex, PreviewFrameSource Source,
    string SourceHostFrame, string VerifiedHostFrame, string PreviousHostFrame, string PresentationSequence,
    string CameraIdentity, int Width, int Height, bool FlipY, bool PresentationAvailable, double PresentationTick,
    double ElapsedSeconds, float InterpolationFactor, string MediaType, int EncodedLength, PreviewField[] Fields);

public sealed record PreviewDataCurve(string Id, string DisplayName, string Channel, string Clock, string ActorId,
    string TargetId, string Component, string Coordinates, string FrameCoordinates, string Unit, string Encoding);
public sealed record PreviewDataCurveSample(PreviewFrameSource Source, string? PresentationSequence,
    double? PresentationTick, double Value, string RawValue);
public sealed record PreviewDataCurveSamples(PreviewDataCurve Curve, PreviewPage<PreviewDataCurveSample> Page,
    PreviewObservationAvailability[] Availability);

// BTSMTL 编排值（OrchestrationValueKind）的观察形态；定点等数值以原始值文本给出，不在消费端换算。
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewEmptyValue), "none")]
[JsonDerivedType(typeof(PreviewBooleanValue), "boolean")]
[JsonDerivedType(typeof(PreviewInt32Value), "int32")]
[JsonDerivedType(typeof(PreviewUInt64Value), "uint64")]
[JsonDerivedType(typeof(PreviewObjectHandleValue), "objectHandle")]
[JsonDerivedType(typeof(PreviewScalarValue), "scalar")]
[JsonDerivedType(typeof(PreviewVector2Value), "vector2")]
[JsonDerivedType(typeof(PreviewVector3Value), "vector3")]
[JsonDerivedType(typeof(PreviewYawValue), "yaw")]
[JsonDerivedType(typeof(PreviewIdentityValue), "identity")]
[JsonDerivedType(typeof(PreviewQuaternionValue), "quaternion")]
[JsonDerivedType(typeof(PreviewEnumValue), "enum")]
[JsonDerivedType(typeof(PreviewSpatialIdentityValue), "spatialIdentity")]
public abstract record PreviewOrchestrationValue;
public sealed record PreviewEmptyValue : PreviewOrchestrationValue;
public sealed record PreviewBooleanValue(bool Value) : PreviewOrchestrationValue;
public sealed record PreviewInt32Value(int Value) : PreviewOrchestrationValue;
public sealed record PreviewUInt64Value(string Value) : PreviewOrchestrationValue;
public sealed record PreviewObjectHandleValue(string Value) : PreviewOrchestrationValue;
public sealed record PreviewScalarValue(string Raw) : PreviewOrchestrationValue;
public sealed record PreviewVector2Value(string[] Raw) : PreviewOrchestrationValue;
public sealed record PreviewVector3Value(string[] Raw) : PreviewOrchestrationValue;
public sealed record PreviewYawValue(string Raw) : PreviewOrchestrationValue;
public sealed record PreviewIdentityAddress(string Space, int Index);
public sealed record PreviewIdentityValue(PreviewIdentityAddress Value) : PreviewOrchestrationValue;
public sealed record PreviewQuaternionValue(string[] Raw) : PreviewOrchestrationValue;
public sealed record PreviewEnumValue(int TypeContentId, int Value) : PreviewOrchestrationValue;
public sealed record PreviewSpatialIdentityValue(PreviewIdentityAddress Identity, string[] PositionRaw, string YawRaw)
    : PreviewOrchestrationValue;

public sealed record PreviewInputValue(string InputId, string Kind, float[]? Value, PreviewOrchestrationValue? RuntimeValue);
public sealed record PreviewInputRequest(string RequestId, string Sequence, string SourceTick, string ExpireTick, int Priority);
public sealed record PreviewActorInput(string ActorId, int RosterIndex, bool Received, string InputSequence,
    string SourceTick, PreviewInputValue[] Values, PreviewInputRequest[] Requests);
public sealed record PreviewInputRecord(PreviewFrameSource Source, PreviewActorInput[] Actors)
{
    public double? InputTimeSeconds { get; init; }
}
public sealed record PreviewInputPlan(string ActorId, string Plan, string PlanRevision, int SchemeIndex);
public sealed record PreviewInputPage(PreviewPage<PreviewInputRecord> Page, PreviewInputPlan[] Plans,
    PreviewObservationAvailability[] Availability)
{
    public string? ValueEncoding { get; init; }
    public double? InputTimeOriginSeconds { get; init; }
    public double? InputTimeStepSeconds { get; init; }
    public string? InputTimeUnavailableReason { get; init; }
}
