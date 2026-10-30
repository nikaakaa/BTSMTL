using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public interface ITimelineRuntimeLogicSamples<TValue, TContext>
        where TValue : unmanaged
        where TContext : struct
    {
        void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame, ref TContext context);
    }

    public interface ITimelineLogicSampleInvoker<TValue, TContext>
        where TValue : unmanaged
        where TContext : struct
    {
        OrchestrationExtensionResult Invoke(int bindingIndex, ref TContext context,
            in OrchestrationExtensionArguments<TValue> invocation);
    }

    public readonly struct TimelineRuntimeLogicSampleClip<TScalar> where TScalar : unmanaged
    {
        public TimelineRuntimeLogicSampleClip(int clipIndex, int kindIndex, TimelineClipTimeRange timeRange,
            TimelineContentSampling sampling, TimelineRuntimeSampleCurves<TScalar> curves)
        {
            ClipIndex = clipIndex;
            KindIndex = kindIndex;
            TimeRange = timeRange;
            Sampling = sampling;
            Curves = curves;
        }

        public int ClipIndex { get; }
        public int KindIndex { get; }
        public TimelineClipTimeRange TimeRange { get; }
        public TimelineContentSampling Sampling { get; }
        public TimelineRuntimeSampleCurves<TScalar> Curves { get; }
    }

    // 逻辑采样核心：只读纯值片段记录与曲线键，扩展调用经结构体插头，可进入 Burst。
    public static class TimelineRuntimeLogicSampling<TValue, TScalar, TMath, TValues, TProjection>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TMath : struct, IScalarMath<TScalar>
        where TValues : struct, IOrchestrationScalarValueAccess<TValue, TScalar>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
    {
        public static int InputValueCapacity(int clipCount, OrchestrationTimelinePlaybackMode mode) =>
            clipCount == 0 ? 0 : checked((mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1) * TimelineLogicSampleExtension.InputCount);

        // 返回失败的绑定序号；全部成功时返回 -1。
        public static int Invoke<TContext, TInvoker>(ReadOnlySpan<TimelineRuntimeLogicSampleClip<TScalar>> clips,
            ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> keys, OrchestrationTime duration,
            Span<TValue> inputValues, ref TInvoker invoker,
            int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame, ref TContext context,
            out OrchestrationExtensionResult failure)
            where TContext : struct
            where TInvoker : struct, ITimelineLogicSampleInvoker<TValue, TContext>
        {
            TValues values = default;
            TProjection projection = default;
            TValue previousTimeValue = values.FromScalar(projection.Project(previousTime));
            TValue timeValue = values.FromScalar(projection.Project(time));
            int intervalCount = checked(cycle - previousCycle + 1);
            for (int bindingIndex = 0; bindingIndex < clips.Length; bindingIndex++)
            {
                ref readonly TimelineRuntimeLogicSampleClip<TScalar> clip = ref clips[bindingIndex];
                TimelineClipTimeRange range = clip.TimeRange;
                TimelineContentSampling sampling = clip.Sampling;
                TimelineRuntimeSampleCurves<TScalar> curves = clip.Curves;
                int count = 0;
                for (int traversal = previousCycle; traversal <= cycle; traversal++)
                {
                    OrchestrationTime from = traversal == previousCycle ? previousTime : OrchestrationTime.Zero;
                    OrchestrationTime to = traversal == cycle ? time : duration;
                    if (sampling.Window == TimelineSampleWindow.TraversalWindow)
                    {
                        if (range.Start > to || range.End <= from)
                            continue;
                    }
                    else if (traversal != cycle || range.Start > to ||
                        range.End < to && sampling.Extrapolation != ExtraPolationMode.Hold)
                        continue;
                    if (!TimelineRuntimeClipSampler<TScalar, TMath, TProjection>.TrySample(in range, in sampling,
                        in curves, keys, clip.ClipIndex, clip.KindIndex, from, to, traversal,
                        TimelinePresentationSampleReason.Advance, out TimelineRuntimeSample<TScalar> sample))
                        continue;
                    Span<TValue> inputs = inputValues.Slice(count * TimelineLogicSampleExtension.InputCount,
                        TimelineLogicSampleExtension.InputCount);
                    inputs[TimelineLogicSampleExtension.InputIndex.Weight] = values.FromScalar(sample.Weight);
                    inputs[TimelineLogicSampleExtension.InputIndex.PreviousSourceTime] = values.FromScalar(sample.PreviousSourceTime);
                    inputs[TimelineLogicSampleExtension.InputIndex.SourceTime] = values.FromScalar(sample.SourceTime);
                    inputs[TimelineLogicSampleExtension.InputIndex.NormalizedTime] = values.FromScalar(sample.NormalizedTime);
                    inputs[TimelineLogicSampleExtension.InputIndex.Cycle] = values.FromInt32(sample.Cycle);
                    inputs[TimelineLogicSampleExtension.InputIndex.PlaybackIdentity] = values.FromUInt64(playbackIdentity);
                    inputs[TimelineLogicSampleExtension.InputIndex.PlaybackSlot] = values.FromInt32(playbackSlot);
                    inputs[TimelineLogicSampleExtension.InputIndex.AdvancePreviousTime] = previousTimeValue;
                    inputs[TimelineLogicSampleExtension.InputIndex.AdvancePreviousCycle] = values.FromInt32(previousCycle);
                    inputs[TimelineLogicSampleExtension.InputIndex.AdvanceTime] = timeValue;
                    inputs[TimelineLogicSampleExtension.InputIndex.AdvanceCycle] = values.FromInt32(cycle);
                    count++;
                }
                if (count == 0)
                    continue;
                var rows = new OrchestrationExtensionInputRows<TValue>(
                    inputValues.Slice(0, count * TimelineLogicSampleExtension.InputCount),
                    TimelineLogicSampleExtension.InputCount, count, intervalCount);
                var invocation = new OrchestrationExtensionArguments<TValue>(
                    OrchestrationExtensionInvocationKind.Execute, operationIndex, operationGeneration, tick, 0,
                    stateAncestors, executionFrame, rows, Span<TValue>.Empty, default);
                OrchestrationExtensionResult result = invoker.Invoke(bindingIndex, ref context, in invocation);
                if (result != OrchestrationExtensionResult.Success)
                {
                    failure = result;
                    return bindingIndex;
                }
            }
            failure = OrchestrationExtensionResult.Success;
            return -1;
        }
    }

    public sealed class TimelineRuntimeLogicSamples<TValue, TScalar, TMath, TLowering, TValues, TProjection, TContext> :
        ITimelineRuntimeLogicSamples<TValue, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
        where TLowering : struct, IScalarContentLowering<TScalar>
        where TValues : struct, IOrchestrationScalarValueAccess<TValue, TScalar>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
        where TContext : struct
    {
        readonly TimelineContentUnit m_Content;
        readonly TimelineRuntimeLogicSampleClip<TScalar>[] m_Clips;
        readonly GameplayAbilityCurveKey<TScalar>[] m_CurveKeys;
        readonly TValue[] m_Inputs;
        readonly TimelineRuntimeClipSampling<TScalar, TMath, TLowering, TProjection> m_Sampling;
        readonly OrchestrationExtensionBindings<TValue, TContext> m_Bindings;

        public TimelineRuntimeLogicSamples(TimelineContentUnit content, OrchestrationTimelinePlaybackMode mode,
            IReadOnlyList<IOrchestrationExtension> implementations)
        {
            m_Content = content;
            m_Sampling = new TimelineRuntimeClipSampling<TScalar, TMath, TLowering, TProjection>(content,
                TimelineExecutionDomain.Logic);
            var tracks = new Dictionary<string, TimelineContentTrack>(content.Tracks.Count, StringComparer.Ordinal);
            foreach (TimelineContentTrack track in content.Tracks)
                tracks.Add(track.AuthoringId, track);
            var references = new List<OrchestrationExtensionReference>();
            var clips = new List<TimelineRuntimeLogicSampleClip<TScalar>>();
            for (int clipIndex = 0; clipIndex < content.Clips.Count; clipIndex++)
            {
                TimelineContentClip clip = content.Clips[clipIndex];
                if (clip.TrackMuted || !clip.ExecutionPolicy.IsLogic || clip.ContentKind != TimelineContentKind.Sampled)
                    continue;
                TimelineContentTrack track = tracks[clip.TrackAuthoringId];
                TimelineLogicSampleExtension extension = track.LogicSampleExtension.Value;
                references.Add(extension.CreateReference(clipIndex,
                    $"Timeline '{content.Identity}'，轨道 '{track.AuthoringId}'，片段 '{clip.AuthoringId}' ({clip.ContractKind})"));
                clips.Add(new TimelineRuntimeLogicSampleClip<TScalar>(clipIndex, m_Sampling.KindIndexAt(clipIndex),
                    clip.TimeRange, clip.Sampling, m_Sampling.CurvesAt(clipIndex)));
            }
            m_Clips = clips.ToArray();
            m_CurveKeys = m_Sampling.CurveKeys.ToArray();
            var required = new int[references.Count];
            for (int index = 0; index < required.Length; index++)
                required[index] = index;
            m_Bindings = OrchestrationExtensionBindings<TValue, TContext>.Prepare(references.ToArray(), required,
                implementations, (bindingIndex, implementation) =>
                {
                    int clipIndex = m_Clips[bindingIndex].ClipIndex;
                    TimelineContentClip clip = content.Clips[clipIndex];
                    if (implementation is not ITimelineLogicSampleExtension<TScalar> preparation)
                        throw new InvalidOperationException($"Timeline 片段 '{clip.AuthoringId}' ({clip.ContractKind}) 的扩展未提供采样参数准备接口。");
                    return preparation.PrepareSample(in clip, clipIndex, m_Sampling);
                });
            m_Inputs = new TValue[TimelineRuntimeLogicSampling<TValue, TScalar, TMath, TValues, TProjection>
                .InputValueCapacity(m_Clips.Length, mode)];
        }

        public ReadOnlySpan<TimelineRuntimeLogicSampleClip<TScalar>> Clips => m_Clips;
        public ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> CurveKeys => m_CurveKeys;

        public void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame, ref TContext context)
        {
            var invoker = new Invoker(m_Bindings);
            int failed = TimelineRuntimeLogicSampling<TValue, TScalar, TMath, TValues, TProjection>.Invoke(
                m_Clips, m_CurveKeys, m_Content.Duration, m_Inputs, ref invoker, operationIndex, operationGeneration,
                tick, playbackSlot, playbackIdentity, previousTime, previousCycle, time, cycle, stateAncestors,
                executionFrame, ref context, out OrchestrationExtensionResult result);
            if (failed >= 0)
            {
                TimelineContentClip clip = m_Content.Clips[m_Clips[failed].ClipIndex];
                throw new InvalidOperationException($"Timeline 片段 '{clip.AuthoringId}' ({clip.ContractKind}) 的同次采样返回 '{result}'。");
            }
        }

        readonly struct Invoker : ITimelineLogicSampleInvoker<TValue, TContext>
        {
            readonly OrchestrationExtensionBindings<TValue, TContext> m_Bindings;

            internal Invoker(OrchestrationExtensionBindings<TValue, TContext> bindings) => m_Bindings = bindings;

            public OrchestrationExtensionResult Invoke(int bindingIndex, ref TContext context,
                in OrchestrationExtensionArguments<TValue> invocation) =>
                m_Bindings.InvokeCandidate(bindingIndex, ref context, in invocation);
        }
    }
}
