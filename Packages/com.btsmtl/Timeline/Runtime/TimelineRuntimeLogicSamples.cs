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
        int InputValueCapacity { get; }

        void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            Span<TValue> inputValues, ReadOnlySpan<OrchestrationStateAncestor> stateAncestors, ref TContext context);
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
        readonly int[] m_ClipIndices;
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
            var clipIndices = new List<int>();
            for (int clipIndex = 0; clipIndex < content.Clips.Count; clipIndex++)
            {
                TimelineContentClip clip = content.Clips[clipIndex];
                if (clip.TrackMuted || !clip.ExecutionPolicy.IsLogic || clip.ContentKind != TimelineContentKind.Sampled)
                    continue;
                TimelineContentTrack track = tracks[clip.TrackAuthoringId];
                TimelineLogicSampleExtension extension = track.LogicSampleExtension.Value;
                references.Add(extension.CreateReference(clipIndex,
                    $"Timeline '{content.Identity}'，轨道 '{track.AuthoringId}'，片段 '{clip.AuthoringId}' ({clip.ContractKind})"));
                clipIndices.Add(clipIndex);
            }
            m_ClipIndices = clipIndices.ToArray();
            var required = new int[references.Count];
            for (int index = 0; index < required.Length; index++)
                required[index] = index;
            m_Bindings = OrchestrationExtensionBindings<TValue, TContext>.Prepare(references.ToArray(), required,
                implementations, (bindingIndex, implementation) =>
                {
                    TimelineContentClip clip = content.Clips[m_ClipIndices[bindingIndex]];
                    if (implementation is not ITimelineLogicSampleExtension<TScalar> preparation)
                        throw new InvalidOperationException($"Timeline 片段 '{clip.AuthoringId}' ({clip.ContractKind}) 的扩展未提供采样参数准备接口。");
                    return preparation.PrepareSample(in clip, m_ClipIndices[bindingIndex], m_Sampling);
                });
            int maximumIntervals = mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
            InputValueCapacity = m_ClipIndices.Length == 0 ? 0 :
                checked(maximumIntervals * TimelineLogicSampleExtension.InputCount);
        }

        public int InputValueCapacity { get; }

        public void Invoke(int operationIndex, ulong operationGeneration, ulong tick, int playbackSlot, ulong playbackIdentity,
            OrchestrationTime previousTime, int previousCycle, OrchestrationTime time, int cycle,
            Span<TValue> inputValues, ReadOnlySpan<OrchestrationStateAncestor> stateAncestors, ref TContext context)
        {
            TValues values = default;
            TProjection projection = default;
            TValue previousTimeValue = values.FromScalar(projection.Project(previousTime));
            TValue timeValue = values.FromScalar(projection.Project(time));
            int intervalCount = checked(cycle - previousCycle + 1);
            for (int bindingIndex = 0; bindingIndex < m_ClipIndices.Length; bindingIndex++)
            {
                int clipIndex = m_ClipIndices[bindingIndex];
                TimelineContentClip clip = m_Content.Clips[clipIndex];
                int count = 0;
                for (int traversal = previousCycle; traversal <= cycle; traversal++)
                {
                    OrchestrationTime from = traversal == previousCycle ? previousTime : OrchestrationTime.Zero;
                    OrchestrationTime to = traversal == cycle ? time : m_Content.Duration;
                    if (clip.Sampling.Window == TimelineSampleWindow.TraversalWindow)
                    {
                        if (clip.StartTime > to || clip.EndTime <= from)
                            continue;
                    }
                    else if (traversal != cycle || clip.StartTime > to ||
                        clip.EndTime < to && clip.Sampling.Extrapolation != ExtraPolationMode.Hold)
                        continue;
                    if (!m_Sampling.TrySample(in clip, clipIndex, from, to, traversal,
                        TimelinePresentationSampleReason.Advance, out TimelineRuntimeSample<TScalar> sample))
                        continue;
                    Span<TValue> inputs = inputValues.Slice(count * TimelineLogicSampleExtension.InputCount,
                        TimelineLogicSampleExtension.InputCount);
                    inputs[TimelineLogicSampleExtension.Ports.Weight.Index] = values.FromScalar(sample.Weight);
                    inputs[TimelineLogicSampleExtension.Ports.PreviousSourceTime.Index] = values.FromScalar(sample.PreviousSourceTime);
                    inputs[TimelineLogicSampleExtension.Ports.SourceTime.Index] = values.FromScalar(sample.SourceTime);
                    inputs[TimelineLogicSampleExtension.Ports.NormalizedTime.Index] = values.FromScalar(sample.NormalizedTime);
                    inputs[TimelineLogicSampleExtension.Ports.Cycle.Index] = values.FromInt32(sample.Cycle);
                    inputs[TimelineLogicSampleExtension.Ports.PlaybackIdentity.Index] = values.FromUInt64(playbackIdentity);
                    inputs[TimelineLogicSampleExtension.Ports.PlaybackSlot.Index] = values.FromInt32(playbackSlot);
                    inputs[TimelineLogicSampleExtension.Ports.AdvancePreviousTime.Index] = previousTimeValue;
                    inputs[TimelineLogicSampleExtension.Ports.AdvancePreviousCycle.Index] = values.FromInt32(previousCycle);
                    inputs[TimelineLogicSampleExtension.Ports.AdvanceTime.Index] = timeValue;
                    inputs[TimelineLogicSampleExtension.Ports.AdvanceCycle.Index] = values.FromInt32(cycle);
                    count++;
                }
                if (count == 0)
                    continue;
                var rows = new OrchestrationExtensionInputRows<TValue>(
                    inputValues.Slice(0, count * TimelineLogicSampleExtension.InputCount),
                    TimelineLogicSampleExtension.InputCount, count, intervalCount);
                var invocation = new OrchestrationExtensionInvocation<TValue>(
                    OrchestrationExtensionInvocationKind.Execute, operationIndex, operationGeneration, tick, 0,
                    stateAncestors, rows, Span<TValue>.Empty, default);
                OrchestrationExtensionResult result = m_Bindings.InvokeCandidate(bindingIndex, ref context, in invocation);
                if (result != OrchestrationExtensionResult.Success)
                    throw new InvalidOperationException($"Timeline 片段 '{clip.AuthoringId}' ({clip.ContractKind}) 的同次采样返回 '{result}'。");
            }
        }
    }
}
