using System;
using Photon.Deterministic;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public static class AbilityTimelineRuntimeLimits
    {
        public const int MaximumPlaybackCount = 1024;
        public const int MaximumCycleAdvance = 20;
    }

    [ValueOperations]
    public readonly partial struct AbilityTimelineTreeClipExit : IEquatable<AbilityTimelineTreeClipExit>
    {
        public AbilityTimelineTreeClipExit(CanonicalTextValue clipAuthoringId, int cycle)
        {
            ClipAuthoringId = clipAuthoringId;
            Cycle = cycle;
        }

        public CanonicalTextValue ClipAuthoringId { get; }
        public int Cycle { get; }
        public bool Equals(AbilityTimelineTreeClipExit other) =>
            Cycle == other.Cycle && CanonicalTextValue.Equals(ClipAuthoringId, other.ClipAuthoringId, StringComparison.Ordinal);
        public override bool Equals(object value) => value is AbilityTimelineTreeClipExit other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ClipAuthoringId, Cycle);
    }
    public enum TimelineClipTimePoint : byte
    {
        Start = 1,
        End = 2,
        CurveEnd = 3,
        EaseIn = 4,
        EaseOut = 5
    }

    public enum TimelineCurveChannel : byte
    {
        Weight = 1,
        EaseIn = 2,
        EaseOut = 3,
        PositionX = 4,
        PositionY = 5,
        PositionZ = 6,
        Yaw = 7
    }

    public enum TimelineClipScalarValue : byte
    {
        Intensity = 1
    }

    public enum TimelineTreeClipEdgeKind : byte
    {
        Root = 1,
        Enable = 2,
        Disable = 3,
        Destroy = 4
    }

    public enum TimelinePresentationOutputKind : byte
    {
        SelectProducer = 1,
        SampleProducer = 2,
        CompleteProducer = 3,
        ReleaseProducer = 4,
        Camera = 5,
        ForceProducer = 6
    }

    public enum TimelineTraceSeverity : byte
    {
        Detail = 1,
        Information = 2,
        Warning = 3,
        Error = 4
    }

    [ValueOperations]
    public readonly partial struct TimelineActionContextIdentity : IEquatable<TimelineActionContextIdentity>
    {
        public TimelineActionContextIdentity(
            string actionId,
            CanonicalTextValue contextId,
            ulong instanceId,
            ulong predictionKey,
            CanonicalTextValue targetActorId,
            CharacterSkillId skillId = default,
            OperationHandle skillEntryOperation = default,
            ulong skillExecutionGeneration = 0)
        {
            ActionId = actionId ?? string.Empty;
            ContextId = contextId.IsNull ? CanonicalTextValue.Empty : contextId;
            InstanceId = instanceId;
            PredictionKey = predictionKey;
            TargetActorId = targetActorId;
            SkillId = skillId;
            SkillEntryOperation = skillEntryOperation;
            SkillExecutionGeneration = skillExecutionGeneration;
            RequireValid();
        }

        [ValueValidation]
        void RequireValid()
        {
            if (SkillId.IsValid != SkillEntryOperation.IsValid || !SkillId.IsValid && SkillExecutionGeneration != 0)
                throw new ArgumentException("Timeline Action Context skill execution identity is incomplete.");
        }

        public string ActionId { get; }
        public CanonicalTextValue ContextId { get; }
        public ulong InstanceId { get; }
        public ulong PredictionKey { get; }
        public CanonicalTextValue TargetActorId { get; }
        public CharacterSkillId SkillId { get; }
        public OperationHandle SkillEntryOperation { get; }
        public ulong SkillExecutionGeneration { get; }
        public bool HasSkillExecution => SkillId.IsValid && SkillEntryOperation.IsValid;
        public bool IsValid =>
            !CanonicalTextValue.IsNullOrEmpty(ActionId) &&
            !ContextId.IsEmpty &&
            InstanceId != 0 &&
            PredictionKey != 0;

        public bool Equals(TimelineActionContextIdentity other) =>
            CanonicalTextValue.Equals(ActionId, other.ActionId, StringComparison.Ordinal) &&
            ContextId.Equals(other.ContextId) &&
            InstanceId == other.InstanceId &&
            PredictionKey == other.PredictionKey &&
            SkillId == other.SkillId &&
            SkillEntryOperation.Equals(other.SkillEntryOperation) &&
            SkillExecutionGeneration == other.SkillExecutionGeneration;



        public override int GetHashCode() => HashCode.Combine(ActionId, ContextId, InstanceId, PredictionKey, SkillId, SkillEntryOperation, SkillExecutionGeneration);
    }

    public enum AbilityTimelineMotionChannel : byte
    {
        Action = 1,
        GameplayResult = 2
    }

    public enum AbilityTimelineMotionSpace : byte
    {
        ActorLocal = 0,
        World = 1
    }

    public enum AbilityTimelineMotionBlendMode : byte
    {
        Additive = 0,
        WeightedBlend = 1,
        Override = 2
    }

    public readonly struct AbilityTimelineLogicMotion
    {
        public AbilityTimelineLogicMotion(
            SimulationExecutionSource source,
            CanonicalTextValue sourceIdentity,
            CharacterSkillId abilityId,
            ulong sourceGeneration,
            FP displacementX,
            FP displacementY,
            FP displacementZ,
            FP yawDegrees,
            FP weight,
            int priority,
            AbilityTimelineMotionSpace space,
            AbilityTimelineMotionChannel channel,
            AbilityTimelineMotionBlendMode blendMode,
            bool consumeLowerChannels)
        {
            if (!source.IsValid)
                throw new ArgumentException("Timeline motion requires a valid source.", nameof(source));
            if (!abilityId.IsValid)
                throw new ArgumentException("Timeline motion requires an Ability identity.", nameof(abilityId));
            if (weight < FP._0 || weight > FP._1)
                throw new ArgumentOutOfRangeException(nameof(weight));
            Source = source;
            SourceIdentity = CanonicalTextValue.IsNullOrEmpty(sourceIdentity)
                ? throw new ArgumentException("Timeline motion requires a source identity.", nameof(sourceIdentity))
                : sourceIdentity;
            AbilityId = abilityId;
            SourceGeneration = sourceGeneration;
            DisplacementX = displacementX;
            DisplacementY = displacementY;
            DisplacementZ = displacementZ;
            YawDegrees = yawDegrees;
            Weight = weight;
            Priority = priority;
            Space = space;
            Channel = channel;
            BlendMode = blendMode;
            ConsumeLowerChannels = consumeLowerChannels;
        }

        public SimulationExecutionSource Source { get; }
        public CanonicalTextValue SourceIdentity { get; }
        public CharacterSkillId AbilityId { get; }
        public ulong SourceGeneration { get; }
        public FP DisplacementX { get; }
        public FP DisplacementY { get; }
        public FP DisplacementZ { get; }
        public FP YawDegrees { get; }
        public FP Weight { get; }
        public int Priority { get; }
        public AbilityTimelineMotionSpace Space { get; }
        public AbilityTimelineMotionChannel Channel { get; }
        public AbilityTimelineMotionBlendMode BlendMode { get; }
        public bool ConsumeLowerChannels { get; }
    }

    public interface IAbilityTimelineLogicMotionReader
    {
        int MaximumMotionContributionsPerAdvance { get; }
        void CopyPendingMotion(ulong runtimeHandle, List<AbilityTimelineLogicMotion> results);
    }

    public readonly struct AbilityTimelineMotionSourcePose
    {
        public AbilityTimelineMotionSourcePose(FP x, FP y, FP z, FP yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public FP X { get; }
        public FP Y { get; }
        public FP Z { get; }
        public FP Yaw { get; }

        public static AbilityTimelineMotionSourcePose AccumulatePrefix(
            in AbilityTimelineMotionSourcePose previous,
            in AbilityTimelineMotionSourcePose current,
            in AbilityTimelineMotionSourcePose cycleStart,
            in AbilityTimelineMotionSourcePose cycleEnd,
            int cycleCount)
        {
            FP cycles = cycleCount;
            return new AbilityTimelineMotionSourcePose(
                current.X - previous.X + (cycleEnd.X - cycleStart.X) * cycles,
                current.Y - previous.Y + (cycleEnd.Y - cycleStart.Y) * cycles,
                current.Z - previous.Z + (cycleEnd.Z - cycleStart.Z) * cycles,
                current.Yaw - previous.Yaw + (cycleEnd.Yaw - cycleStart.Yaw) * cycles);
        }
    }

    public interface IAbilityTimelineMotionSourceReader
    {
        SimulationExecutionSource ReadMotionSource(ulong runtimeHandle);
        AbilityTimelineMotionSourcePose SampleMotionSource(ulong runtimeHandle, string motionClipId, FP time);
        AbilityTimelineMotionSourcePose SampleMotionPrefix(ulong runtimeHandle, string motionClipId, int cycle, FP time);
    }

    public readonly struct AbilityTimelineLogicMotionWarp : IMotionWarpWindowRequest
    {
        public AbilityTimelineLogicMotionWarp(
            SimulationExecutionSource source,
            CharacterSkillId abilityId,
            CanonicalTextValue actionContextIdentity,
            ulong actionInstanceId,
            ulong actionPredictionKey,
            ulong skillExecutionGeneration,
            OperationHandle stateOperation,
            ulong playbackGeneration,
            int cycle,
            FP startTime,
            FP endTime,
            FP previousTime,
            FP currentTime,
            FP sourceStartPositionX,
            FP sourceStartPositionY,
            FP sourceStartPositionZ,
            FP sourceStartYawDegrees,
            FP sourceEndPositionX,
            FP sourceEndPositionY,
            FP sourceEndPositionZ,
            FP sourceEndYawDegrees,
            FP previousPositionX,
            FP previousPositionY,
            FP previousPositionZ,
            FP previousYawDegrees,
            FP currentPositionX,
            FP currentPositionY,
            FP currentPositionZ,
            FP currentYawDegrees,
            FP previousPositionProgress,
            FP previousYawProgress,
            FP currentPositionProgress,
            FP currentYawProgress,
            FP yawResponse,
            FP inputDirectionX,
            FP inputDirectionZ,
            FP inputYawResponse,
            ProgramMotionWarpTranslationMode translationMode,
            ProgramMotionWarpTargetOffsetSpace targetOffsetSpace,
            ProgramMotionWarpRotationMode rotationMode,
            ProgramMotionWarpRotationMethod rotationMethod,
            FP targetPlanarOffsetX,
            FP targetPlanarOffsetY,
            FP targetYawOffsetDegrees,
            FP maximumPositionCorrection,
            FP maximumYawCorrectionDegrees,
            FP maximumYawRateDegreesPerSecond,
            ProgramMotionWarpLimitPolicy limitPolicy,
            bool enabled,
            bool hasTarget,
            AbilityTimelineMotionSourcePose target,
            CanonicalTextValue sourceMotionClipId,
            AbilityTreeClipInvocation graphInvocation,
            SimulationExecutionSource nodeSource,
            ulong graphInvocationGeneration,
            ulong parentInvocationGeneration,
            AbilityTimelineMotionSourcePose sourcePrefix)
        {
            Source = source;
            AbilityId = abilityId;
            ActionContextIdentity = actionContextIdentity;
            ActionInstanceId = actionInstanceId;
            ActionPredictionKey = actionPredictionKey;
            SkillExecutionGeneration = skillExecutionGeneration;
            StateOperation = stateOperation;
            PlaybackGeneration = playbackGeneration;
            Cycle = cycle;
            StartTime = startTime;
            EndTime = endTime;
            PreviousTime = previousTime;
            CurrentTime = currentTime;
            SourceStartPositionX = sourceStartPositionX;
            SourceStartPositionY = sourceStartPositionY;
            SourceStartPositionZ = sourceStartPositionZ;
            SourceStartYawDegrees = sourceStartYawDegrees;
            SourceEndPositionX = sourceEndPositionX;
            SourceEndPositionY = sourceEndPositionY;
            SourceEndPositionZ = sourceEndPositionZ;
            SourceEndYawDegrees = sourceEndYawDegrees;
            PreviousPositionX = previousPositionX;
            PreviousPositionY = previousPositionY;
            PreviousPositionZ = previousPositionZ;
            PreviousYawDegrees = previousYawDegrees;
            CurrentPositionX = currentPositionX;
            CurrentPositionY = currentPositionY;
            CurrentPositionZ = currentPositionZ;
            CurrentYawDegrees = currentYawDegrees;
            PreviousPositionProgress = previousPositionProgress;
            PreviousYawProgress = previousYawProgress;
            CurrentPositionProgress = currentPositionProgress;
            CurrentYawProgress = currentYawProgress;
            YawResponse = yawResponse;
            InputDirectionX = inputDirectionX;
            InputDirectionZ = inputDirectionZ;
            InputYawResponse = inputYawResponse;
            TranslationMode = translationMode;
            TargetOffsetSpace = targetOffsetSpace;
            RotationMode = rotationMode;
            RotationMethod = rotationMethod;
            TargetPlanarOffsetX = targetPlanarOffsetX;
            TargetPlanarOffsetY = targetPlanarOffsetY;
            TargetYawOffsetDegrees = targetYawOffsetDegrees;
            MaximumPositionCorrection = maximumPositionCorrection;
            MaximumYawCorrectionDegrees = maximumYawCorrectionDegrees;
            MaximumYawRateDegreesPerSecond = maximumYawRateDegreesPerSecond;
            LimitPolicy = limitPolicy;
            Enabled = enabled;
            HasTarget = hasTarget;
            Target = target;
            SourceMotionClipId = sourceMotionClipId;
            GraphInvocation = graphInvocation;
            NodeSource = nodeSource;
            GraphInvocationGeneration = graphInvocationGeneration;
            ParentInvocationGeneration = parentInvocationGeneration;
            SourcePrefix = sourcePrefix;
        }

        public SimulationExecutionSource Source { get; }
        public CharacterSkillId AbilityId { get; }
        public CanonicalTextValue ActionContextIdentity { get; }
        public ulong ActionInstanceId { get; }
        public ulong ActionPredictionKey { get; }
        public ulong SkillExecutionGeneration { get; }
        public OperationHandle StateOperation { get; }
        public ulong PlaybackGeneration { get; }
        public int Cycle { get; }
        public FP StartTime { get; }
        public FP EndTime { get; }
        public FP PreviousTime { get; }
        public FP CurrentTime { get; }
        public FP SourceStartPositionX { get; }
        public FP SourceStartPositionY { get; }
        public FP SourceStartPositionZ { get; }
        public FP SourceStartYawDegrees { get; }
        public FP SourceEndPositionX { get; }
        public FP SourceEndPositionY { get; }
        public FP SourceEndPositionZ { get; }
        public FP SourceEndYawDegrees { get; }
        public FP PreviousPositionX { get; }
        public FP PreviousPositionY { get; }
        public FP PreviousPositionZ { get; }
        public FP PreviousYawDegrees { get; }
        public FP CurrentPositionX { get; }
        public FP CurrentPositionY { get; }
        public FP CurrentPositionZ { get; }
        public FP CurrentYawDegrees { get; }
        public FP PreviousPositionProgress { get; }
        public FP PreviousYawProgress { get; }
        public FP CurrentPositionProgress { get; }
        public FP CurrentYawProgress { get; }
        public FP YawResponse { get; }
        public FP InputDirectionX { get; }
        public FP InputDirectionZ { get; }
        public FP InputYawResponse { get; }
        public ProgramMotionWarpTranslationMode TranslationMode { get; }
        public ProgramMotionWarpTargetOffsetSpace TargetOffsetSpace { get; }
        public ProgramMotionWarpRotationMode RotationMode { get; }
        public ProgramMotionWarpRotationMethod RotationMethod { get; }
        public FP TargetPlanarOffsetX { get; }
        public FP TargetPlanarOffsetY { get; }
        public FP TargetYawOffsetDegrees { get; }
        public FP MaximumPositionCorrection { get; }
        public FP MaximumYawCorrectionDegrees { get; }
        public FP MaximumYawRateDegreesPerSecond { get; }
        public ProgramMotionWarpLimitPolicy LimitPolicy { get; }
        public bool Enabled { get; }
        public bool HasTarget { get; }
        public AbilityTimelineMotionSourcePose Target { get; }
        public CanonicalTextValue SourceMotionClipId { get; }
        public AbilityTreeClipInvocation GraphInvocation { get; }
        public SimulationExecutionSource NodeSource { get; }
        public ulong GraphInvocationGeneration { get; }
        public ulong ParentInvocationGeneration { get; }
        public AbilityTimelineMotionSourcePose SourcePrefix { get; }
        public bool UsesPositionProgress => TranslationMode is ProgramMotionWarpTranslationMode.SkewToTarget or ProgramMotionWarpTranslationMode.LinearToTarget;
        public bool UsesYawProgress => RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                                       RotationMethod == ProgramMotionWarpRotationMethod.ProgressCurve;
        public bool UsesYawRate => RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                                   RotationMethod == ProgramMotionWarpRotationMethod.ConstantRate;
    }

    [ValueOperations]
    public readonly partial struct AbilityTimelineInvocationSource
    {
        public AbilityTimelineInvocationSource(
            int operationIndex,
            CanonicalTextValue graphAuthoringId,
            CanonicalTextValue nodeAuthoringId,
            CanonicalTextValue graphInvocationPath,
            CanonicalTextValue operationExecutionPath,
            ulong invocationGeneration)
        {
            OperationIndex = operationIndex;
            GraphAuthoringId = graphAuthoringId.Trim();
            NodeAuthoringId = nodeAuthoringId.Trim();
            GraphInvocationPath = graphInvocationPath.Trim();
            OperationExecutionPath = operationExecutionPath.Trim();
            InvocationGeneration = invocationGeneration;
            RequireValid();
        }

        [ValueValidation]
        void RequireValid()
        {
            if (OperationIndex < 0)
                throw new ArgumentOutOfRangeException("operationIndex");
            if (GraphAuthoringId.IsEmpty)
                throw new ArgumentException("Identity is required.", "graphAuthoringId");
            if (NodeAuthoringId.IsEmpty)
                throw new ArgumentException("Identity is required.", "nodeAuthoringId");
            if (GraphInvocationPath.IsEmpty)
                throw new ArgumentException("Identity is required.", "graphInvocationPath");
            if (OperationExecutionPath.IsEmpty)
                throw new ArgumentException("Identity is required.", "operationExecutionPath");
            if (InvocationGeneration == 0)
                throw new ArgumentOutOfRangeException("invocationGeneration");
        }

        public int OperationIndex { get; }
        public CanonicalTextValue GraphAuthoringId { get; }
        public CanonicalTextValue NodeAuthoringId { get; }
        public CanonicalTextValue GraphInvocationPath { get; }
        public CanonicalTextValue OperationExecutionPath { get; }
        public ulong InvocationGeneration { get; }
        public bool IsValid => OperationIndex >= 0 &&
                               !CanonicalTextValue.IsNullOrEmpty(GraphAuthoringId) &&
                               !CanonicalTextValue.IsNullOrEmpty(NodeAuthoringId) &&
                               !CanonicalTextValue.IsNullOrEmpty(GraphInvocationPath) &&
                               !CanonicalTextValue.IsNullOrEmpty(OperationExecutionPath) &&
                               InvocationGeneration != 0;
    }

    internal interface IAbilityTimelineMotionWarpTarget<TChannel> where TChannel : struct
    {
        void CaptureWindowEntry(in AbilityTimelineLogicMotionWarp warp);
        void TraceSourceNotResolved(in AbilityTimelineLogicMotionWarp warp);
        void ApplyTimelineMotionWarp(in AbilityTimelineLogicMotionWarp warp, ref TChannel channel);
        void FailTimelineMotionWarp(OperationHandle operation, string code, string detail);
    }

    internal static class AbilityTimelineMotionWarpRuntime
    {
        public static void ApplyDirect<TChannel, TTarget>(
            IReadOnlyList<AbilityTimelineLogicMotionWarp> warps,
            OperationHandle resolvedOwner,
            CanonicalTextValue resolvedMotionClipId,
            ref TChannel channel,
            TTarget target)
            where TChannel : struct
            where TTarget : IAbilityTimelineMotionWarpTarget<TChannel>
        {
            int selected = -1;
            for (int index = 0; index < warps.Count; index++)
            {
                AbilityTimelineLogicMotionWarp warp = warps[index];
                target.CaptureWindowEntry(in warp);
                if (!warp.Source.Operation.Equals(resolvedOwner) || warp.SourceMotionClipId != resolvedMotionClipId)
                {
                    target.TraceSourceNotResolved(in warp);
                    continue;
                }
                if (selected >= 0)
                {
                    AbilityTimelineLogicMotionWarp previous = warps[selected];
                    if (!IsLoopContinuation(in previous, in warp))
                    {
                        target.FailTimelineMotionWarp(warp.StateOperation, MotionModifierDiagnosticCode.AmbiguousModifier,
                            $"Action channel owner '{resolvedOwner}' has conflicting Timeline MotionWarps: " +
                            $"'{previous.StateOperation}' cycle {previous.Cycle} [{previous.PreviousTime}, {previous.CurrentTime}] and " +
                            $"'{warp.StateOperation}' cycle {warp.Cycle} [{warp.PreviousTime}, {warp.CurrentTime}].");
                        return;
                    }
                }
                selected = index;
            }
            for (int index = 0; index < warps.Count; index++)
            {
                AbilityTimelineLogicMotionWarp warp = warps[index];
                if (warp.Source.Operation.Equals(resolvedOwner) && warp.SourceMotionClipId == resolvedMotionClipId)
                    target.ApplyTimelineMotionWarp(in warp, ref channel);
            }
        }

        static bool IsLoopContinuation(in AbilityTimelineLogicMotionWarp previous, in AbilityTimelineLogicMotionWarp current) =>
            previous.StateOperation.Equals(current.StateOperation) &&
            previous.Source.Equals(current.Source) &&
            previous.AbilityId.Equals(current.AbilityId) &&
            CanonicalTextValue.Equals(previous.ActionContextIdentity, current.ActionContextIdentity, StringComparison.Ordinal) &&
            previous.ActionInstanceId == current.ActionInstanceId &&
            previous.ActionPredictionKey == current.ActionPredictionKey &&
            previous.SkillExecutionGeneration == current.SkillExecutionGeneration &&
            previous.PlaybackGeneration == current.PlaybackGeneration &&
            current.Cycle == previous.Cycle + 1 &&
            previous.CurrentTime == previous.EndTime &&
            current.PreviousTime == current.StartTime;
    }

    public readonly struct TimelinePresentationOutput<TTime>
        where TTime : struct
    {
        public TimelinePresentationOutput(
            OperationHandle operation,
            SimulationExecutionSource source,
            TimelinePresentationOutputKind kind,
            TTime sampleTime,
            TTime weight,
            ulong producerGeneration,
            int cycle,
            ulong sourceActionInstanceId,
            TTime visualTimeScale)
        {
            if (!operation.IsValid)
                throw new ArgumentException("Timeline presentation output requires a valid operation.", nameof(operation));
            if (!source.IsValid)
                throw new ArgumentException("Timeline presentation output source is invalid.", nameof(source));
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            if (RequiresGeneration(kind) && producerGeneration == 0)
                throw new ArgumentOutOfRangeException(nameof(producerGeneration));
            Operation = operation;
            Source = source;
            Kind = kind;
            SampleTime = sampleTime;
            Weight = weight;
            ProducerGeneration = producerGeneration;
            Cycle = cycle;
            SourceActionInstanceId = sourceActionInstanceId;
            VisualTimeScale = visualTimeScale;
        }

        public OperationHandle Operation { get; }
        public SimulationExecutionSource Source { get; }
        public TimelinePresentationOutputKind Kind { get; }
        public TTime SampleTime { get; }
        public TTime Weight { get; }
        public ulong ProducerGeneration { get; }
        public int Cycle { get; }
        public ulong SourceActionInstanceId { get; }
        public TTime VisualTimeScale { get; }

        static bool RequiresGeneration(TimelinePresentationOutputKind kind) =>
            kind == TimelinePresentationOutputKind.SelectProducer ||
            kind == TimelinePresentationOutputKind.SampleProducer ||
            kind == TimelinePresentationOutputKind.CompleteProducer ||
            kind == TimelinePresentationOutputKind.ReleaseProducer ||
            kind == TimelinePresentationOutputKind.Camera ||
            kind == TimelinePresentationOutputKind.ForceProducer;
    }

    public readonly struct TimelineTraceOutput
    {
        public TimelineTraceOutput(
            OperationHandle operation,
            string code,
            TimelineTraceSeverity severity,
            string detail)
            : this(
                operation,
                OperationHandle.Invalid,
                code,
                severity,
                detail,
                0f,
                0,
                0,
                default)
        {
        }

        public TimelineTraceOutput(
            OperationHandle operation,
            OperationHandle timelineOperation,
            string code,
            TimelineTraceSeverity severity,
            string detail,
            float time,
            ulong playbackGeneration,
            int cycle,
            TimelineActionContextIdentity actionContext)
        {
            if (!operation.IsValid)
                throw new ArgumentException("Timeline trace output requires a valid operation.", nameof(operation));
            if (timelineOperation.IsValid && playbackGeneration == 0)
                throw new ArgumentOutOfRangeException(nameof(playbackGeneration));
            if (!float.IsFinite(time))
                throw new ArgumentOutOfRangeException(nameof(time));
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            Operation = operation;
            TimelineOperation = timelineOperation;
            Code = SimulationIdentity.Require(code, nameof(code));
            Severity = severity;
            Detail = detail ?? string.Empty;
            Time = time;
            PlaybackGeneration = playbackGeneration;
            Cycle = cycle;
            ActionContext = actionContext;
        }

        public OperationHandle Operation { get; }
        public OperationHandle TimelineOperation { get; }
        public string Code { get; }
        public TimelineTraceSeverity Severity { get; }
        public string Detail { get; }
        public float Time { get; }
        public ulong PlaybackGeneration { get; }
        public int Cycle { get; }
        public TimelineActionContextIdentity ActionContext { get; }
    }

    public enum AbilityTimelineRuntimeStatus : byte
    {
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4
    }

    [ValueOperations]
    public readonly partial struct AbilityTimelineStartRequest
    {
        public AbilityTimelineStartRequest(
            CanonicalTextValue timelineId,
            ulong playbackIdentity,
            BTSMTL.Timeline.TimelinePlaybackMode playbackMode,
            TimelineActionContextIdentity actionContext,
            AbilityTimelineInvocationSource invocationSource,
            ulong inputSequence,
            SimulationTick tick,
            FP startTime)
        {
            TimelineId = timelineId.Trim();
            PlaybackIdentity = playbackIdentity;
            PlaybackMode = playbackMode;
            InputSequence = inputSequence;
            ActionContext = actionContext;
            InvocationSource = invocationSource;
            Tick = tick;
            StartTime = startTime;
            RequireValid();
        }

        [ValueValidation]
        void RequireValid()
        {
            if (TimelineId.IsEmpty)
                throw new ArgumentException("Identity is required.", "timelineId");
            if (PlaybackIdentity == 0)
                throw new ArgumentOutOfRangeException("playbackIdentity");
            if (!ActionContext.IsValid || !ActionContext.HasSkillExecution || ActionContext.SkillExecutionGeneration == 0)
                throw new ArgumentException("Ability Timeline Action context is incomplete.", "actionContext");
            if (!InvocationSource.IsValid)
                throw new ArgumentException("Ability Timeline invocation source is incomplete.", "invocationSource");
            if (!Tick.IsValid)
                throw new ArgumentOutOfRangeException("tick");
        }

        public CanonicalTextValue TimelineId { get; }
        public ulong PlaybackIdentity { get; }
        public BTSMTL.Timeline.TimelinePlaybackMode PlaybackMode { get; }
        public TimelineActionContextIdentity ActionContext { get; }
        public AbilityTimelineInvocationSource InvocationSource { get; }
        public SimulationTick Tick { get; }
        public ulong InputSequence { get; }
        public FP StartTime { get; }

    }

    public readonly partial struct AbilityTimelinePlaybackControl : IEquatable<AbilityTimelinePlaybackControl>
    {
        public AbilityTimelinePlaybackControl(FP rate, bool paused)
        {
            Rate = rate;
            Paused = paused;
            RequireValid();
        }

        public FP Rate { get; }
        public bool Paused { get; }
        [ValueValidation]
        void RequireValid()
        {
            if (Rate < FP._0)
                throw new ArgumentOutOfRangeException("rate");
        }
        public bool IsPaused => Paused || Rate == FP._0;
        public static AbilityTimelinePlaybackControl Normal => new AbilityTimelinePlaybackControl(FP._1, false);
        public bool Equals(AbilityTimelinePlaybackControl other) => Rate == other.Rate && Paused == other.Paused;
        public override bool Equals(object obj) => obj is AbilityTimelinePlaybackControl other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Rate.RawValue, Paused);
    }

    public enum AbilityTimelineProgressState : byte
    {
        Active = 0,
        Completed = 1,
        Stopped = 2
    }

    [ValueOperations]
    public readonly partial struct AbilityTimelineProgress : IEquatable<AbilityTimelineProgress>
    {
        public AbilityTimelineProgress(
            CanonicalTextValue timelineId,
            CanonicalTextValue contentRevision,
            ulong generation,
            ulong logicTick,
            FP duration,
            FP previousTime,
            FP time,
            int previousCycle,
            int cycle,
            bool loop,
            AbilityTimelineProgressState state,
            AbilityTimelinePlaybackControl control)
        {
            TimelineId = timelineId.Trim();
            ContentRevision = contentRevision.Trim();
            Generation = generation;
            LogicTick = logicTick;
            Duration = duration;
            PreviousTime = previousTime;
            Time = time;
            PreviousCycle = previousCycle;
            Cycle = cycle;
            Loop = loop;
            State = state;
            Control = control;
            if (state > AbilityTimelineProgressState.Stopped)
                throw new ArgumentOutOfRangeException(nameof(state));
            RequireValid();
        }

        [ValueValidation]
        void RequireValid()
        {
            if (TimelineId.IsEmpty)
                throw new ArgumentException("Identity is required.", "timelineId");
            if (ContentRevision.IsEmpty)
                throw new ArgumentException("Identity is required.", "contentRevision");
            if (Generation == 0 || LogicTick == 0)
                throw new ArgumentOutOfRangeException("generation");
            if (Duration < FP._0 || PreviousTime < FP._0 ||
                PreviousTime > Duration || Time < FP._0 || Time > Duration ||
                PreviousCycle < 0 || Cycle < PreviousCycle ||
                Cycle == PreviousCycle && Time < PreviousTime ||
                !Loop && (PreviousCycle != 0 || Cycle != 0))
                throw new ArgumentException("Timeline progress must describe a forward interval within its content.");
        }

        public CanonicalTextValue TimelineId { get; }
        public CanonicalTextValue ContentRevision { get; }
        public ulong Generation { get; }
        public ulong LogicTick { get; }
        public FP Duration { get; }
        public FP PreviousTime { get; }
        public FP Time { get; }
        public int PreviousCycle { get; }
        public int Cycle { get; }
        public bool Loop { get; }
        public AbilityTimelineProgressState State { get; }
        public AbilityTimelinePlaybackControl Control { get; }
        public bool IsTerminal => State != AbilityTimelineProgressState.Active;
        [ValuePresence]
        public bool IsValid => Generation != 0;

        public bool Equals(AbilityTimelineProgress other) =>
            CanonicalTextValue.Equals(TimelineId, other.TimelineId, StringComparison.Ordinal) &&
            CanonicalTextValue.Equals(ContentRevision, other.ContentRevision, StringComparison.Ordinal) &&
            Generation == other.Generation && LogicTick == other.LogicTick &&
            Duration == other.Duration && PreviousTime == other.PreviousTime && Time == other.Time &&
            PreviousCycle == other.PreviousCycle && Cycle == other.Cycle &&
            Loop == other.Loop && State == other.State && Control.Equals(other.Control);


        public override bool Equals(object obj) => obj is AbilityTimelineProgress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TimelineId, ContentRevision, Generation, LogicTick, Time.RawValue, Cycle);
    }

    public readonly struct AbilityTimelineAdvancePending
    {
        public AbilityTimelineAdvancePending(ulong runtimeHandle, ulong sequence, AbilityTimelineProgress progress)
        {
            if (runtimeHandle == 0 || sequence == 0 || !progress.IsValid)
                throw new ArgumentException("Timeline pending advance requires its runtime handle, sequence and progress.");
            RuntimeHandle = runtimeHandle;
            Sequence = sequence;
            Progress = progress;
        }

        public ulong RuntimeHandle { get; }
        public ulong Sequence { get; }
        public AbilityTimelineProgress Progress { get; }
        public bool IsValid => RuntimeHandle > 0 && Sequence != 0;
    }

    public readonly struct AbilityTimelineTickResult
    {
        public AbilityTimelineTickResult(AbilityTimelineRuntimeStatus status, AbilityTimelineAdvancePending pending)
        {
            Status = status;
            Pending = pending;
            if (status == AbilityTimelineRuntimeStatus.Running && (!pending.IsValid || !pending.Progress.IsValid))
                throw new ArgumentException("A running Ability Timeline advance requires a pending commit candidate.");
        }

        public AbilityTimelineRuntimeStatus Status { get; }
        public AbilityTimelineAdvancePending Pending { get; }
        public AbilityTimelineProgress Progress => Pending.Progress;
    }

    public readonly struct AbilityTimelineStopPending
    {
        public AbilityTimelineStopPending(ulong runtimeHandle, ulong sequence, AbilityTimelineProgress progress)
        {
            if (runtimeHandle == 0 || sequence == 0)
                throw new ArgumentException("Timeline stop candidate requires its runtime handle and sequence.");
            RuntimeHandle = runtimeHandle;
            Sequence = sequence;
            Progress = progress;
        }

        public ulong RuntimeHandle { get; }
        public ulong Sequence { get; }
        public AbilityTimelineProgress Progress { get; }
        public bool IsValid => RuntimeHandle > 0 && Sequence != 0;
    }

    public readonly struct AbilityTimelineStopResult
    {
        public AbilityTimelineStopResult(AbilityTimelineRuntimeStatus status, AbilityTimelineStopPending pending, ulong sourceActionInstanceId)
        {
            Status = status;
            Pending = pending;
            SourceActionInstanceId = sourceActionInstanceId;
            if (status == AbilityTimelineRuntimeStatus.Running && !pending.IsValid)
                throw new ArgumentException("A running Ability Timeline stop requires a pending commit candidate.");
        }

        public AbilityTimelineRuntimeStatus Status { get; }
        public AbilityTimelineStopPending Pending { get; }
        public ulong SourceActionInstanceId { get; }
        public AbilityTimelineProgress Progress => Pending.Progress;
    }
    [ValueOperations]
    public readonly partial struct AbilityTimelineTreeClipState
    {
        public AbilityTimelineTreeClipState(
            CanonicalTextValue clipAuthoringId,
            CanonicalTextValue treeGraphId,
            CanonicalTextValue treeGraphRevision,
            CanonicalTextValue nodeAuthoringId,
            ulong playbackGeneration,
            ulong branchRevision,
            int cycle)
        {
            ClipAuthoringId = clipAuthoringId.Trim();
            TreeGraphId = treeGraphId.Trim();
            TreeGraphRevision = treeGraphRevision.Trim();
            NodeAuthoringId = nodeAuthoringId.Trim();
            PlaybackGeneration = playbackGeneration;
            BranchRevision = branchRevision;
            Cycle = cycle;
            RequireValid();
        }

        [ValueValidation]
        void RequireValid()
        {
            if (ClipAuthoringId.IsEmpty)
                throw new ArgumentException("Identity is required.", "clipAuthoringId");
            if (TreeGraphId.IsEmpty)
                throw new ArgumentException("Identity is required.", "treeGraphId");
            if (TreeGraphRevision.IsEmpty)
                throw new ArgumentException("Identity is required.", "treeGraphRevision");
            if (NodeAuthoringId.IsEmpty)
                throw new ArgumentException("Identity is required.", "nodeAuthoringId");
            if (PlaybackGeneration == 0 || BranchRevision == 0 || Cycle < 0)
                throw new ArgumentOutOfRangeException("playbackGeneration");
        }

        public CanonicalTextValue ClipAuthoringId { get; }
        public CanonicalTextValue TreeGraphId { get; }
        public CanonicalTextValue TreeGraphRevision { get; }
        public CanonicalTextValue NodeAuthoringId { get; }
        public ulong PlaybackGeneration { get; }
        public ulong BranchRevision { get; }
        public int Cycle { get; }
    }

    [ValueOperations(RetainMethod = "RetainStateStorage", ReleaseMethod = "ReleaseStateStorage")]
    public readonly partial struct AbilityTimelineRuntimeSnapshot
    {
        public AbilityTimelineRuntimeSnapshot(AbilityTimelineStartRequest request,
            BTSMTL.Timeline.TimelinePlaybackState playback,
            TimelineSnapshotItems<AbilityTimelineTreeClipState> activeTreeClips)
        {
            Request = request;
            Playback = playback;
            ActiveTreeClips = activeTreeClips;
            RequireValid();
        }

        public AbilityTimelineStartRequest Request { get; }
        public BTSMTL.Timeline.TimelinePlaybackState Playback { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipState> ActiveTreeClips { get; }
        public bool IsValid => RuntimeHandle != 0;
        public ulong RuntimeHandle => Request.PlaybackIdentity;
        public ulong Generation => Request.PlaybackIdentity;
        public CanonicalTextValue RequestId => Playback.RequestId;
        public CanonicalTextValue OwnerIdentity => Playback.ExecutionIdentity.OwnerIdentity;
        public CanonicalTextValue CallIdentity => Playback.ExecutionIdentity.CallIdentity;
        public ulong ExecutionInstanceId => Playback.ExecutionIdentity.InstanceId;
        public BTSMTL.Timeline.TimelinePlaybackMode PlaybackMode => Request.PlaybackMode;
        public CanonicalTextValue ContentRevision => Playback.ContentRevision;
        public BTSMTL.Timeline.TimelineRuntimePlaybackState State => Playback.State;
        public FP CursorTime => Playback.CursorTime;
        public int Cycle => Playback.Cycle;
        public int TimeCarry => Playback.TimeCarry;
        public AbilityTimelinePlaybackControl Control => Playback.Control;
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> TreeDecisionExits => Playback.TreeDecisionExits;
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> PendingTreeDecisionExits => Playback.PendingTreeDecisionExits;
        public CanonicalTextValue SectionId => Playback.SectionId;
        public TimelineSnapshotItems<CanonicalTextValue> ActiveClipIds => Playback.ActiveClipIds;
        public bool HasStopContext => Playback.HasStopContext;
        public BTSMTL.Timeline.TimelinePlaybackStopContext StopContext => Playback.StopContext;
        public bool InitialBoundaryPending => Playback.InitialBoundaryPending;
        public CanonicalTextValue TimelineId => Request.TimelineId;
        public TimelineActionContextIdentity ActionContext => Request.ActionContext;
        public AbilityTimelineInvocationSource InvocationSource => Request.InvocationSource;
        public ulong InputSequence => Request.InputSequence;
        public SimulationTick StartTick => Request.Tick;
        public FP StartTime => Request.StartTime;

        [ValueValidation]
        void RequireValid()
        {
            for (int index = 0; index < ActiveTreeClips.Count; index++)
                if (ActiveTreeClips.Span[index].Cycle > Cycle)
                    throw new ArgumentException("Timeline active TreeClip cycle exceeds the playback cycle.", nameof(ActiveTreeClips));
        }

        public partial void RetainStateStorage();
        public partial void ReleaseStateStorage();
    }
    public enum AbilityTreeClipHook : byte
    {
        OnEnable = 0,
        OnDisable = 1,
        OnDestroy = 2,
        Root = 3
    }

    public readonly partial struct AbilityTreeClipInvocation
    {
        public AbilityTreeClipInvocation(
            CanonicalTextValue clipAuthoringId,
            CanonicalTextValue treeGraphId,
            CanonicalTextValue treeGraphRevision,
            CanonicalTextValue nodeAuthoringId,
            AbilityTreeClipHook hook,
            FP time,
            FP previousTime,
            FP windowStartTime,
            int cycle,
            ulong playbackGeneration,
            ulong branchRevision,
            ulong actionInstanceId,
            ulong timelineRuntimeHandle,
            FP bodyStartFraction,
            FP bodyEndFraction)
        {
            ClipAuthoringId = SimulationIdentity.Require(clipAuthoringId, nameof(clipAuthoringId));
            TreeGraphId = SimulationIdentity.Require(treeGraphId, nameof(treeGraphId));
            TreeGraphRevision = SimulationIdentity.Require(treeGraphRevision, nameof(treeGraphRevision));
            NodeAuthoringId = SimulationIdentity.Require(nodeAuthoringId, nameof(nodeAuthoringId));
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            Hook = hook;
            Cycle = cycle;
            PlaybackGeneration = playbackGeneration;
            BranchRevision = branchRevision;
            ActionInstanceId = actionInstanceId;
            TimelineRuntimeHandle = timelineRuntimeHandle;
            BodyStartFraction = bodyStartFraction;
            BodyEndFraction = bodyEndFraction;
            RequireRecord();
        }

        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue ClipAuthoringId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue TreeGraphId { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue TreeGraphRevision { get; }
        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue NodeAuthoringId { get; }
        public AbilityTreeClipHook Hook { get; }
        public FP Time { get; }
        public FP PreviousTime { get; }
        public FP WindowStartTime { get; }
        public int Cycle { get; }
        public ulong PlaybackGeneration { get; }
        public ulong BranchRevision { get; }
        public ulong ActionInstanceId { get; }
        public ulong TimelineRuntimeHandle { get; }
        public FP BodyStartFraction { get; }
        public FP BodyEndFraction { get; }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(ClipAuthoringId) &&
            !CanonicalTextValue.IsNullOrEmpty(TreeGraphId) &&
            !CanonicalTextValue.IsNullOrEmpty(TreeGraphRevision) &&
            !CanonicalTextValue.IsNullOrEmpty(NodeAuthoringId) &&
            PlaybackGeneration != 0 &&
            BranchRevision != 0 &&
            ActionInstanceId != 0 &&
            TimelineRuntimeHandle != 0;

        [ValueValidation]
        void RequireRecord()
        {
            if ((byte)Hook > (byte)AbilityTreeClipHook.Root)
                throw new ArgumentOutOfRangeException("Hook");
            if (Time < FP._0)
                throw new ArgumentOutOfRangeException("Time");
            if (Cycle < 0)
                throw new ArgumentOutOfRangeException("Cycle");
            if (PlaybackGeneration == 0)
                throw new ArgumentOutOfRangeException("PlaybackGeneration");
            if (BranchRevision == 0)
                throw new ArgumentOutOfRangeException("BranchRevision");
            if (ActionInstanceId == 0)
                throw new ArgumentOutOfRangeException("ActionInstanceId");
            if (TimelineRuntimeHandle == 0)
                throw new ArgumentOutOfRangeException("TimelineRuntimeHandle");
        }
    }

    public static class AbilityTimelineTimeProjection
    {
        public static FP Fraction(FP previous, FP advance,
            int previousCycle, FP duration, FP time, int cycle) =>
            advance == FP._0 ? FP._0 :
                (time + duration * (cycle - previousCycle) - previous) / advance;
    }

    public interface IAbilityTreeClipInvoker
    {
        bool InvokeTreeClip(in AbilityTreeClipInvocation invocation);
    }

    public interface IAbilityTreeClipInvokerHost
    {
        IAbilityTreeClipInvoker ExchangeTreeClipInvoker(IAbilityTreeClipInvoker invoker);
    }

    public interface IAbilityTimelineRuntime
    {
        ulong Start(in AbilityTimelineStartRequest request);
        AbilityTimelineTickResult Tick(ulong runtimeHandle, ulong logicTick, int tickCount, AbilityTimelinePlaybackControl control);
        void Commit(AbilityTimelineAdvancePending pending);
        void Discard(AbilityTimelineAdvancePending pending);
        AbilityTimelineRuntimeSnapshot Capture(ulong runtimeHandle);
        AbilityTimelineRuntimeSnapshot CaptureRuntimeSnapshot(ulong runtimeHandle);
        void PrepareSnapshotStorage(in GameplayEffectSessionCapacity capacity);
        AbilityTimelineSnapshotStorage BeginPreparedRead();
        AbilityTimelineRuntimeSnapshot CompletePreparedRead(AbilityTimelineSnapshotStorage storage,
            in AbilityTimelineStartRequest request, in BTSMTL.Timeline.TimelinePlaybackState playback);
        void GetSnapshotCapacity(out int clips, out int exits, out int pendingExits, out int trees);
        ulong ApplyRestore(AbilityTimelineRuntimeSnapshot snapshot, ulong logicTick);
        void DiscardUnpublishedPlaybacks(IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots);
        void ReleaseUnreferencedPlaybacks(IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots, ulong committedTick);
        AbilityTimelineStopResult Stop(ulong runtimeHandle, ulong logicTick);
        void CommitStop(AbilityTimelineStopPending pending);
        void DiscardStop(AbilityTimelineStopPending pending);
        bool RequestTreeClipExit(ulong runtimeHandle, CanonicalTextValue clipAuthoringId, int cycle);
    }


}
