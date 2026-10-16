using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum GameplayEffectScopeKind : byte
    {
        Actor = 0,
        ActionInstance = 1
    }

    [ValueOperations]
    public readonly partial struct GameplayEffectScope : IEquatable<GameplayEffectScope>
    {
        GameplayEffectScope(ulong actionInstanceId, ulong executionGeneration)
        {
            ActionInstanceId = actionInstanceId;
            ExecutionGeneration = executionGeneration;
        }

        public static GameplayEffectScope Actor => default;
        public static GameplayEffectScope Action(ulong actionInstanceId, ulong executionGeneration) =>
            new GameplayEffectScope(actionInstanceId, executionGeneration);
        public static GameplayEffectScope Action(in GameplayAbilitySourceContext source) =>
            Action(source.InstanceId, source.SkillExecutionGeneration);
        public GameplayEffectScopeKind Kind => ActionInstanceId == 0
            ? GameplayEffectScopeKind.Actor : GameplayEffectScopeKind.ActionInstance;
        public ulong ActionInstanceId { get; }
        public ulong ExecutionGeneration { get; }
        public bool Equals(GameplayEffectScope other) => ActionInstanceId == other.ActionInstanceId &&
            ExecutionGeneration == other.ExecutionGeneration;
        public override bool Equals(object value) => value is GameplayEffectScope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActionInstanceId, ExecutionGeneration);
    }

    public enum GameplayEffectLifetimeKind : byte
    {
        Independent = 0,
        Action = 1,
        Invocation = 2
    }

    public enum GameplayEffectLifecycleKind : byte
    {
        Applied = 1,
        Confirmed = 2,
        Rejected = 3,
        StackChanged = 4,
        Inhibited = 5,
        Resumed = 6,
        PeriodExecuted = 7,
        Removed = 8,
        Expired = 9,
        Corrected = 10,
        Overflow = 11
    }

    internal enum GameplayEffectRemoveSelector : byte
    {
        Handle = 0,
        EffectId = 1,
        SourceActor = 2,
        EffectTagQuery = 3,
        Holder = 4,
        HeldByAction = 5
    }

}
