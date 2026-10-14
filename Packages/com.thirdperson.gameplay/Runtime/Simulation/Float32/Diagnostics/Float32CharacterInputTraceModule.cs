using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct Float32CharacterInputTraceFrame
    {
        public Float32CharacterInputTraceFrame(
            ActorId actorId,
            SimulationTick tick,
            SimulationInput input)
        {
            if (!actorId.IsValid || !tick.IsValid || input == null ||
                input.NumericProfile != Float32SimulationNumericProfile.Value ||
                input.TickSource.Kind != SimulationTickSourceKind.LocalLogic ||
                input.TickSource.SourceTick == 0 || input.Sequence != input.TickSource.SourceTick)
            {
                throw new ArgumentException("Float32 character input trace frame is invalid.");
            }
            ActorId = actorId;
            Tick = tick;
            Input = input;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public SimulationInput Input { get; }
    }

    public sealed class Float32CharacterInputTrace : IDisposable
    {
        readonly List<Float32CharacterInputTraceFrame> m_Frames;
        readonly Float32SimulationInputFrameStorage[] m_InputFrames;
        int m_Retainers = 1;

        public Float32CharacterInputTrace(string traceId, ActorId actorId, IReadOnlyList<Float32CharacterInputTraceFrame> frames)
        {
            if (string.IsNullOrWhiteSpace(traceId) || !actorId.IsValid || frames == null || frames.Count == 0)
                throw new ArgumentException("Float32 input trace identity is incomplete.");
            for (int index = 0; index < frames.Count; index++)
                if (frames[index].ActorId != actorId || index > 0 && frames[index - 1].Tick.Value + 1 != frames[index].Tick.Value)
                    throw new ArgumentException("Float32 input trace frames must be contiguous and belong to one Actor.", nameof(frames));
            TraceId = traceId.Trim(); ActorId = actorId; actorId.Value.Retain();
            m_Frames = new List<Float32CharacterInputTraceFrame>(frames.Count);
            m_InputFrames = new Float32SimulationInputFrameStorage[frames.Count];
            for (int index = 0; index < frames.Count; index++)
            {
                Float32CharacterInputTraceFrame frame = frames[index];
                var storage = new Float32SimulationInputFrameStorage(frame.Input.Values.Count, frame.Input.Requests.Count);
                m_InputFrames[index] = storage;
                m_Frames.Add(new Float32CharacterInputTraceFrame(actorId, frame.Tick, storage.Capture(frame.Input)));
            }
        }

        internal Float32CharacterInputTrace(string traceId, ActorId actorId, int frames, int values, int requests)
        {
            TraceId = traceId; ActorId = actorId; actorId.Value.Retain();
            m_Frames = new List<Float32CharacterInputTraceFrame>(frames);
            m_InputFrames = new Float32SimulationInputFrameStorage[frames];
            for (int index = 0; index < frames; index++) m_InputFrames[index] = new Float32SimulationInputFrameStorage(values, requests);
        }

        public string TraceId { get; }
        public ActorId ActorId { get; }
        public IReadOnlyList<Float32CharacterInputTraceFrame> Frames => m_Frames;
        internal void Record(SimulationTick tick, SimulationInput input)
        {
            if (m_Frames.Count == m_InputFrames.Length)
                throw new InvalidOperationException("Float32 input recording exceeds RecordedFrameCapacity.");
            if (m_Frames.Count > 0 && m_Frames[m_Frames.Count - 1].Tick.Value + 1 != tick.Value)
                throw new InvalidOperationException("Float32 input recording Tick continuity changed.");
            SimulationInput frozen = m_InputFrames[m_Frames.Count].Capture(input);
            m_Frames.Add(new Float32CharacterInputTraceFrame(ActorId, tick, frozen));
        }

        internal void RestoreCount(int count)
        {
            for (int index = count; index < m_Frames.Count; index++) m_InputFrames[index].Release();
            m_Frames.RemoveRange(count, m_Frames.Count - count);
        }
        public void Retain() => m_Retainers++;
        public void Dispose()
        {
            if (--m_Retainers != 0) return;
            for (int index = 0; index < m_InputFrames.Length; index++) m_InputFrames[index].Release();
            m_Frames.Clear(); ActorId.Value.Release();
        }
    }

    internal readonly struct Float32InputTraceProgress
    {
        internal Float32InputTraceProgress(int recorded, int replay, ulong startTick)
        { Recorded = recorded; Replay = replay; StartTick = startTick; }
        internal readonly int Recorded, Replay;
        internal readonly ulong StartTick;
    }

    public static class Float32CharacterInputTraceModule
    {
        static Float32CharacterInputTrace s_Recording;
        static Float32CharacterInputTraceMode s_Mode;
        static ActorId s_ActorId;
        static Float32CharacterInputTrace s_Replay;
        static Float32CharacterInputTrace s_LastCompletedTrace;
        static int s_ReplayIndex;
        static ulong s_ReplayStartTick;
        static string s_TraceId = string.Empty;
        static string s_ReplayInputSourceIdentity = string.Empty;

        enum Float32CharacterInputTraceMode : byte
        {
            Idle = 0,
            Recording = 1,
            Replaying = 2
        }

        public static Float32CharacterInputTrace LastCompletedTrace => s_LastCompletedTrace;
        public static bool IsRecording => s_Mode == Float32CharacterInputTraceMode.Recording;
        public static bool IsReplayActive => s_Mode == Float32CharacterInputTraceMode.Replaying;

        public static bool IsReplayInput(SimulationInput input) =>
            input != null &&
            input.InputSourceIdentity.StartsWith("Float32InputTrace/", StringComparison.Ordinal);

        public static void PrepareRecording(ActorId actorId, int frameCapacity, int inputValues, int inputRequests)
        {
            RequireIdle();
            if (!actorId.IsValid)
                throw new ArgumentException("Float32 recording ActorId is invalid.", nameof(actorId));
            if (frameCapacity <= 0) throw new InvalidOperationException("Float32 input recording requires a positive RecordedFrameCapacity declaration.");
            ResetActiveState();
            s_ActorId = actorId;
            s_TraceId = Guid.NewGuid().ToString("N");
            s_Recording = new Float32CharacterInputTrace(s_TraceId, actorId, frameCapacity, inputValues, inputRequests);
            s_Mode = Float32CharacterInputTraceMode.Recording;
        }

        public static Float32CharacterInputTrace StopRecording()
        {
            if (s_Mode != Float32CharacterInputTraceMode.Recording || s_Recording.Frames.Count == 0)
                throw new InvalidOperationException("Float32 input recording has no complete trace.");
            Float32CharacterInputTrace trace = s_Recording;
            s_Recording = null;
            s_LastCompletedTrace?.Dispose();
            s_LastCompletedTrace = trace;
            ResetActiveState();
            return trace;
        }

        public static void PrepareReplay(Float32CharacterInputTrace trace)
        {
            RequireIdle();
            s_Replay = trace ?? throw new ArgumentNullException(nameof(trace));
            trace.Retain();
            s_ActorId = trace.ActorId;
            s_TraceId = trace.TraceId;
            s_ReplayInputSourceIdentity = $"Float32InputTrace/{trace.TraceId}";
            s_ReplayIndex = 0;
            s_ReplayStartTick = 0;
            s_Mode = Float32CharacterInputTraceMode.Replaying;
        }

        public static SimulationInput Resolve(
            SimulationInputBuildContext context,
            SimulationInput liveInput)
        {
            if (liveInput == null)
                throw new ArgumentNullException(nameof(liveInput));
            if (context.ActorId != s_ActorId || s_Mode == Float32CharacterInputTraceMode.Idle)
                return liveInput;
            if (s_Mode == Float32CharacterInputTraceMode.Recording)
            {
                s_Recording.Record(context.SimulationTick, liveInput);
                return liveInput;
            }
            if (s_ReplayIndex >= s_Replay.Frames.Count)
                return liveInput;
            if (s_ReplayStartTick == 0)
                s_ReplayStartTick = context.SimulationTick.Value;
            Float32CharacterInputTraceFrame frame = s_Replay.Frames[s_ReplayIndex];
            SimulationInput result = Remap(frame, context);
            s_ReplayIndex++;
            return result;
        }

        public static void Stop()
        {
            ResetActiveState();
        }

        public static void ClearCompletedTrace()
        {
            ResetActiveState();
            s_LastCompletedTrace?.Dispose();
            s_LastCompletedTrace = null;
        }

        static SimulationInput Remap(Float32CharacterInputTraceFrame frame, SimulationInputBuildContext context) =>
            context.InputStorage.Remap(frame.Input, context, s_ReplayInputSourceIdentity, frame.Tick.Value);

        internal static Float32InputTraceProgress CaptureProgress() => new Float32InputTraceProgress(
            s_Mode == Float32CharacterInputTraceMode.Recording ? s_Recording.Frames.Count : 0, s_ReplayIndex, s_ReplayStartTick);
        internal static void RestoreProgress(in Float32InputTraceProgress progress)
        {
            if (s_Mode == Float32CharacterInputTraceMode.Recording) s_Recording.RestoreCount(progress.Recorded);
            s_ReplayIndex = progress.Replay; s_ReplayStartTick = progress.StartTick;
        }
        internal static void CommitProgress()
        {
            if (s_Mode == Float32CharacterInputTraceMode.Replaying && s_ReplayIndex == s_Replay.Frames.Count) ResetActiveState();
        }

        static void RequireIdle()
        {
            if (s_Mode != Float32CharacterInputTraceMode.Idle)
                throw new InvalidOperationException("Float32 character input trace is already active.");
        }

        static void ResetActiveState()
        {
            s_Mode = Float32CharacterInputTraceMode.Idle;
            s_ActorId = default;
            s_Recording?.Dispose(); s_Recording = null;
            s_Replay?.Dispose(); s_Replay = null;
            s_ReplayIndex = 0;
            s_ReplayStartTick = 0;
            s_TraceId = string.Empty;
            s_ReplayInputSourceIdentity = string.Empty;
        }
    }
}
