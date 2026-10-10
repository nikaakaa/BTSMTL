using System;

namespace ThirdPersonSimulation
{
    public interface ICameraProgramConstantReader
    {
        int ReadInt32(OperationNamedConstant field);
        float ReadScalar(OperationNamedConstant field);
        string ReadString(OperationNamedConstant field, bool required);
    }

    public static class CameraProgramRequestFactory
    {
        public static PresentationCameraRequest Build<TConstants>(SimulationOperationCode code, int variant, uint flags,
            PresentationCameraRequestLifecycle lifecycle, TConstants constants)
            where TConstants : struct, ICameraProgramConstantReader
        {
            return code switch
            {
                SimulationOperationCode.CameraStateRequest => PresentationCameraRequest.Sequence(
                    lifecycle,
                    constants.ReadString(OperationNamedConstant.SequenceId, true),
                    variant,
                    checked((int)flags),
                    constants.ReadInt32(OperationNamedConstant.Priority),
                    constants.ReadScalar(OperationNamedConstant.Weight),
                    constants.ReadScalar(OperationNamedConstant.BlendInSeconds),
                    constants.ReadScalar(OperationNamedConstant.BlendOutSeconds),
                    constants.ReadString(OperationNamedConstant.TargetKey, false)),
                SimulationOperationCode.CameraEffectRequest => PresentationCameraRequest.Effect(
                    lifecycle,
                    constants.ReadString(OperationNamedConstant.RequestId, true),
                    variant,
                    constants.ReadString(OperationNamedConstant.ResourceId, true),
                    constants.ReadInt32(OperationNamedConstant.Priority),
                    constants.ReadScalar(OperationNamedConstant.Weight),
                    (flags & CameraProgramOperationSchema.PreserveLockStartOnSubmit) == 0),
                SimulationOperationCode.CameraResponse => PresentationCameraRequest.Response(
                    lifecycle,
                    variant,
                    constants.ReadScalar(OperationNamedConstant.ManualOrbitWeight),
                    constants.ReadScalar(OperationNamedConstant.PitchResponseWeight),
                    constants.ReadScalar(OperationNamedConstant.YawResponseWeight),
                    constants.ReadInt32(OperationNamedConstant.Priority),
                    constants.ReadScalar(OperationNamedConstant.Weight)),
                SimulationOperationCode.CameraTarget => PresentationCameraRequest.Target(
                    lifecycle,
                    constants.ReadString(OperationNamedConstant.TargetKey, false),
                    constants.ReadString(OperationNamedConstant.AnchorKey, false),
                    constants.ReadString(OperationNamedConstant.AimPointKey, false),
                    constants.ReadString(OperationNamedConstant.PreferredBoneKey, false),
                    constants.ReadInt32(OperationNamedConstant.Priority),
                    constants.ReadScalar(OperationNamedConstant.Weight),
                    new PresentationCameraTargetTiming(
                        constants.ReadScalar(OperationNamedConstant.FollowDelaySeconds),
                        constants.ReadScalar(OperationNamedConstant.FollowDurationSeconds),
                        (flags & CameraProgramOperationSchema.FollowIgnoreTimeScale) != 0),
                    new PresentationCameraTargetTiming(
                        constants.ReadScalar(OperationNamedConstant.AimDelaySeconds),
                        constants.ReadScalar(OperationNamedConstant.AimDurationSeconds),
                        (flags & CameraProgramOperationSchema.AimIgnoreTimeScale) != 0)),
                _ => throw new InvalidOperationException(
                    $"Operation code '{code}' is not a Camera request.")
            };
        }
    }

}
