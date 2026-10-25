using System;

namespace ThirdPersonSimulation
{
    public interface ITimelineMotionWarpArbitrationRequest<TTime> : IMotionWarpWindowRequest
        where TTime : unmanaged
    {
        OperationHandle StateOperation { get; }
        int StateProgramIdentityId { get; }
        int SourceProgramIdentityId { get; }
        int SourceExecutionPathIdentityId { get; }
        int SkillContentId { get; }
        int ActionContextIdentityId { get; }
        ulong ActionInstanceId { get; }
        ulong ExecutionGeneration { get; }
        int SourceMotionClipIdentityId { get; }
        TTime StartTime { get; }
        TTime EndTime { get; }
        TTime PreviousTime { get; }
        TTime CurrentTime { get; }
    }

    public interface ITimelineMotionWarpArbitrationTarget<TRequest, TChannel>
        where TRequest : struct
        where TChannel : struct
    {
        void CaptureWindowEntry(in TRequest request);
        void TraceSourceNotResolved(in TRequest request);
        void ApplyTimelineMotionWarp(in TRequest request, ref TChannel channel);
        void FailAmbiguousModifier(in TRequest previous, in TRequest current);
    }

    public static class TimelineMotionWarpArbitration<TTime, TMath>
        where TTime : unmanaged
        where TMath : struct, IScalarMath<TTime>
    {
        public static bool ApplyDirect<TRequest, TChannel, TTarget>(
            ReadOnlySpan<TRequest> warps, int resolvedSkillContentId,
            int resolvedSourceProgramIdentityId, OperationHandle resolvedOwner,
            int resolvedSourceExecutionPathIdentityId, int resolvedMotionClipIdentityId,
            ref TChannel channel, ref TTarget target)
            where TRequest : struct, ITimelineMotionWarpArbitrationRequest<TTime>
            where TChannel : struct
            where TTarget : ITimelineMotionWarpArbitrationTarget<TRequest, TChannel>
        {
            int selected = -1;
            for (int index = 0; index < warps.Length; index++)
            {
                ref readonly TRequest warp = ref warps[index];
                target.CaptureWindowEntry(in warp);
                if (warp.SkillContentId != resolvedSkillContentId ||
                    warp.SourceProgramIdentityId != resolvedSourceProgramIdentityId ||
                    !warp.SourceOperation.Equals(resolvedOwner) ||
                    warp.SourceExecutionPathIdentityId != resolvedSourceExecutionPathIdentityId ||
                    warp.SourceMotionClipIdentityId != resolvedMotionClipIdentityId)
                {
                    target.TraceSourceNotResolved(in warp);
                    continue;
                }
                if (selected >= 0)
                {
                    ref readonly TRequest previous = ref warps[selected];
                    if (!IsLoopContinuation(in previous, in warp) && !IsOrderedWindow(in previous, in warp))
                    {
                        target.FailAmbiguousModifier(in previous, in warp);
                        return false;
                    }
                }
                selected = index;
            }
            for (int index = 0; index < warps.Length; index++)
            {
                ref readonly TRequest warp = ref warps[index];
                if (warp.SkillContentId == resolvedSkillContentId &&
                    warp.SourceProgramIdentityId == resolvedSourceProgramIdentityId &&
                    warp.SourceOperation.Equals(resolvedOwner) &&
                    warp.SourceExecutionPathIdentityId == resolvedSourceExecutionPathIdentityId &&
                    warp.SourceMotionClipIdentityId == resolvedMotionClipIdentityId)
                    target.ApplyTimelineMotionWarp(in warp, ref channel);
            }
            return true;
        }

        static bool IsLoopContinuation<TRequest>(in TRequest previous, in TRequest current)
            where TRequest : struct, ITimelineMotionWarpArbitrationRequest<TTime> =>
            previous.StateProgramIdentityId == current.StateProgramIdentityId &&
            previous.StateOperation.Equals(current.StateOperation) &&
            previous.SourceProgramIdentityId == current.SourceProgramIdentityId &&
            previous.SourceOperation.Equals(current.SourceOperation) &&
            previous.SourceExecutionPathIdentityId == current.SourceExecutionPathIdentityId &&
            previous.SkillContentId == current.SkillContentId &&
            previous.ActionContextIdentityId == current.ActionContextIdentityId &&
            previous.ActionInstanceId == current.ActionInstanceId &&
            previous.ExecutionGeneration == current.ExecutionGeneration &&
            previous.PlaybackGeneration == current.PlaybackGeneration &&
            current.Cycle == previous.Cycle + 1 &&
            default(TMath).Equal(previous.CurrentTime, previous.EndTime) &&
            default(TMath).Equal(current.PreviousTime, current.StartTime);

        static bool IsOrderedWindow<TRequest>(in TRequest previous, in TRequest current)
            where TRequest : struct, ITimelineMotionWarpArbitrationRequest<TTime> =>
            previous.ActionInstanceId == current.ActionInstanceId &&
            previous.ExecutionGeneration == current.ExecutionGeneration &&
            previous.PlaybackGeneration == current.PlaybackGeneration &&
            previous.Cycle == current.Cycle &&
            !default(TMath).GreaterThan(previous.CurrentTime, current.PreviousTime);
    }
}
