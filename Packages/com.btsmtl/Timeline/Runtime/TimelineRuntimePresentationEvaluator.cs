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
            var animations = buffer.Animations;
            var cameraStates = buffer.CameraStates;
            var cameraResponses = buffer.CameraResponses;
            var cameraResources = buffer.CameraResources;
            var scenePresentation = buffer.ScenePresentation;
            FixedScalar currentTime = currentPosition;
            var selected = buffer.SamplingClips;
            TimelineRuntimeClipIndex animationIndex = playback.Index.Presentation.Animations;
            selected.Clear();
            animationIndex.Select(currentTime, currentTime, true, selected);
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                TimelineRuntimeIndexedClip indexed = animationIndex[selected[selectedIndex]];
                playback.Preparation.ClipSampling.SampleAnimation(indexed, content, currentTime, loop, currentCycle, animations);
            }
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


    }
}
