using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationTimelinePlaybackStatus : byte
    {
        Empty = 0,
        Prepared = 1,
        Running = 2,
        Stopping = 3,
        Completed = 4,
        Stopped = 5,
        Failed = 6
    }

    public enum OrchestrationTimelinePlaybackMode : byte
    {
        Once = 0,
        Loop = 1,
        HoldLastFrame = 2
    }

    public enum OrchestrationTimelineHook : byte
    {
        Enable = 0,
        Disable = 1,
        Destroy = 2,
        Root = 3
    }

    public static class OrchestrationTimelineLimits
    {
        public const int MaximumCycleAdvance = 20;
    }

    public enum OrchestrationTimelineClockSource : byte
    {
        FixedTicks = 0,
        ElapsedTime = 1
    }

    public readonly struct OrchestrationTimelineStep<TTime> where TTime : unmanaged
    {
        OrchestrationTimelineStep(ulong tick, OrchestrationTimelineClockSource clockSource,
            TTime previousClockTime, TTime clockTime, TTime rate, bool paused, int sourceIdentityId)
        {
            Tick = tick;
            ClockSource = clockSource;
            PreviousClockTime = previousClockTime;
            ClockTime = clockTime;
            Rate = rate;
            Paused = paused;
            SourceIdentityId = sourceIdentityId;
        }

        public ulong Tick { get; }
        public OrchestrationTimelineClockSource ClockSource { get; }
        public TTime PreviousClockTime { get; }
        public TTime ClockTime { get; }
        public TTime Rate { get; }
        public bool Paused { get; }
        public int SourceIdentityId { get; }

        public static OrchestrationTimelineStep<TTime> FromTicks(ulong tick, TTime rate,
            bool paused, int sourceIdentityId) => new(tick, OrchestrationTimelineClockSource.FixedTicks,
                default, default, rate, paused, sourceIdentityId);

        public static OrchestrationTimelineStep<TTime> FromClockInterval(ulong tick,
            TTime previousClockTime, TTime clockTime, TTime rate, bool paused, int sourceIdentityId) =>
            new(tick, OrchestrationTimelineClockSource.ElapsedTime,
                previousClockTime, clockTime, rate, paused, sourceIdentityId);
    }

    public readonly struct OrchestrationTimelineInvocationToken
    {
        public OrchestrationTimelineInvocationToken(int playbackSlot, ulong sequence,
            ulong playbackIdentity, int clipOrdinal, int cycle, bool marker)
        {
            PlaybackSlot = playbackSlot;
            Sequence = sequence;
            PlaybackIdentity = playbackIdentity;
            ClipOrdinal = clipOrdinal;
            Cycle = cycle;
            Marker = marker;
        }

        public int PlaybackSlot { get; }
        public ulong Sequence { get; }
        public ulong PlaybackIdentity { get; }
        public int ClipOrdinal { get; }
        public int Cycle { get; }
        public bool Marker { get; }
    }

    public readonly struct OrchestrationTimelineInvocation<TTime> where TTime : unmanaged
    {
        public OrchestrationTimelineInvocation(OrchestrationTimelineInvocationToken token,
            OrchestrationExecutionEntry entry, OrchestrationTimelineHook hook,
            ulong tick, int sourceIdentityId,
            TTime time, TTime previousTime, TTime windowStartTime,
            TTime bodyStartFraction, TTime bodyEndFraction)
        {
            Token = token;
            Entry = entry;
            Hook = hook;
            Tick = tick;
            SourceIdentityId = sourceIdentityId;
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            BodyStartFraction = bodyStartFraction;
            BodyEndFraction = bodyEndFraction;
        }

        public OrchestrationTimelineInvocationToken Token { get; }
        public OrchestrationExecutionEntry Entry { get; }
        public OrchestrationTimelineHook Hook { get; }
        public ulong Tick { get; }
        public int SourceIdentityId { get; }
        public TTime Time { get; }
        public TTime PreviousTime { get; }
        public TTime WindowStartTime { get; }
        public TTime BodyStartFraction { get; }
        public TTime BodyEndFraction { get; }
    }

    public readonly struct OrchestrationTimelineInvocationFacts
    {
        public OrchestrationTimelineInvocationFacts(in OrchestrationTimelineInvocation<OrchestrationTime> invocation,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            OrchestrationTime advancePreviousTime, int advancePreviousCycle,
            OrchestrationTime advanceTime, int advanceCycle)
        {
            Invocation = invocation;
            Start = start;
            AdvancePreviousTime = advancePreviousTime;
            AdvancePreviousCycle = advancePreviousCycle;
            AdvanceTime = advanceTime;
            AdvanceCycle = advanceCycle;
        }

        public readonly OrchestrationTimelineInvocation<OrchestrationTime> Invocation;
        public readonly OrchestrationTimelineStart<OrchestrationTime> Start;
        public OrchestrationTime AdvancePreviousTime { get; }
        public int AdvancePreviousCycle { get; }
        public OrchestrationTime AdvanceTime { get; }
        public int AdvanceCycle { get; }
        public bool HasInvocation => Invocation.Token.PlaybackIdentity != 0;
        public bool EndsExecutionFrame => Invocation.Token.Marker ||
            Invocation.Hook == OrchestrationTimelineHook.Disable ||
            Invocation.Hook == OrchestrationTimelineHook.Destroy;
    }

    public readonly ref struct OrchestrationTimelineBatch<TTime> where TTime : unmanaged
    {
        public OrchestrationTimelineBatch(int playbackSlot, ulong sequence,
            OrchestrationTimelinePlaybackStatus status,
            ReadOnlySpan<OrchestrationTimelineInvocation<TTime>> invocations)
        {
            PlaybackSlot = playbackSlot;
            Sequence = sequence;
            Status = status;
            Invocations = invocations;
        }

        public int PlaybackSlot { get; }
        public ulong Sequence { get; }
        public OrchestrationTimelinePlaybackStatus Status { get; }
        public ReadOnlySpan<OrchestrationTimelineInvocation<TTime>> Invocations { get; }
    }

    public readonly struct OrchestrationTimelineEntryBinding
    {
        public OrchestrationTimelineEntryBinding(OrchestrationExecutionEntry enable,
            OrchestrationExecutionEntry root, OrchestrationExecutionEntry disable,
            OrchestrationExecutionEntry destroy)
        {
            Enable = enable;
            Root = root;
            Disable = disable;
            Destroy = destroy;
        }

        public OrchestrationExecutionEntry Enable { get; }
        public OrchestrationExecutionEntry Root { get; }
        public OrchestrationExecutionEntry Disable { get; }
        public OrchestrationExecutionEntry Destroy { get; }

        public OrchestrationExecutionEntry Entry(OrchestrationTimelineHook hook) => hook switch
        {
            OrchestrationTimelineHook.Enable => Enable,
            OrchestrationTimelineHook.Root => Root,
            OrchestrationTimelineHook.Disable => Disable,
            OrchestrationTimelineHook.Destroy => Destroy,
            _ => throw new ArgumentOutOfRangeException(nameof(hook))
        };
    }

    public interface IOrchestrationTimelineBindings<TTime, TValue, TContext>
        where TTime : unmanaged, IEquatable<TTime>
        where TValue : unmanaged
        where TContext : struct
    {
        OrchestrationTimelineStart<TTime> Start(int operationIndex, int contentIdentityId,
            ulong playbackIdentity, ulong operationGeneration, ulong tick, ReadOnlySpan<TValue> inputs,
            ref TContext context);

        OrchestrationTimelineStep<TTime> Step(int operationIndex,
            in OrchestrationTimelineStart<TTime> start, ulong tick, ReadOnlySpan<TValue> inputs,
            ref TContext context);

        void AcceptTerminal(int operationIndex, int playbackSlot, ulong tick,
            in OrchestrationTimelinePlaybackState<TTime> accepted,
            in OrchestrationStopContext stopContext, ref TContext context);

        int EnterInvocation(in OrchestrationTimelineStart<TTime> start,
            in OrchestrationTimelineInvocation<TTime> invocation,
            ref TContext context);

        void SelectExecutionFrame(int frameSlot, ref TContext context);

        OrchestrationOperationResult ReleaseInvocation(in OrchestrationTimelineStart<TTime> start,
            in OrchestrationTimelineInvocation<TTime> invocation,
            ref TContext context);

        void LeaveInvocation(ref TContext context);
    }
}
