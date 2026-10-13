using System;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline.Runtime
{
    internal static class TimelineRuntimePresentationEvaluator
    {
        public static TimelineRuntimePresentationOperations Evaluate(
            TimelineRuntimePlayback playback,
            FixedScalar currentPosition,
            int currentCycle,
            bool loop,
            TimelineRuntimePresentationBuffer buffer)
        {
            if (playback == null)
                throw new ArgumentNullException(nameof(playback));
            TimelineContentUnit content = playback.Content;
            var cameraStates = buffer.CameraStates;
            var cameraResponses = buffer.CameraResponses;
            var cameraResources = buffer.CameraResources;
            var scenePresentation = buffer.ScenePresentation;
            FixedScalar currentTime = currentPosition;
            var selected = buffer.SamplingClips;
            SampleAnimations(content, playback.Index, playback.Preparation.ClipSampling,
                currentTime, currentCycle, loop, buffer);
            selected.Clear();
            TimelineRuntimeClipIndex cameraIndex = playback.Index.Presentation.CameraAndScene;
            cameraIndex.Select(currentTime, currentTime, true, selected);
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                playback.Preparation.ClipSampling.SampleCameraAndScene(cameraIndex[selected[selectedIndex]], content,
                    currentTime, currentCycle, playback.ExecutionIdentity, playback.Generation,
                    cameraStates, cameraResponses, cameraResources, scenePresentation);
            }
            return new TimelineRuntimePresentationOperations(buffer);
        }

        internal static void SampleAnimations(TimelineContentUnit content,
            TimelineRuntimeContentIndex index, TimelineRuntimeClipSampling sampling,
            FixedScalar time, int cycle, bool loop, TimelineRuntimePresentationBuffer buffer)
        {
            var selected = buffer.SamplingClips;
            TimelineRuntimeClipIndex animations = index.Presentation.Animations;
            selected.Clear();
            animations.Select(time, time, true, selected);
            for (int i = 0; i < selected.Count; i++)
                sampling.SampleAnimation(animations[selected[i]], content, time, loop, cycle, buffer.Animations);
        }

    }
}
