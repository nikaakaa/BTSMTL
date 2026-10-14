using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public enum TimelineRuntimePlaybackState : byte
    {
        Unprepared = 0,
        Prepared = 1,
        Running = 2,
        Stopping = 3,
        Completed = 4,
        Stopped = 5,
        Failed = 6,
        Disposed = 7
    }

    [ValueOperations]
    public readonly partial struct TimelineExecutionIdentity : IEquatable<TimelineExecutionIdentity>
    {
        public TimelineExecutionIdentity(CanonicalTextValue ownerIdentity, CanonicalTextValue callIdentity, ulong instanceId)
        {
            OwnerIdentity = ownerIdentity.Trim();
            CallIdentity = callIdentity.Trim();
            InstanceId = instanceId;
            RequireValid();
        }

        public CanonicalTextValue OwnerIdentity { get; }
        public CanonicalTextValue CallIdentity { get; }
        public ulong InstanceId { get; }
        [ValueValidation]
        void RequireValid()
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(OwnerIdentity))
                throw new ArgumentException("Timeline identity is required.", "ownerIdentity");
            if (CanonicalTextValue.IsNullOrWhiteSpace(CallIdentity))
                throw new ArgumentException("Timeline identity is required.", "callIdentity");
            if (InstanceId == 0)
                throw new ArgumentOutOfRangeException("instanceId");
        }
        public bool IsValid => !CanonicalTextValue.IsNullOrEmpty(OwnerIdentity) &&
            !CanonicalTextValue.IsNullOrEmpty(CallIdentity) &&
            InstanceId != 0;
        public bool Equals(TimelineExecutionIdentity other) =>
            OwnerIdentity.Equals(other.OwnerIdentity) &&
            CallIdentity.Equals(other.CallIdentity) &&
            InstanceId == other.InstanceId;
        public override bool Equals(object obj) => obj is TimelineExecutionIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OwnerIdentity, CallIdentity, InstanceId);
        public static bool operator ==(TimelineExecutionIdentity left, TimelineExecutionIdentity right) => left.Equals(right);
        public static bool operator !=(TimelineExecutionIdentity left, TimelineExecutionIdentity right) => !left.Equals(right);
    }

    public readonly struct TimelineScenePresentationSample
    {
        public TimelineScenePresentationSample(
            OperationHandle operation,
            string targetIdentity,
            string parameterId,
            float value,
            float normalizedTime,
            TimelineExecutionIdentity executionIdentity,
            ulong generation,
            int cycle)
        {
            if (!operation.IsValid || !executionIdentity.IsValid || generation == 0 || cycle < 0 ||
                float.IsNaN(value) || float.IsInfinity(value) ||
                float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
                throw new ArgumentException("Timeline scene presentation sample is incomplete.");
            Operation = operation;
            if (string.IsNullOrWhiteSpace(targetIdentity))
                throw new ArgumentException("Timeline identity is required.", nameof(targetIdentity));
            if (string.IsNullOrWhiteSpace(parameterId))
                throw new ArgumentException("Timeline identity is required.", nameof(parameterId));
            TargetIdentity = targetIdentity.Trim();
            ParameterId = parameterId.Trim();
            Value = value;
            NormalizedTime = normalizedTime < 0f ? 0f : normalizedTime > 1f ? 1f : normalizedTime;
            ExecutionIdentity = executionIdentity;
            Generation = generation;
            Cycle = cycle;
        }

        public OperationHandle Operation { get; }
        public string TargetIdentity { get; }
        public string ParameterId { get; }
        public float Value { get; }
        public float NormalizedTime { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public ulong Generation { get; }
        public int Cycle { get; }
    }

    public interface ITimelineScenePresentationSink
    {
        string TargetIdentity { get; }
        void WriteScalar(TimelineScenePresentationSample sample);
        void WriteBoolean(TimelineScenePresentationSample sample);
    }

    public interface ITimelineStandaloneTraceSink
    {
        bool Enabled { get; }
        void Add(TimelineTraceOutput output);
    }

    public readonly struct TimelinePlaybackObservation
    {
        public TimelinePlaybackObservation(
            SimulationRootKind rootKind,
            string rootIdentity,
            string entryIdentity,
            string contentIdentity,
            string sourceRevision,
            string contentHash,
            string layoutHash,
            TimelineRuntimePlaybackState state,
            TimelineExecutionIdentity executionIdentity,
            IEnumerable<string> bindingIds)
        {
            RootKind = rootKind;
            RootIdentity = rootIdentity ?? string.Empty;
            EntryIdentity = entryIdentity ?? string.Empty;
            ContentIdentity = contentIdentity ?? string.Empty;
            SourceRevision = sourceRevision ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            LayoutHash = layoutHash ?? string.Empty;
            State = state;
            ExecutionIdentity = executionIdentity;
            BindingIds = new ReadOnlyCollection<string>(
                (bindingIds ?? Array.Empty<string>()).Select(value => value ?? string.Empty).ToList());
        }

        public SimulationRootKind RootKind { get; }
        public string RootIdentity { get; }
        public string EntryIdentity { get; }
        public string ContentIdentity { get; }
        public string SourceRevision { get; }
        public string ContentHash { get; }
        public string LayoutHash { get; }
        public TimelineRuntimePlaybackState State { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public IReadOnlyList<string> BindingIds { get; }
        public bool IsValid => Enum.IsDefined(typeof(SimulationRootKind), RootKind) &&
                               !string.IsNullOrEmpty(RootIdentity) &&
                               !string.IsNullOrEmpty(EntryIdentity) &&
                               !string.IsNullOrEmpty(ContentIdentity);
        public bool HasExecutionIdentity => ExecutionIdentity.IsValid;
    }

    public readonly struct TimelineBindingValue
    {
        TimelineBindingValue(
            TimelineBindingValueKind valueKind,
            string targetIdentity,
            float scalar,
            bool boolean)
        {
            ValueKind = valueKind;
            TargetIdentity = targetIdentity ?? string.Empty;
            Scalar = scalar;
            Boolean = boolean;
        }

        public TimelineBindingValueKind ValueKind { get; }
        public string TargetIdentity { get; }
        public float Scalar { get; }
        public bool Boolean { get; }

        public static TimelineBindingValue Target(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new ArgumentException("Timeline target identity is required.", nameof(identity));
            return new TimelineBindingValue(TimelineBindingValueKind.Target, identity.Trim(), 0f, false);
        }

        public static TimelineBindingValue ScalarValue(float value) =>
            new TimelineBindingValue(TimelineBindingValueKind.Scalar, string.Empty, value, false);

        public static TimelineBindingValue BooleanValue(bool value) =>
            new TimelineBindingValue(TimelineBindingValueKind.Boolean, string.Empty, 0f, value);

        public bool Matches(TimelineBindingDeclaration binding)
        {
            return ValueKind == binding.ValueKind &&
                   (ValueKind != TimelineBindingValueKind.Target || !string.IsNullOrEmpty(TargetIdentity)) &&
                   (ValueKind != TimelineBindingValueKind.Scalar || !float.IsNaN(Scalar) && !float.IsInfinity(Scalar));
        }
    }

    public readonly struct TimelineCallBinding
    {
        public TimelineCallBinding(string bindingId, TimelineBindingValue value)
        {
            BindingId = string.IsNullOrWhiteSpace(bindingId)
                ? throw new ArgumentException("Timeline call binding identity is required.", nameof(bindingId))
                : bindingId.Trim();
            Value = value;
        }

        public string BindingId { get; }
        public TimelineBindingValue Value { get; }
    }

    public readonly struct TimelineBindingHandle : IEquatable<TimelineBindingHandle>
    {
        public TimelineBindingHandle(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public int Value { get; }
        public bool IsValid => Value > 0;

        public bool Equals(TimelineBindingHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TimelineBindingHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static TimelineBindingHandle Invalid => default;
    }

    public sealed class TimelineBindingPlan
    {
        readonly ReadOnlyCollection<TimelineBindingDeclaration> m_Bindings;
        readonly Dictionary<string, int> m_Slots;
        readonly Dictionary<string, string> m_TargetBindings;

        public TimelineBindingPlan(TimelineContentUnit content,
            TimelineExecutionDomainMask domains = TimelineExecutionDomainMask.Logic | TimelineExecutionDomainMask.Presentation)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            var bindings = new List<TimelineBindingDeclaration>();
            if (domains == (TimelineExecutionDomainMask.Logic | TimelineExecutionDomainMask.Presentation))
                bindings.AddRange(content.Bindings);
            else
                for (int i = 0; i < content.Bindings.Count; i++)
                    for (int j = 0; j < content.Clips.Count; j++)
                    {
                        TimelineContentClip clip = content.Clips[j];
                        if ((TimelineExecutionDomains.ToMask(clip.ExecutionPolicy.Domain) & domains) == 0)
                            continue;
                        bool used = false;
                        for (int k = 0; k < clip.Bindings.Count; k++)
                            used |= clip.Bindings[k].BindingId == content.Bindings[i].BindingId ||
                                clip.Bindings[k].TargetBindingId == content.Bindings[i].BindingId;
                        if (used)
                        {
                            bindings.Add(content.Bindings[i]);
                            break;
                        }
                    }
            bindings.Sort((left, right) => string.CompareOrdinal(left.BindingId, right.BindingId));
            m_Bindings = bindings.AsReadOnly();
            m_Slots = new Dictionary<string, int>(StringComparer.Ordinal);
            m_TargetBindings = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (!m_Slots.TryAdd(m_Bindings[i].BindingId, i))
                    throw new InvalidOperationException($"Timeline binding '{m_Bindings[i].BindingId}' is duplicated.");
            }
            for (int clipIndex = 0; clipIndex < content.Clips.Count; clipIndex++)
            {
                if ((TimelineExecutionDomains.ToMask(content.Clips[clipIndex].ExecutionPolicy.Domain) & domains) == 0)
                    continue;
                IReadOnlyList<TimelineContentBindingUse> uses = content.Clips[clipIndex].Bindings;
                for (int useIndex = 0; useIndex < uses.Count; useIndex++)
                {
                    TimelineContentBindingUse use = uses[useIndex];
                    if (use.Access == TimelineBindingAccess.Input || string.IsNullOrEmpty(use.TargetBindingId))
                        continue;
                    if (!m_Slots.ContainsKey(use.TargetBindingId))
                        throw new InvalidOperationException($"Timeline binding '{use.BindingId}' targets undeclared binding '{use.TargetBindingId}'.");
                    if (m_TargetBindings.TryGetValue(use.BindingId, out string existing) &&
                        !string.Equals(existing, use.TargetBindingId, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Timeline binding '{use.BindingId}' targets more than one binding.");
                    m_TargetBindings[use.BindingId] = use.TargetBindingId;
                }
            }
        }

        public TimelineContentUnit Content { get; }
        public IReadOnlyList<TimelineBindingDeclaration> Bindings => m_Bindings;

        public bool TryGetTargetBindingId(string bindingId, out string targetBindingId) =>
            m_TargetBindings.TryGetValue(bindingId ?? string.Empty, out targetBindingId);

        public bool TryGetSlot(string bindingId, out int slot)
        {
            return m_Slots.TryGetValue(bindingId ?? string.Empty, out slot);
        }

        public TimelineBindingDeclaration Require(string bindingId)
        {
            return TryGetSlot(bindingId, out int slot)
                ? m_Bindings[slot]
                : throw new InvalidOperationException($"Timeline binding '{bindingId}' is not declared by '{Content.Identity}'.");
        }
    }

    public sealed class TimelineCallInput
    {
        readonly TimelineBindingValue[] m_Values;
        readonly bool[] m_Assigned;

        public TimelineCallInput(TimelineBindingPlan plan, IEnumerable<TimelineCallBinding> bindings)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            m_Values = new TimelineBindingValue[plan.Bindings.Count];
            m_Assigned = new bool[plan.Bindings.Count];
            if (bindings == null)
                return;
            foreach (TimelineCallBinding binding in bindings)
            {
                if (!plan.TryGetSlot(binding.BindingId, out int slot))
                    throw new InvalidOperationException($"Timeline call binding '{binding.BindingId}' is not declared by '{plan.Content.Identity}'.");
                TimelineBindingDeclaration declaration = plan.Bindings[slot];
                if (declaration.Access != TimelineBindingAccess.Input || declaration.Lifetime != TimelineBindingLifetime.Call)
                    throw new InvalidOperationException($"Timeline binding '{binding.BindingId}' is not a call input.");
                if (!binding.Value.Matches(declaration))
                    throw new InvalidOperationException($"Timeline call binding '{binding.BindingId}' has type '{binding.Value.ValueKind}', expected '{declaration.ValueKind}'.");
                if (m_Assigned[slot])
                    throw new InvalidOperationException($"Timeline call binding '{binding.BindingId}' is assigned more than once.");
                m_Values[slot] = binding.Value;
                m_Assigned[slot] = true;
            }
        }

        public TimelineBindingPlan Plan { get; }

        public bool TryGet(string bindingId, out TimelineBindingValue value)
        {
            if (Plan.TryGetSlot(bindingId, out int slot) && m_Assigned[slot])
            {
                value = m_Values[slot];
                return true;
            }
            value = default;
            return false;
        }

        public void ValidateRequiredInputs(List<string> errors)
        {
            for (int i = 0; i < Plan.Bindings.Count; i++)
            {
                TimelineBindingDeclaration declaration = Plan.Bindings[i];
                if (declaration.Access == TimelineBindingAccess.Input && !m_Assigned[i])
                    errors?.Add($"timeline_binding_missing:{declaration.BindingId}:call input is required");
            }
        }
    }

    public interface ITimelineDomainBindingResolver
    {
        bool TryResolve(
            TimelineBindingDeclaration declaration,
            TimelineBindingValue callValue,
            out TimelineBindingHandle handle,
            out string error);
    }

    public sealed class TimelinePreparedBindings
    {
        readonly TimelineBindingHandle[] m_Handles;

        internal TimelinePreparedBindings(
            TimelineBindingPlan plan,
            TimelineCallInput callInput,
            TimelineBindingHandle[] handles)
        {
            Plan = plan;
            CallInput = callInput;
            m_Handles = handles;
        }

        public TimelineBindingPlan Plan { get; }
        public TimelineCallInput CallInput { get; }

        public bool TryGetHandle(string bindingId, out TimelineBindingHandle handle)
        {
            if (Plan.TryGetSlot(bindingId, out int slot))
            {
                handle = m_Handles[slot];
                return handle.IsValid;
            }
            handle = TimelineBindingHandle.Invalid;
            return false;
        }
    }

    public static class TimelineBindingPreparation
    {
        public static TimelinePreparedBindings Prepare(
            TimelineBindingPlan plan,
            TimelineCallInput callInput,
            ITimelineDomainBindingResolver resolver,
            List<string> errors)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (callInput == null)
                throw new ArgumentNullException(nameof(callInput));
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));
            if (!ReferenceEquals(plan, callInput.Plan))
                throw new ArgumentException("Timeline call input belongs to a different binding plan.", nameof(callInput));
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            int initialErrorCount = errors?.Count ?? 0;
            callInput.ValidateRequiredInputs(errors);
            var handles = new TimelineBindingHandle[plan.Bindings.Count];
            for (int i = 0; i < plan.Bindings.Count; i++)
            {
                TimelineBindingDeclaration declaration = plan.Bindings[i];
                if (declaration.Access == TimelineBindingAccess.Input)
                    continue;
                TimelineBindingValue callValue = default;
                if (plan.TryGetTargetBindingId(declaration.BindingId, out string targetBindingId) &&
                    !callInput.TryGet(targetBindingId, out callValue))
                {
                    errors.Add($"timeline_binding_target_missing:{declaration.BindingId}:{targetBindingId}:target input is required");
                    continue;
                }
                if (!resolver.TryResolve(declaration, callValue, out TimelineBindingHandle handle, out string error) || !handle.IsValid)
                {
                    errors?.Add($"timeline_binding_unresolved:{declaration.BindingId}:{error ?? "domain binding is unresolved"}");
                    continue;
                }
                handles[i] = handle;
            }
            if (errors != null && errors.Count != initialErrorCount)
                return null;
            return new TimelinePreparedBindings(plan, callInput, handles);
        }
    }

    public readonly struct TimelineTickContext<TScalar>
        where TScalar : struct
    {
        public TimelineTickContext(ulong logicTick, TScalar delta)
        {
            if (logicTick == 0)
                throw new ArgumentOutOfRangeException(nameof(logicTick));
            LogicTick = logicTick;
            Delta = delta;
        }

        public ulong LogicTick { get; }
        public TScalar Delta { get; }
    }

    public interface ITimelineTickExecutionView<TScalar>
        where TScalar : struct
    {
        TimelineTickContext<TScalar> Context { get; }
        bool TryReadBoolean(TimelineBindingHandle handle, out bool value);
        bool TryReadScalar(TimelineBindingHandle handle, out TScalar value);
        void WriteBoolean(TimelineBindingHandle handle, bool value);
        void WriteScalar(TimelineBindingHandle handle, TScalar value);
    }
    public readonly struct TimelinePlaybackHandle
    {
        public TimelinePlaybackHandle(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0;

        public static TimelinePlaybackHandle Invalid => default;
    }

    public readonly struct TimelinePlaybackActionContext
    {
        public TimelinePlaybackActionContext(
            ulong actionInstanceId,
            string actionId,
            ulong predictionKey,
            ulong inputSequence,
            ulong startLocalLogicTick,
            CharacterSkillId skillId)
        {
            SkillId = skillId;
            ActionInstanceId = actionInstanceId;
            ActionId = actionId ?? string.Empty;
            PredictionKey = predictionKey;
            InputSequence = inputSequence;
            StartLocalLogicTick = startLocalLogicTick;
        }

        public ulong ActionInstanceId { get; }
        public string ActionId { get; }
        public ulong PredictionKey { get; }
        public ulong InputSequence { get; }
        public ulong StartLocalLogicTick { get; }
        public CharacterSkillId SkillId { get; }
        public bool IsValid => ActionInstanceId != 0;
    }

    public enum TimelinePlaybackMode
    {
        Once,
        Loop,
        HoldLastFrame
    }

    public enum TimelinePlaybackStatus
    {
        None,
        Requested,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum TimelinePlaybackStopCause
    {
        SelfAbort,
        LowerPriorityAbort,
        ExplicitParentStop,
        StateTransition,
        Reset,
        Shutdown
    }

    public readonly partial struct TimelinePlaybackStopContext
    {
        public TimelinePlaybackStopContext(TimelinePlaybackStopCause cause, ulong localLogicTick)
        {
            Cause = cause;
            LocalLogicTick = localLogicTick;
        }

        public TimelinePlaybackStopCause Cause { get; }
        public ulong LocalLogicTick { get; }
    }

    public enum TimelineTreeExecutionPhase
    {
        Decision,
        Commit
    }

}
