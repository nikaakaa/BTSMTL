using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation.Fixed
{
    public enum FixedCharacterInputTraceMode : byte
    {
        Idle = 0,
        PreparingRecording = 1,
        Recording = 2,
        PreparingReplay = 3,
        Replaying = 4,
        Completed = 5,
        Faulted = 6,
        ReplayPaused = 7,
        SessionHistory = 8
    }

    public interface IFixedCharacterInputHistory
    {
        bool IsRecording { get; }
        bool IsReplaying { get; }
        int RecordedInputCount { get; }
        SimulationInput Resolve(in FixedCharacterInputBuildContext context, SimulationInput liveInput);
        void RecordBlackboardWrite(ActorId actor, in FixedBlackboardWriteResult result, in FixedActionReplayIdentity origin);
        FixedTypedIngressBatch ResolveIngress(SimulationTickSourceIdentity source, SimulationTick tick);
        void NotifyIngressDisposition(FixedCharacterControlSourceStateDisposition disposition);
    }

    public readonly struct FixedCharacterInputTraceFrame
    {
        public FixedCharacterInputTraceFrame(
            ActorId actorId,
            SimulationTick tick,
            SimulationInput input)
        {
            if (!actorId.IsValid || !tick.IsValid || !input.IsValid ||
                input.NumericProfile != FixedSimulationNumericProfile.Value ||
                input.TickSource.Kind != SimulationTickSourceKind.LocalLogic ||
                CanonicalTextValue.IsNullOrEmpty(input.TickSource.ClockId) ||
                input.TickSource.SourceTick == 0 ||
                input.Sequence != input.TickSource.SourceTick)
            {
                throw new ArgumentException("Fixed character input trace frame is invalid.");
            }
            ActorId = actorId;
            Tick = tick;
            Input = input.CaptureOwned();
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public SimulationInput Input { get; }
        internal void Retain() { ActorId.Retain(); Input.RetainText(); }
        internal void Release() { Input.ReleaseText(); ActorId.Release(); }
    }

    public sealed class FixedCharacterInputTrace : IDisposable
    {
        readonly ReadOnlyCollection<FixedCharacterInputTraceFrame> m_Frames;
        int m_Retainers = 1;

        public FixedCharacterInputTrace(
            string traceId,
            ActorId actorId,
            int tickRate,
            IEnumerable<FixedCharacterInputTraceFrame> frames)
        {
            if (string.IsNullOrWhiteSpace(traceId) || !actorId.IsValid ||
                tickRate <= 0)
            {
                throw new ArgumentException("Fixed character input trace identity is incomplete.");
            }
            var values = new List<FixedCharacterInputTraceFrame>(
                frames ?? throw new ArgumentNullException(nameof(frames)));
            if (values.Count == 0)
                throw new ArgumentException(
                    "Fixed character input trace requires at least one frame.",
                    nameof(frames));
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].ActorId != actorId ||
                    i > 0 && values[i - 1].Tick.Value + 1 != values[i].Tick.Value)
                {
                    throw new ArgumentException(
                        "Fixed character input trace frames must be contiguous and belong to one Actor.",
                        nameof(frames));
                }
            }
            TraceId = traceId.Trim();
            ActorId = actorId;
            TickRate = tickRate;
            m_Frames = values.AsReadOnly();
            ActorId.Retain();
            for (int index = 0; index < m_Frames.Count; index++) m_Frames[index].Retain();
        }

        public void Retain() => m_Retainers++;
        public void Dispose()
        {
            if (--m_Retainers != 0) return;
            for (int index = 0; index < m_Frames.Count; index++) m_Frames[index].Release();
            ActorId.Release();
        }

        public string TraceId { get; }
        public ActorId ActorId { get; }
        public int TickRate { get; }
        public IReadOnlyList<FixedCharacterInputTraceFrame> Frames => m_Frames;

        public static StableHash ComputeBodyHash(WorldBodyState body) =>
            StableHash.Compute(
                body.ActorId.Value.ToString(),
                body.Position.X.Raw.ToString(CultureInfo.InvariantCulture),
                body.Position.Y.Raw.ToString(CultureInfo.InvariantCulture),
                body.Position.Z.Raw.ToString(CultureInfo.InvariantCulture),
                body.Yaw.Degrees.Raw.ToString(CultureInfo.InvariantCulture),
                body.Velocity.X.Raw.ToString(CultureInfo.InvariantCulture),
                body.Velocity.Y.Raw.ToString(CultureInfo.InvariantCulture),
                body.Velocity.Z.Raw.ToString(CultureInfo.InvariantCulture),
                body.VerticalVelocity.Raw.ToString(CultureInfo.InvariantCulture),
                body.Grounded ? "1" : "0",
                ((byte)body.Collision).ToString(CultureInfo.InvariantCulture));
    }

    public readonly struct FixedCharacterInputTraceStatus
    {
        readonly StableHash m_StartBodyHash;

        public FixedCharacterInputTraceStatus(
            FixedCharacterInputTraceMode mode,
            string traceId,
            string actorId,
            int frameCount,
            int replayedFrameCount,
            StableHash startBodyHash,
            string message)
        {
            Mode = mode;
            TraceId = traceId ?? string.Empty;
            ActorId = actorId ?? string.Empty;
            FrameCount = frameCount;
            ReplayedFrameCount = replayedFrameCount;
            m_StartBodyHash = startBodyHash;
            Message = message ?? string.Empty;
        }

        public FixedCharacterInputTraceMode Mode { get; }
        public string TraceId { get; }
        public string ActorId { get; }
        public int FrameCount { get; }
        public int ReplayedFrameCount { get; }
        public string StartBodyHash => m_StartBodyHash.ToString();
        public string Message { get; }
    }

    public readonly struct FixedCharacterInputReplayFrameEvidence
    {
        public FixedCharacterInputReplayFrameEvidence(
            int relativeFrame,
            ulong recordedTick,
            ulong replayTick,
            StableHash inputHash,
            StableHash bodyHash,
            WorldBodyState body)
        {
            if (relativeFrame < 0 || recordedTick == 0 || replayTick == 0 ||
                !inputHash.IsValid || !bodyHash.IsValid ||
                !body.ActorId.IsValid)
            {
                throw new ArgumentException(
                    "Fixed input replay frame evidence is invalid.");
            }
            RelativeFrame = relativeFrame;
            RecordedTick = recordedTick;
            ReplayTick = replayTick;
            InputHash = inputHash;
            BodyHash = bodyHash;
            Body = body;
        }

        public int RelativeFrame { get; }
        public ulong RecordedTick { get; }
        public ulong ReplayTick { get; }
        public StableHash InputHash { get; }
        public StableHash BodyHash { get; }
        public WorldBodyState Body { get; }
    }

    public sealed class FixedCharacterInputReplayEvidence
    {
        readonly ReadOnlyCollection<FixedCharacterInputReplayFrameEvidence>
            m_Frames;

        public FixedCharacterInputReplayEvidence(
            string traceId,
            StableHash startBodyHash,
            ulong replayStartTick,
            IEnumerable<FixedCharacterInputReplayFrameEvidence> frames)
        {
            TraceId = string.IsNullOrWhiteSpace(traceId)
                ? throw new ArgumentException(
                    "Fixed input replay evidence TraceId is invalid.",
                    nameof(traceId))
                : traceId.Trim();
            if (!startBodyHash.IsValid || replayStartTick == 0)
                throw new ArgumentException(
                    "Fixed input replay evidence identity is invalid.");
            var values = new List<FixedCharacterInputReplayFrameEvidence>(
                frames ?? throw new ArgumentNullException(nameof(frames)));
            if (values.Count == 0)
                throw new ArgumentException(
                    "Fixed input replay evidence requires frames.",
                    nameof(frames));
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].RelativeFrame != i ||
                    i > 0 && values[i - 1].ReplayTick + 1 !=
                    values[i].ReplayTick)
                {
                    throw new ArgumentException(
                        "Fixed input replay evidence frame continuity is invalid.",
                        nameof(frames));
                }
            }
            StartBodyHash = startBodyHash;
            ReplayStartTick = replayStartTick;
            m_Frames = values.AsReadOnly();
            InputSequenceHash = StableHash.Compute(
                values.ConvertAll(value => value.InputHash.ToString()).ToArray());
            BodyTrajectoryHash = StableHash.Compute(
                values.ConvertAll(value => value.BodyHash.ToString()).ToArray());
        }

        public string TraceId { get; }
        public StableHash StartBodyHash { get; }
        public ulong ReplayStartTick { get; }
        public StableHash InputSequenceHash { get; }
        public StableHash BodyTrajectoryHash { get; }
        public IReadOnlyList<FixedCharacterInputReplayFrameEvidence> Frames =>
            m_Frames;
    }

    public static class FixedCharacterInputTraceModule
    {
        sealed class ReplayFrameBuilder
        {
            internal ulong ReplayTick;
            internal WorldBodyState Body;
        }

        static readonly List<FixedCharacterInputTraceFrame> s_RecordingFrames =
            new List<FixedCharacterInputTraceFrame>();

        static FixedCharacterInputTraceMode s_Mode;
        static IFixedCharacterInputHistory s_SessionHistory;
        static ActorId s_ActorId;
        static int s_TickRate;
        static FixedCharacterInputTrace s_Replay;
        static FixedCharacterInputTrace s_LastCompletedTrace;
        static WorldBodyState s_StartBody;
        static StableHash s_StartBodyHash;
        static bool s_HasStartBody;
        static ulong s_ReplayStartTick;
        static int s_ReplayIndex;
        static int s_ReplayBodyCount;
        static int s_ReplayPauseAfterFrameCount;
        static bool s_ContinueAfterReplay;
        static ReplayFrameBuilder[] s_ReplayEvidence =
            Array.Empty<ReplayFrameBuilder>();
        static string s_TraceId = string.Empty;
        static string s_ReplayInputSourceIdentity = string.Empty;
        static string s_ReplayAwaitingBodyMessage = string.Empty;
        static string s_ReplayPausedMessage = string.Empty;
        static string s_ReplayCompletedMessage = string.Empty;
        static string s_Message = string.Empty;
        static FixedCharacterInputTraceStatus s_LastStoppedStatus;

        public static FixedCharacterInputTraceStatus Status =>
            new FixedCharacterInputTraceStatus(
                s_Mode,
                s_TraceId,
                s_ActorId.IsValid ? s_ActorId.Value.ToString() : string.Empty,
                s_Mode == FixedCharacterInputTraceMode.SessionHistory
                    ? s_SessionHistory.RecordedInputCount :
                s_Mode == FixedCharacterInputTraceMode.Recording ||
                s_Mode == FixedCharacterInputTraceMode.PreparingRecording
                    ? s_RecordingFrames.Count
                    : s_Replay?.Frames.Count ?? 0,
                s_ReplayIndex,
                s_HasStartBody
                    ? s_StartBodyHash
                    : default,
                s_Message);

        public static FixedCharacterInputTrace LastCompletedTrace => s_LastCompletedTrace;
        public static FixedCharacterInputTraceStatus LastStoppedStatus => s_LastStoppedStatus;
        public static bool IsRecording => s_Mode == FixedCharacterInputTraceMode.Recording ||
            s_Mode == FixedCharacterInputTraceMode.SessionHistory && s_SessionHistory.IsRecording;
        public static bool IsReplayActive =>
            s_Mode == FixedCharacterInputTraceMode.PreparingReplay ||
            s_Mode == FixedCharacterInputTraceMode.Replaying ||
            s_Mode == FixedCharacterInputTraceMode.ReplayPaused;

        public static bool IsReplayInput(SimulationInput input)
        {
            if (!input.IsValid)
                return false;
            CanonicalTextValue identity = input.InputSourceIdentity;
            using CanonicalTextReadLease read = identity.AcquireRead();
            return identity.Span.StartsWith("FixedInputTrace/".AsSpan(), StringComparison.Ordinal);
        }

        public static void PrepareRecording(ActorId actorId)
        {
            if (!actorId.IsValid)
                throw new ArgumentException(
                    "Fixed character input recording ActorId is invalid.",
                    nameof(actorId));
            RequireIdle();
            ResetState();
            s_ActorId = actorId;
            s_TraceId = Guid.NewGuid().ToString("N");
            s_Mode = FixedCharacterInputTraceMode.PreparingRecording;
            s_Message = "Waiting for the canonical Fixed recording start body.";
        }

        public static void BeginSessionHistory(IFixedCharacterInputHistory history, string traceId)
        {
            RequireIdle();
            ResetState();
            s_SessionHistory = history;
            s_TraceId = traceId;
            s_Mode = FixedCharacterInputTraceMode.SessionHistory;
            s_Message = "Session input history owns the canonical Fixed input boundary.";
        }

        public static void EndSessionHistory(IFixedCharacterInputHistory history)
        {
            if (ReferenceEquals(s_SessionHistory, history))
                Stop();
        }

        public static void StartRecording()
        {
            if (s_Mode != FixedCharacterInputTraceMode.PreparingRecording ||
                !s_HasStartBody)
            {
                throw new InvalidOperationException(
                    "Fixed character input recording start state is not prepared.");
            }
            s_Mode = FixedCharacterInputTraceMode.Recording;
            s_Message = "Waiting for the first canonical Fixed input frame.";
        }

        public static void PrepareRecordingFromCheckpoint(
            ActorId actorId,
            WorldBodyState startBody)
        {
            PrepareRecording(actorId);
            s_StartBody = startBody;
            s_StartBodyHash = FixedCharacterInputTrace.ComputeBodyHash(startBody);
            s_HasStartBody = true;
            s_Message = "Fixed character input recording start body captured from Session state.";
        }

        public static FixedCharacterInputTrace StopRecording()
        {
            if (s_Mode != FixedCharacterInputTraceMode.Recording)
                throw new InvalidOperationException(
                    "Fixed character input recording is not active.");
            if (s_RecordingFrames.Count == 0 || s_TickRate <= 0 ||
                !s_HasStartBody)
            {
                throw new InvalidOperationException(
                    "Fixed character input recording has no canonical closure.");
            }
            var trace = new FixedCharacterInputTrace(
                s_TraceId,
                s_ActorId,
                s_TickRate,
                s_RecordingFrames);
            trace.Retain();
            s_LastCompletedTrace?.Dispose();
            s_LastCompletedTrace = trace;
            ResetState();
            return trace;
        }

        public static void PrepareReplay(FixedCharacterInputTrace trace, ActorId targetActorId, int pauseAfterFrameCount)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!targetActorId.IsValid)
                throw new ArgumentException("Fixed input replay requires a target Actor.", nameof(targetActorId));
            if (pauseAfterFrameCount < 0 || pauseAfterFrameCount >= trace.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(pauseAfterFrameCount));
            RequireIdle();
            ResetState();
            trace.Retain();
            s_Replay = trace;
            s_ActorId = targetActorId;
            s_TraceId = trace.TraceId;
            s_ReplayInputSourceIdentity = $"FixedInputTrace/{trace.TraceId}";
            s_ReplayAwaitingBodyMessage = $"Replayed all {trace.Frames.Count} canonical Fixed inputs; waiting for final Body publication.";
            s_ReplayPausedMessage = $"Fixed character input replay paused after {pauseAfterFrameCount} warmup frames.";
            s_ReplayCompletedMessage = $"Replayed and observed all {trace.Frames.Count} canonical Fixed input frames.";
            s_ReplayPauseAfterFrameCount = pauseAfterFrameCount;
            s_ReplayEvidence = new ReplayFrameBuilder[trace.Frames.Count];
            for (int i = 0; i < s_ReplayEvidence.Length; i++)
                s_ReplayEvidence[i] = new ReplayFrameBuilder();
            s_Mode = FixedCharacterInputTraceMode.PreparingReplay;
            s_Message = "Waiting for the canonical Fixed replay start body.";
        }

        public static void StartReplay()
        {
            if (s_Mode != FixedCharacterInputTraceMode.PreparingReplay ||
                s_Replay == null || !s_HasStartBody)
            {
                throw new InvalidOperationException(
                    $"Fixed character input replay start state is not prepared. Mode={s_Mode}, HasStartBody={s_HasStartBody}, Replay={(s_Replay == null ? "null" : s_Replay.TraceId)}.");
            }
            s_Mode = FixedCharacterInputTraceMode.Replaying;
            s_Message = "Waiting for the first canonical Fixed replay input frame.";
        }

        public static void PrepareReplayFromCheckpoint(
            FixedCharacterInputTrace trace,
            int pauseAfterFrameCount,
            WorldBodyState startBody,
            bool continueAfterReplay = false)
        {
            PrepareReplay(trace, startBody.ActorId, pauseAfterFrameCount);
            s_StartBody = startBody;
            s_StartBodyHash = FixedCharacterInputTrace.ComputeBodyHash(startBody);
            s_HasStartBody = true;
            s_ContinueAfterReplay = continueAfterReplay;
            s_Message = "Fixed character input replay start body restored from Session checkpoint.";
        }

        public static void ResumeReplay()
        {
            if (s_Mode != FixedCharacterInputTraceMode.ReplayPaused ||
                s_ReplayIndex != s_ReplayPauseAfterFrameCount ||
                s_ReplayBodyCount != s_ReplayPauseAfterFrameCount)
            {
                throw new InvalidOperationException("Fixed character input replay is not paused at its capture boundary.");
            }
            s_Mode = FixedCharacterInputTraceMode.Replaying;
            s_Message = "Fixed character input replay resumed for capture.";
        }

        public static WorldBodyState ResolveInitialBody(WorldBodyState authoredBody)
        {
            if (authoredBody.ActorId != s_ActorId ||
                s_Mode != FixedCharacterInputTraceMode.PreparingRecording &&
                s_Mode != FixedCharacterInputTraceMode.PreparingReplay)
            {
                return authoredBody;
            }
            if (s_HasStartBody && !BodyEquals(s_StartBody, authoredBody))
                throw new InvalidOperationException(
                    "Fixed character input trace start body was registered more than once with different state.");
            if (!s_HasStartBody)
            {
                s_StartBody = authoredBody;
                s_StartBodyHash = FixedCharacterInputTrace.ComputeBodyHash(authoredBody);
                s_HasStartBody = true;
            }
            s_Message = "Canonical Fixed trace start body registered; Session simulation is gated.";
            return authoredBody;
        }

        public static bool CanAdvanceSimulation(ActorId actorId) =>
            actorId != s_ActorId ||
            s_Mode != FixedCharacterInputTraceMode.PreparingRecording &&
            s_Mode != FixedCharacterInputTraceMode.PreparingReplay &&
            s_Mode != FixedCharacterInputTraceMode.ReplayPaused &&
            s_Mode != FixedCharacterInputTraceMode.Completed;

        public static void ObservePublishedBody(
            ActorId actorId,
            SimulationTick tick,
            WorldBodyState body)
        {
            if (actorId != s_ActorId ||
                s_Mode != FixedCharacterInputTraceMode.Replaying)
            {
                return;
            }
            try
            {
                if (body.ActorId != actorId ||
                    s_ReplayBodyCount >= s_ReplayIndex ||
                    s_ReplayBodyCount >= s_ReplayEvidence.Length)
                {
                    throw new InvalidOperationException(
                        "Fixed character input replay published Body without a matching input frame.");
                }
                ReplayFrameBuilder builder =
                    s_ReplayEvidence[s_ReplayBodyCount];
                if (builder.ReplayTick != tick.Value)
                {
                    throw new InvalidOperationException(
                        "Fixed character input replay Body Tick does not match its input frame.");
                }
                builder.Body = body;
                s_ReplayBodyCount++;
                if (s_ReplayPauseAfterFrameCount > 0 &&
                    s_ReplayBodyCount == s_ReplayPauseAfterFrameCount)
                {
                    s_Mode = FixedCharacterInputTraceMode.ReplayPaused;
                    s_Message = s_ReplayPausedMessage;
                }
                else if (s_ReplayIndex == s_Replay.Frames.Count &&
                    s_ReplayBodyCount == s_Replay.Frames.Count)
                {
                    if (s_ContinueAfterReplay)
                    {
                        ResetState();
                    }
                    else
                    {
                        s_Mode = FixedCharacterInputTraceMode.Completed;
                        s_Message = s_ReplayCompletedMessage;
                    }
                }
            }
            catch (Exception exception)
            {
                s_Mode = FixedCharacterInputTraceMode.Faulted;
                s_Message = exception.Message;
                throw;
            }
        }

        public static FixedCharacterInputReplayEvidence CaptureReplayEvidence()
        {
            if (s_Mode != FixedCharacterInputTraceMode.Completed ||
                s_Replay == null || s_ReplayStartTick == 0 ||
                s_ReplayIndex != s_Replay.Frames.Count ||
                s_ReplayBodyCount != s_Replay.Frames.Count)
            {
                throw new InvalidOperationException(
                    "Fixed character input replay evidence is incomplete.");
            }
            var frames = new FixedCharacterInputReplayFrameEvidence[
                s_Replay.Frames.Count];
            for (int i = 0; i < frames.Length; i++)
            {
                ReplayFrameBuilder builder = s_ReplayEvidence[i];
                frames[i] = new FixedCharacterInputReplayFrameEvidence(
                    i,
                    s_Replay.Frames[i].Tick.Value,
                    builder.ReplayTick,
                    ComputeInputHash(s_Replay.Frames[i].Input),
                    FixedCharacterInputTrace.ComputeBodyHash(builder.Body),
                    builder.Body);
            }
            return new FixedCharacterInputReplayEvidence(
                s_Replay.TraceId,
                s_StartBodyHash,
                s_ReplayStartTick,
                frames);
        }

        public static void Stop()
        {
            if (s_Mode == FixedCharacterInputTraceMode.Idle)
                return;
            FixedCharacterInputTraceStatus stoppedStatus = Status;
            ResetState();
            s_LastStoppedStatus = stoppedStatus;
        }

        public static void ClearCompletedTrace()
        {
            Stop();
            s_LastCompletedTrace?.Dispose();
            s_LastCompletedTrace = null;
        }

        internal static void RecordBlackboardWrite(ActorId actor, in FixedBlackboardWriteResult result, in FixedActionReplayIdentity origin)
        {
            if (s_Mode == FixedCharacterInputTraceMode.SessionHistory && s_SessionHistory.IsRecording)
                s_SessionHistory.RecordBlackboardWrite(actor, in result, in origin);
        }

        internal static FixedTypedIngressBatch ResolveIngress(SimulationTickSourceIdentity source, SimulationTick tick) =>
            s_Mode == FixedCharacterInputTraceMode.SessionHistory
                ? s_SessionHistory.ResolveIngress(source, tick) : FixedTypedIngressBatch.Empty;

        internal static void NotifyIngressDisposition(FixedCharacterControlSourceStateDisposition disposition)
        {
            if (s_Mode == FixedCharacterInputTraceMode.SessionHistory)
                s_SessionHistory.NotifyIngressDisposition(disposition);
        }

        public static bool IsSessionHistoryReplaying =>
            s_Mode == FixedCharacterInputTraceMode.SessionHistory && s_SessionHistory.IsReplaying;

        public static SimulationInput Resolve(
            FixedCharacterInputBuildContext context,
            SimulationInput liveInput)
        {
            if (s_Mode == FixedCharacterInputTraceMode.SessionHistory)
                return s_SessionHistory.Resolve(in context, liveInput);
            if (!liveInput.IsValid)
                throw new ArgumentNullException(nameof(liveInput));
            if (context.ActorId != s_ActorId ||
                s_Mode == FixedCharacterInputTraceMode.Idle)
            {
                return liveInput;
            }
            try
            {
                return s_Mode switch
                {
                    FixedCharacterInputTraceMode.PreparingRecording =>
                        throw new InvalidOperationException(
                            "Fixed character input recording consumed input before start release."),
                    FixedCharacterInputTraceMode.Recording =>
                        Record(context, liveInput),
                    FixedCharacterInputTraceMode.PreparingReplay =>
                        throw new InvalidOperationException(
                            "Fixed character input replay consumed input before start release."),
                    FixedCharacterInputTraceMode.Replaying =>
                        Replay(context),
                    FixedCharacterInputTraceMode.ReplayPaused =>
                        throw new InvalidOperationException(
                            "Fixed character input replay consumed input while paused at the capture boundary."),
                    FixedCharacterInputTraceMode.Completed =>
                        HoldLastReplayFrame(context),
                    FixedCharacterInputTraceMode.Faulted =>
                        throw new InvalidOperationException(s_Message),
                    _ => liveInput
                };
            }
            catch (Exception exception)
            {
                s_Mode = FixedCharacterInputTraceMode.Faulted;
                s_Message = exception.Message;
                throw;
            }
        }

        static SimulationInput Record(
            FixedCharacterInputBuildContext context,
            SimulationInput liveInput)
        {
            if (s_RecordingFrames.Count == 0)
            {
                s_TickRate = context.TickRate;
            }
            else if (s_RecordingFrames[^1].Tick.Value + 1 !=
                     context.SimulationTick.Value)
            {
                throw new InvalidOperationException(
                    "Fixed character input recording Tick continuity changed.");
            }
            var frame = new FixedCharacterInputTraceFrame(context.ActorId, context.SimulationTick, liveInput);
            frame.Retain();
            s_RecordingFrames.Add(frame);
            s_Message = "Recording canonical Fixed input frames.";
            return liveInput;
        }

        static SimulationInput Replay(
            FixedCharacterInputBuildContext context)
        {
            if (s_ReplayStartTick == 0)
                s_ReplayStartTick = context.SimulationTick.Value;
            ulong expectedTick = checked(s_ReplayStartTick + (ulong)s_ReplayIndex);
            if (context.SimulationTick.Value != expectedTick)
                throw new InvalidOperationException(
                    "Fixed character input replay Tick continuity changed.");
            FixedCharacterInputTraceFrame frame = s_Replay.Frames[s_ReplayIndex];
            SimulationInput result = context.InputStorage.Remap(
                frame.Input,
                context.Source,
                s_ReplayInputSourceIdentity,
                context.InputSequence,
                frame.Tick.Value,
                context.SimulationTick.Value);
            ReplayFrameBuilder builder = s_ReplayEvidence[s_ReplayIndex];
            if (builder.ReplayTick != 0)
                throw new InvalidOperationException(
                    "Fixed character input replay frame was resolved more than once.");
            builder.ReplayTick = context.SimulationTick.Value;
            s_ReplayIndex++;
            if (s_ReplayIndex == s_Replay.Frames.Count)
            {
                s_Message = s_ReplayAwaitingBodyMessage;
            }
            else
            {
                s_Message = "Replaying canonical Fixed input frames.";
            }
            return result;
        }

        static SimulationInput HoldLastReplayFrame(
            FixedCharacterInputBuildContext context)
        {
            FixedCharacterInputTraceFrame frame = s_Replay.Frames[^1];
            return context.InputStorage.Hold(
                frame.Input,
                context.Source,
                s_ReplayInputSourceIdentity,
                context.InputSequence);
        }

        static bool BodyEquals(WorldBodyState left, WorldBodyState right) =>
            left.ActorId == right.ActorId &&
            left.Position == right.Position &&
            left.Yaw == right.Yaw &&
            left.Velocity == right.Velocity &&
            left.VerticalVelocity == right.VerticalVelocity &&
            left.Grounded == right.Grounded &&
            left.Collision == right.Collision;

        static StableHash ComputeInputHash(SimulationInput input)
        {
            var values = new string[5 + input.Values.Count * 15 + input.Requests.Count * 5];
            int index = 0;
            values[index++] = input.NumericProfile.ToString();
            values[index++] = ((byte)input.TickSource.Kind).ToString(CultureInfo.InvariantCulture);
            values[index++] = input.TickSource.ClockId.ToString();
            values[index++] = input.TickSource.SourceTick.ToString(CultureInfo.InvariantCulture);
            values[index++] = input.Sequence.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < input.Values.Count; i++)
            {
                SimulationInputValue value = input.Values[i];
                values[index++] = value.InputId.ToString();
                values[index++] = ((byte)value.Kind).ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Boolean ? "1" : "0";
                values[index++] = value.Scalar.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Vector2.X.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Vector2.Y.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Vector3.X.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Vector3.Y.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Vector3.Z.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.Yaw.Degrees.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.ActionTargetSnapshot.TargetId.ToString();
                values[index++] = value.ActionTargetSnapshot.Position.X.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.ActionTargetSnapshot.Position.Y.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.ActionTargetSnapshot.Position.Z.Raw.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = value.ActionTargetSnapshot.Yaw.Degrees.Raw.ToString(
                    CultureInfo.InvariantCulture);
            }
            for (int i = 0; i < input.Requests.Count; i++)
            {
                SimulationInputRequest request = input.Requests[i];
                values[index++] = request.RequestId.ToString();
                values[index++] = request.Sequence.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = request.SourceTick.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = request.ExpireSimulationTick.ToString(
                    CultureInfo.InvariantCulture);
                values[index++] = request.Priority.ToString(
                    CultureInfo.InvariantCulture);
            }
            return StableHash.Compute(values);
        }

        static void RequireIdle()
        {
            if (s_Mode != FixedCharacterInputTraceMode.Idle)
                throw new InvalidOperationException(
                    $"Fixed character input trace is already {s_Mode}.");
        }

        static void ResetState()
        {
            s_LastStoppedStatus = default;
            s_Mode = FixedCharacterInputTraceMode.Idle;
            s_ActorId = default;
            s_TickRate = 0;
            s_Replay?.Dispose();
            s_Replay = null;
            s_StartBody = default;
            s_StartBodyHash = default;
            s_HasStartBody = false;
            s_ReplayStartTick = 0;
            s_ReplayIndex = 0;
            s_ReplayBodyCount = 0;
            s_ReplayPauseAfterFrameCount = 0;
            s_ContinueAfterReplay = false;
            s_ReplayEvidence = Array.Empty<ReplayFrameBuilder>();
            s_TraceId = string.Empty;
            s_ReplayInputSourceIdentity = string.Empty;
            s_ReplayAwaitingBodyMessage = string.Empty;
            s_ReplayPausedMessage = string.Empty;
            s_ReplayCompletedMessage = string.Empty;
            s_Message = string.Empty;
            for (int index = 0; index < s_RecordingFrames.Count; index++) s_RecordingFrames[index].Release();
            s_RecordingFrames.Clear();
            s_SessionHistory = null;
        }
    }
}
