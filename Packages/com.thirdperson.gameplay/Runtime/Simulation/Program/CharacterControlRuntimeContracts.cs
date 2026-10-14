using System;
using Photon.Deterministic;

namespace ThirdPersonSimulation
{
    public readonly struct CharacterControlTickContext
    {
        public CharacterControlTickContext(ActorId actorId, SimulationTick tick, int tickRate, FP timeScale)
        {
            if (!actorId.IsValid || !tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Character control tick context is incomplete.");
            ActorId = actorId;
            Tick = tick;
            TickRate = tickRate;
            TimeScale = timeScale;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public int TickRate { get; }
        public FP TimeScale { get; }
    }

    public interface ICommittedCharacterControlState
    {
        CharacterControlRuntimeState ControlState { get; }
        bool IsAbilityActive(CharacterSkillId abilityId);
        System.Collections.Generic.IReadOnlyList<AbilityTimelineRuntimeSnapshot> TimelineSnapshots { get; }
    }

    public readonly struct CharacterControlDecision
    {
        public CharacterControlDecision(bool hasMoveInput, CharacterLocomotionMode locomotionMode)
        {
            HasMoveInput = hasMoveInput;
            LocomotionMode = locomotionMode;
        }
        public bool HasMoveInput { get; }
        public CharacterLocomotionMode LocomotionMode { get; }
    }

    public interface ICharacterControlModule
    {
        CharacterControlModuleContract Contract { get; }

        CharacterControlDecision ReadDecision(ICharacterControlReadPort read,
            ICharacterControlStateReadPort state);

        void PrepareDecision(
            in CharacterControlTickContext context,
            ICharacterControlReadPort read,
            ICharacterControlStatePort state,
            ICharacterControlOutputPort output);

        bool HasMoveInput { get; }
        CharacterLocomotionMode LocomotionMode { get; }
        void Tick(bool blockMovingTurn);
        void ApplyMovementIntent(bool continueRunning);
    }

    public interface ICharacterControlReadPort
    {
        bool CompareInputVector2Magnitude(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison);
        bool CompareInputDirectionToBodyYaw(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison);
    }

    public interface ICharacterControlStateReadPort
    {
        CharacterControlStateId ReadState(CharacterControlStateFieldId field);
        CharacterControlTransitionId ReadTransition(CharacterControlStateFieldId field);
        bool ReadBoolean(CharacterControlStateFieldId field);
        int ReadInt32(CharacterControlStateFieldId field);
        ulong ReadUInt64(CharacterControlStateFieldId field);
        FP ReadNumber(CharacterControlStateFieldId field);
    }

    public interface ICharacterControlStatePort : ICharacterControlStateReadPort
    {
        void WriteState(CharacterControlStateFieldId field, CharacterControlStateId value);
        void WriteTransition(CharacterControlStateFieldId field, CharacterControlTransitionId value);
        void WriteBoolean(CharacterControlStateFieldId field, bool value);
        void WriteInt32(CharacterControlStateFieldId field, int value);
        void WriteUInt64(CharacterControlStateFieldId field, ulong value);
        void WriteNumber(CharacterControlStateFieldId field, FP value);
    }

    public readonly struct CharacterControlMotionRequest
    {
        public CharacterControlMotionRequest(
            SimulationExecutionSource source,
            string sourceIdentity,
            string binding,
            SimulationInputValueId input,
            FP continuousTicks,
            ulong playbackGeneration)
        {
            if (!source.IsCharacterControl || string.IsNullOrEmpty(binding) || !input.IsValid || continuousTicks < 0)
                throw new ArgumentException("Character control motion request is incomplete.");
            if (playbackGeneration == 0)
                throw new ArgumentException("Character control motion request playback generation is invalid.", nameof(playbackGeneration));
            Source = source;
            SourceIdentity = string.IsNullOrEmpty(sourceIdentity)
                ? throw new ArgumentException("Character control motion request source identity is incomplete.", nameof(sourceIdentity))
                : sourceIdentity;
            Binding = SimulationIdentity.Require(binding, nameof(binding));
            Input = input;
            ContinuousTicks = continuousTicks;
            PlaybackGeneration = playbackGeneration;
        }

        public SimulationExecutionSource Source { get; }
        public string SourceIdentity { get; }
        public string Binding { get; }
        public SimulationInputValueId Input { get; }
        public FP ContinuousTicks { get; }
        public ulong PlaybackGeneration { get; }
    }

    public enum CharacterControlTraceKind : byte
    {
        None, StateEntered, StateExited, TransitionEvaluated, TransitionSelected
    }

    public readonly struct CharacterControlTraceDetail
    {
        public CharacterControlTraceDetail(CharacterControlTraceKind kind,
            CharacterControlStateId target = default, bool passed = false)
        { Kind = kind; Target = target; Passed = passed; }
        public readonly CharacterControlTraceKind Kind;
        public readonly CharacterControlStateId Target;
        public readonly bool Passed;
    }

    public interface ICharacterControlOutputPort
    {
        bool TraceEnabled { get; }
        void SubmitMotion(CharacterControlMotionRequest request);
        void Trace(SimulationExecutionSource source, string code, CanonicalTextValue detail, ulong generation, in CharacterControlTraceDetail controlDetail = default);
    }
}
