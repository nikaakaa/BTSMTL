using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    internal enum PortableEffectChangeKind : byte
    {
        Lifecycle = 1,
        Attribute = 2,
        Cue = 3,
        Failure = 4,
        Reconciliation = 5,
        Tag = 6
    }

    internal static class PortableCueTriggerText
    {
        internal static string Identity(PortableCueTrigger trigger) => trigger switch
        {
            PortableCueTrigger.OnActive => nameof(PortableCueTrigger.OnActive),
            PortableCueTrigger.WhileActive => nameof(PortableCueTrigger.WhileActive),
            PortableCueTrigger.Executed => nameof(PortableCueTrigger.Executed),
            PortableCueTrigger.Removed => nameof(PortableCueTrigger.Removed),
            PortableCueTrigger.Expired => nameof(PortableCueTrigger.Expired),
            _ => throw new ArgumentOutOfRangeException(nameof(trigger))
        };
    }

    internal readonly struct PortableEffectRuntimeChange
    {
        PortableEffectRuntimeChange(PortableEffectChangeKind kind, ulong cursor)
            : this()
        {
            Kind = kind;
            Cursor = cursor;
        }

        public PortableEffectRuntimeChange(
            ulong cursor,
            PortableEffectDefinition definition,
            ulong instanceId,
            SimulationGameplayEffectLifecycleOperation operation,
            SimulationGameplayEffectContext context,
            ulong startTick,
            ulong endTick,
            int stackCount,
            ulong lifecycleRevision,
            bool instant)
            : this(PortableEffectChangeKind.Lifecycle, cursor)
        {
            Definition = definition;
            InstanceId = instanceId;
            Operation = operation;
            Context = context;
            StartTick = startTick;
            EndTick = endTick;
            StackCount = stackCount;
            LifecycleRevision = lifecycleRevision;
            Instant = instant;
        }

        public PortableEffectRuntimeChange(
            ulong cursor,
            PortableAttributeChange value,
            CanonicalTextValue causeEffectId,
            ulong causeInstanceId,
            SimulationGameplayEffectContext causeContext)
            : this(PortableEffectChangeKind.Attribute, cursor)
        {
            Value = value;
            CauseEffectId = causeEffectId;
            InstanceId = causeInstanceId;
            Context = causeContext;
        }

        public PortableEffectRuntimeChange(ulong cursor, string cueId, PortableCueTrigger trigger, PortableEffectDefinition definition, ulong instanceId, SimulationGameplayEffectContext context)
            : this(PortableEffectChangeKind.Cue, cursor)
        {
            CueId = cueId;
            Trigger = trigger;
            Definition = definition;
            InstanceId = instanceId;
            Context = context;
        }

        public PortableEffectRuntimeChange(ulong cursor, GameplayEffectExecutionFailure failure)
            : this(PortableEffectChangeKind.Failure, cursor)
        {
            InstanceId = failure.OwnerInstanceId;
            Failure = failure;
        }

        public PortableEffectRuntimeChange(ulong cursor, GameplayEffectReconciliationResult reconciliation, SimulationGameplayEffectContext context)
            : this(PortableEffectChangeKind.Reconciliation, cursor)
        {
            InstanceId = reconciliation.AuthoritativeInstanceId;
            Context = context;
            Reconciliation = reconciliation;
        }

        public PortableEffectRuntimeChange(ulong cursor, GameplayTagRuntimeChange tag)
            : this(PortableEffectChangeKind.Tag, cursor)
        {
            InstanceId = tag.CauseInstanceId;
            Tag = tag;
        }

        public PortableEffectChangeKind Kind { get; }
        public ulong Cursor { get; }
        public PortableEffectDefinition Definition { get; }
        public ulong InstanceId { get; }
        public SimulationGameplayEffectContext Context { get; }
        public SimulationGameplayEffectLifecycleOperation Operation { get; }
        public ulong StartTick { get; }
        public ulong EndTick { get; }
        public int StackCount { get; }
        public ulong LifecycleRevision { get; }
        public bool Instant { get; }
        public PortableAttributeChange Value { get; }
        public CanonicalTextValue CauseEffectId { get; }
        public string CueId { get; }
        public PortableCueTrigger Trigger { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public GameplayEffectReconciliationResult Reconciliation { get; }
        public GameplayTagRuntimeChange Tag { get; }
        internal void Retain()
        {
            Context.Retain(); CauseEffectId.Retain(); Failure.Retain(); Reconciliation.Retain(); Tag.Retain();
        }
        internal void Release()
        {
            Context.Release(); CauseEffectId.Release(); Failure.Release(); Reconciliation.Release(); Tag.Release();
        }
    }

    internal readonly struct PortableEffectCause
    {
        public PortableEffectCause(PortableEffectDefinition definition, ulong instanceId, SimulationGameplayEffectContext context)
        {
            Definition = definition;
            InstanceId = instanceId;
            Context = context;
        }

        public PortableEffectDefinition Definition { get; }
        public ulong InstanceId { get; }
        public SimulationGameplayEffectContext Context { get; }
    }

}

