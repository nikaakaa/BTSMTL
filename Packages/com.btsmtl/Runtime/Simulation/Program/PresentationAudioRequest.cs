using System;

namespace ThirdPersonSimulation
{
    public enum PresentationAudioCommand : byte
    {
        Play = 1,
        Stop = 2,
        SetParameter = 3,
        ExecuteRule = 4,
        StopResource = 5
    }

    public enum PresentationAudioLifetime : byte
    {
        OneShot = 1,
        Action = 2
    }

    public enum PresentationAudioTarget : byte
    {
        Owner = 1,
        ActionTarget = 2
    }

    public readonly struct PresentationAudioRequest
    {
        public PresentationAudioRequest(PresentationAudioCommand command, int resourceSlot,
            PresentationAudioTarget target, PresentationAudioLifetime lifetime,
            int soundSlot, int parameterSlot, float parameterValue)
        {
            Command = command;
            ResourceSlot = resourceSlot;
            Target = target;
            Lifetime = lifetime;
            SoundSlot = soundSlot;
            ParameterSlot = parameterSlot;
            ParameterValue = parameterValue;
        }

        public PresentationAudioCommand Command { get; }
        public int ResourceSlot { get; }
        public PresentationAudioTarget Target { get; }
        public PresentationAudioLifetime Lifetime { get; }
        public int SoundSlot { get; }
        public int ParameterSlot { get; }
        public float ParameterValue { get; }

        public static PresentationAudioRequest PlayOneShot(int resourceSlot, PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.Play, resourceSlot, target,
                PresentationAudioLifetime.OneShot, -1, -1, 0f);

        public static PresentationAudioRequest PlayAction(int resourceSlot, int soundSlot,
            PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.Play, resourceSlot, target,
                PresentationAudioLifetime.Action, soundSlot, -1, 0f);

        public static PresentationAudioRequest ExecuteRule(int ruleSlot, PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.ExecuteRule, ruleSlot, target,
                PresentationAudioLifetime.OneShot, -1, -1, 0f);

        public static PresentationAudioRequest Stop(int soundSlot, PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.Stop, -1, target,
                PresentationAudioLifetime.Action, soundSlot, -1, 0f);

        public static PresentationAudioRequest StopResource(int resourceSlot, PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.StopResource, resourceSlot, target,
                PresentationAudioLifetime.OneShot, -1, -1, 0f);

        public static PresentationAudioRequest SetParameter(int soundSlot, int parameterSlot,
            float value, PresentationAudioTarget target) =>
            new PresentationAudioRequest(PresentationAudioCommand.SetParameter, -1, target,
                PresentationAudioLifetime.Action, soundSlot, parameterSlot, value);
    }

    public readonly struct AudioPlaybackOwner : IEquatable<AudioPlaybackOwner>
    {
        public AudioPlaybackOwner(ActorId actorId, ulong actionInstanceId,
            ulong timelineRuntimeHandle, ulong playbackGeneration)
        {
            ActorId = actorId;
            ActionInstanceId = actionInstanceId;
            TimelineRuntimeHandle = timelineRuntimeHandle;
            PlaybackGeneration = playbackGeneration;
        }

        public ActorId ActorId { get; }
        public ulong ActionInstanceId { get; }
        public ulong TimelineRuntimeHandle { get; }
        public ulong PlaybackGeneration { get; }

        public bool Equals(AudioPlaybackOwner other) => ActorId == other.ActorId &&
            ActionInstanceId == other.ActionInstanceId &&
            TimelineRuntimeHandle == other.TimelineRuntimeHandle &&
            PlaybackGeneration == other.PlaybackGeneration;

        public override bool Equals(object obj) => obj is AudioPlaybackOwner other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(ActorId, ActionInstanceId, TimelineRuntimeHandle, PlaybackGeneration);
    }
}
