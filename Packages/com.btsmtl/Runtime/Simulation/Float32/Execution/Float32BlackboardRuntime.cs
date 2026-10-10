using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal readonly struct SimulationBlackboardSlotGroup
    {
        public SimulationBlackboardSlotGroup(
            int value,
            int ownerToken,
            int lifetime,
            int writeStamp,
            ProgramScopeLayout scope,
            int compiledOwnerIndex,
            ProgramBlackboardLifetime lifetimeKind,
            ProgramCatalogEntry declaration)
        {
            Value = value;
            OwnerToken = ownerToken;
            Lifetime = lifetime;
            WriteStamp = writeStamp;
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            CompiledOwnerIndex = compiledOwnerIndex;
            LifetimeKind = lifetimeKind;
            Declaration = declaration;
        }

        public int Value { get; }
        public int OwnerToken { get; }
        public int Lifetime { get; }
        public int WriteStamp { get; }
        public ProgramScopeLayout Scope { get; }
        public int CompiledOwnerIndex { get; }
        public ProgramBlackboardLifetime LifetimeKind { get; }
        public ProgramCatalogEntry Declaration { get; }
    }

    internal readonly struct SimulationTimelineBlackboardContext
    {
        public SimulationTimelineBlackboardContext(
            OperationHandle timeline,
            OperationHandle clip,
            int cycle,
            Float32ActionInstanceState action)
        {
            Timeline = timeline;
            Clip = clip;
            Cycle = cycle;
            Action = action;
        }

        public OperationHandle Timeline { get; }
        public OperationHandle Clip { get; }
        public int Cycle { get; }
        public Float32ActionInstanceState Action { get; }
        public bool IsValid => Timeline.IsValid && Clip.IsValid;
    }

    internal readonly struct SimulationActionWindowProjectionCandidate
    {
        public SimulationActionWindowProjectionCandidate(
            SimulationExecutionSource source,
            ulong generation,
            ActorId actorId,
            ulong logicTick,
            string declarationId,
            string actionId,
            GameplayAbilitySourceContext sourceContext,
            string windowId,
            string windowType,
            string windowTag,
            ulong digest)
        {
            Source = source;
            Generation = generation;
            ActorId = actorId;
            LogicTick = logicTick;
            DeclarationId = declarationId;
            ActionId = actionId;
            SourceContext = sourceContext;
            WindowId = windowId;
            WindowType = windowType;
            WindowTag = windowTag;
            Digest = digest;
        }

        public SimulationExecutionSource Source { get; }
        public ulong Generation { get; }
        public ActorId ActorId { get; }
        public ulong LogicTick { get; }
        public string DeclarationId { get; }
        public string ActionId { get; }
        public GameplayAbilitySourceContext SourceContext { get; }
        public ulong ActionInstanceId => SourceContext.InstanceId;
        public string WindowId { get; }
        public string WindowType { get; }
        public string WindowTag { get; }
        public ulong Digest { get; }
    }

    internal readonly struct SimulationActionWindowProjectionKey : IEquatable<SimulationActionWindowProjectionKey>
    {
        readonly string m_DeclarationId;
        readonly GameplayAbilitySourceContext m_SourceContext;

        public SimulationActionWindowProjectionKey(string declarationId, in GameplayAbilitySourceContext sourceContext)
        {
            m_DeclarationId = declarationId ?? string.Empty;
            m_SourceContext = sourceContext;
        }

        public bool Equals(SimulationActionWindowProjectionKey other) =>
            m_SourceContext.Equals(other.m_SourceContext) &&
            CanonicalTextValue.Equals(m_DeclarationId, other.m_DeclarationId, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is SimulationActionWindowProjectionKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((m_DeclarationId == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(m_DeclarationId)) * 397 ^
                m_SourceContext.GetHashCode());
    }

    internal sealed class Float32BlackboardRuntime : Float32OperationModule, IFloat32BlackboardPort
    {
        readonly Float32StatePort m_State;
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly IFloat32ActionContextReader m_Actions;
        readonly Float32FactSink m_Facts;
        readonly Float32TraceSink m_Trace;
        readonly List<SimulationActionWindowProjectionCandidate> m_ActionWindowProjections;
        readonly HashSet<SimulationActionWindowProjectionKey> m_ActionWindowProjectionKeys;
        readonly Stack<SimulationTimelineBlackboardContext> m_TimelineBlackboardContexts;
        readonly Stack<TimelineBlackboardScope> m_TimelineBlackboardScopePool = new();

        public Float32BlackboardRuntime(
            Float32GameplayAbilityExecutionAccess access,
            Float32StatePort state,
            Float32AbilityExecutionFrame frame,
            IFloat32ActionContextReader actions,
            Float32FactSink facts,
            Float32TraceSink trace,
            Float32AbilityExecutionWorkspace workspace)
            : base(access)
        {
            m_State = state;
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_Facts = facts;
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));
            m_ActionWindowProjections = workspace.ActionWindowProjections;
            m_ActionWindowProjectionKeys = workspace.ActionWindowProjectionKeys;
            m_TimelineBlackboardContexts = workspace.TimelineBlackboardContexts;
        }

        public void EndFrame()
        {
            FlushBlackboardProjections();
            m_ActionWindowProjections.Clear();
            m_ActionWindowProjectionKeys.Clear();
        }

        public void WriteGraphCallParameter(int valueSlot, AbilityStateValue value)
        {
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(valueSlot);
            if (value.Kind != m_Ability.StateSlots[valueSlot].ValueKind)
                throw new InvalidOperationException($"Graph call parameter state '{valueSlot}' expects '{m_Ability.StateSlots[valueSlot].ValueKind}', received '{value.Kind}'.");
            m_State.Set(group.Value, value);
        }

        public AbilityStateValue ReadGraphCallParameter(int valueSlot)
        {
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(valueSlot);
            return m_State.Get(group.Value);
        }

        public void ResetGraphCallParameter(int valueSlot)
        {
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(valueSlot);
            m_State.Reset(group.Value);
        }

        public void ActivateOperationScopes<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            ulong generation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            if (generation == 0)
                throw new InvalidOperationException($"Operation scope '{SourcePath(operation)}' has no activation generation.");
        }

        public void CompleteOperationScopes(SimulationOperation operation)
        {
        }

        public void ClearStateScopes(SimulationOperation state)
        {
        }

        public void ClearActionInstanceScopes(ulong actionInstanceId)
        {
            if (actionInstanceId == 0)
                return;
        }

        public void ProjectBlackboardInput(BlackboardInputStateBinding binding, SimulationInputValue value)
        {
            if (!CanonicalTextValue.Equals(binding.InputId, value.InputId, StringComparison.Ordinal) ||
                (byte)binding.InputKind != (byte)value.Kind)
                throw new InvalidOperationException($"Blackboard Input Binding '{binding.InputId}/{binding.InputKind}' received '{value.InputId}/{value.Kind}'.");
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(binding.StateAddress.SlotIndex);
            if (group.Scope.Kind != ProgramScopeKind.Character || group.LifetimeKind != ProgramBlackboardLifetime.Spawn)
                throw new InvalidOperationException($"Blackboard Input Binding '{binding.InputId}' must use Character/Spawn ownership.");
            var owner = new BlackboardOwnerToken(ProgramScopeKind.Character, group.CompiledOwnerIndex, 1);
            if (m_State.Get(group.OwnerToken).BlackboardOwnerToken != owner)
                MaterializeGroup(group, owner);
            m_State.Set(group.Value, ToStateValue(value));
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(new BlackboardWriteStamp(
                m_Layout.RootOperation,
                m_Frame.Tick.Value,
                default,
                OperationHandle.Invalid,
                OperationHandle.Invalid,
                0)));
        }

        static AbilityStateValue ToStateValue(SimulationInputValue value)
        {
            return value.Kind switch
            {
                ProgramInputValueKind.Boolean => AbilityStateValue.FromBoolean(value.Boolean),
                ProgramInputValueKind.Scalar => AbilityStateValue.FromScalar(value.Scalar),
                ProgramInputValueKind.Vector2 => AbilityStateValue.FromVector2(value.Vector2),
                ProgramInputValueKind.Vector3 => AbilityStateValue.FromVector3(value.Vector3),
                ProgramInputValueKind.Yaw => AbilityStateValue.FromYaw(value.Yaw),
                ProgramInputValueKind.ActionTargetSnapshot => AbilityStateValue.FromActionTargetSnapshot(value.ActionTargetSnapshot),
                _ => throw new InvalidOperationException($"Blackboard Input Binding value kind '{value.Kind}' is unsupported.")
            };
        }

        public AbilityStateValue Read<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(valueSlot);
            if (TryReadActiveGraphCallInput(cursor, valueSlot, out AbilityStateValue graphInput))
                return graphInput;
            BlackboardOwnerToken expected = ResolveBlackboardOwnerToken(cursor, operation, group, false, out _);
            if (m_State.Get(group.OwnerToken).BlackboardOwnerToken != expected)
                return DefaultValue(group);
            return m_State.Get(valueSlot);
        }

        bool TryReadActiveGraphCallInput<TTarget>(
            OperationControlCursor<TTarget> cursor,
            int valueSlot,
            out AbilityStateValue value)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            for (int i = 0; i < m_Ability.GraphCallFrames.Count; i++)
            {
                ProgramGraphCallFrame frame = m_Ability.GraphCallFrames[i];
                if (!cursor.IsActive(frame.OwnerOperation))
                    continue;
                for (int bindingIndex = 0; bindingIndex < frame.Inputs.Count; bindingIndex++)
                {
                    if (frame.Inputs[bindingIndex].StateSlot != valueSlot)
                        continue;
                    value = ReadGraphCallParameter(valueSlot);
                    return true;
                }
            }
            value = default;
            return false;
        }

        public void Write<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            int valueSlot,
            AbilityStateValue value)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            SimulationBlackboardSlotGroup group = Access.Services.BlackboardGroup(valueSlot);
            ProgramBlackboardLifetime lifetime = group.LifetimeKind;
            if (lifetime == ProgramBlackboardLifetime.Config)
                throw new InvalidOperationException($"Blackboard config '{m_Ability.StateSlots[valueSlot].OwnerIdentity}' is read-only.");
            BlackboardOwnerToken expected = ResolveBlackboardOwnerToken(cursor, operation, group, true, out Float32ActionInstanceState action);
            StoreValue(group, value, expected, BuildBlackboardWriteStamp(operation, action));
            ProjectBlackboardWrite(cursor,
                SimulationExecutionSource.FromSkillOperation(operation.Handle, SourcePath(operation)),
                0, group, value, action);
            if (m_Trace.CaptureBlackboard && !cursor.IsPredictiveEvaluation)
                m_Trace.AddBlackboard(operation, valueSlot, value, expected, group.LifetimeKind);
        }

        void StoreValue(SimulationBlackboardSlotGroup group, AbilityStateValue value,
            BlackboardOwnerToken expected, BlackboardWriteStamp stamp)
        {
            BlackboardOwnerToken current = m_State.Get(group.OwnerToken).BlackboardOwnerToken;
            if (current != expected)
            {
                if (group.Scope.Kind == ProgramScopeKind.ActionInstance && current.IsValid && m_Actions.ContainsInstance(current.Generation))
                {
                    throw new InvalidOperationException(
                        $"ActionInstance Blackboard scope '{group.Scope.Identity}' cannot bind active instances '{current.Generation}' and '{expected.Generation}' to one state address.");
                }
                MaterializeGroup(group, expected);
            }
            m_State.Set(group.Value, value);
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(stamp));
        }

        BlackboardOwnerToken ResolveBlackboardOwnerToken<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            SimulationBlackboardSlotGroup group,
            bool writing,
            out Float32ActionInstanceState action)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            action = default;
            ProgramScopeLayout scope = group.Scope;
            ulong expectedGeneration;
            switch (scope.Kind)
            {
                case ProgramScopeKind.Character:
                    expectedGeneration = 1;
                    break;
                case ProgramScopeKind.Graph:
                    if (group.LifetimeKind == ProgramBlackboardLifetime.Config)
                    {
                        expectedGeneration = 1;
                    }
                    else
                    {
                        if (!cursor.IsActive(scope.OwnerOperation))
                            throw new InvalidOperationException($"Blackboard access '{SourcePath(operation)}' is outside Graph scope '{scope.Identity}'.");
                        expectedGeneration = cursor.ReadGeneration(scope.OwnerOperation);
                    }
                    break;
                case ProgramScopeKind.State:
                    if (!cursor.IsCurrentStateExecution(scope.OwnerOperation))
                        throw new InvalidOperationException($"Blackboard access '{SourcePath(operation)}' is outside State scope '{scope.Identity}'.");
                    expectedGeneration = cursor.ReadGeneration(scope.OwnerOperation);
                    break;
                case ProgramScopeKind.ActionInstance:
                    action = ResolveBlackboardActionContext(operation, writing && BlackboardRequiresActionWindowProjection(group));
                    if (!action.IsActive)
                        throw new InvalidOperationException($"Blackboard access '{SourcePath(operation)}' requires an explicit active Action Context.");
                    expectedGeneration = action.InstanceId;
                    break;
                case ProgramScopeKind.Frame:
                    expectedGeneration = m_Frame.Tick.Value;
                    if (writing && BlackboardRequiresActionWindowProjection(group))
                        action = ResolveBlackboardActionContext(operation, true);
                    break;
                default:
                    throw new InvalidOperationException($"Blackboard scope '{scope.Kind}' is unsupported.");
            }
            return new BlackboardOwnerToken(scope.Kind, group.CompiledOwnerIndex, expectedGeneration);
        }

        Float32ActionInstanceState ResolveBlackboardActionContext(SimulationOperation operation, bool requiresProjection)
        {
            if (m_TimelineBlackboardContexts.Count > 0)
            {
                SimulationTimelineBlackboardContext timeline = m_TimelineBlackboardContexts.Peek();
                Float32ActionInstanceState action = m_Actions.RequireActive(timeline.Action);
                string explicitContext = GetStringConstant(operation, OperationNamedConstant.FactContext, string.Empty);
                if (!CanonicalTextValue.IsNullOrEmpty(explicitContext) &&
                    !CanonicalTextValue.Equals(explicitContext, action.ContextId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"TreeClip Blackboard write '{SourcePath(operation)}' Action Context does not match its Timeline playback context.");
                }
                return action;
            }

            if (m_Actions.TryGetCurrentSkillExecution(out Float32ActionInstanceState skillAction))
            {
                string skillContext = GetStringConstant(operation, OperationNamedConstant.FactContext, string.Empty);
                if (!CanonicalTextValue.IsNullOrEmpty(skillContext) &&
                    !CanonicalTextValue.Equals(skillContext, skillAction.ContextId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Skill operation '{SourcePath(operation)}' Action Context does not match its Action instance.");
                return skillAction;
            }

            CanonicalTextValue contextId = GetStringConstant(operation, OperationNamedConstant.FactContext, string.Empty);
            if (!CanonicalTextValue.IsNullOrEmpty(contextId))
            {
                int slot = m_Actions.FindActive(contextId, out Float32ActionInstanceState explicitAction);
                return slot >= 0 ? explicitAction : default;
            }

            if (requiresProjection)
                return default;
            return m_Actions.FindOnlyActive();
        }

        BlackboardWriteStamp BuildBlackboardWriteStamp(SimulationOperation operation, Float32ActionInstanceState action)
        {
            if (m_TimelineBlackboardContexts.Count == 0)
            {
                return new BlackboardWriteStamp(
                    operation.Handle,
                    m_Frame.Tick.Value,
                    action.IsActive ? action.SourceContext : default,
                    OperationHandle.Invalid,
                    OperationHandle.Invalid,
                    0);
            }
            SimulationTimelineBlackboardContext context = m_TimelineBlackboardContexts.Peek();
            return new BlackboardWriteStamp(
                operation.Handle,
                m_Frame.Tick.Value,
                action.IsActive ? action.SourceContext : default,
                context.Timeline,
                context.Clip,
                context.Cycle);
        }

        void ProjectBlackboardWrite<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationExecutionSource source,
            ulong commandSequence,
            SimulationBlackboardSlotGroup group,
            AbilityStateValue value,
            Float32ActionInstanceState action)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramCatalogEntry declaration = group.Declaration;
            if (!TryCatalogInt32(declaration, ProgramCatalogFieldId.Projection, out int projectionValue))
                return;
            ProgramBlackboardFactProjectionKind projection =
                (ProgramBlackboardFactProjectionKind)projectionValue;
            if (projection != ProgramBlackboardFactProjectionKind.ActionWindow ||
                value.Kind != ProgramStateValueKind.Boolean)
            {
                throw new InvalidOperationException($"Blackboard projection '{declaration.Identity}' is not a valid Bool ActionWindow projection.");
            }
            if (!value.Boolean)
                return;
            if (group.Scope.Kind != ProgramScopeKind.Frame || group.LifetimeKind != ProgramBlackboardLifetime.Frame)
                throw new InvalidOperationException($"ActionWindow projection '{declaration.Identity}' is not Frame/Frame.");
            if (!action.IsActive)
                throw new InvalidOperationException($"ActionWindow projection '{declaration.Identity}' has no active Action Context at tick '{m_Frame.Tick.Value}'.");

            if (!m_ActionWindowProjectionKeys.Add(new SimulationActionWindowProjectionKey(
                    declaration.Identity,
                    action.SourceContext)))
                return;
            m_ActionWindowProjections.Add(new SimulationActionWindowProjectionCandidate(
                source,
                source.IsBlackboardCommand ? commandSequence : cursor.ReadGeneration(source.Operation),
                m_Frame.ActorId,
                m_Frame.Tick.Value,
                declaration.Identity,
                action.ActionId,
                action.SourceContext,
                CatalogString(declaration, ProgramCatalogFieldId.ActionWindowId),
                CatalogString(declaration, ProgramCatalogFieldId.ActionWindowType),
                CatalogIdentity(declaration, ProgramCatalogFieldId.ActionWindowTag),
                CatalogUInt64(declaration, ProgramCatalogFieldId.ActionWindowDigest)));
        }

        public bool IsActionWindowActive(SimulationOperation operation, in GameplayAbilitySourceContext source)
        {
            string windowTag = operation.Text0;
            for (int i = 0; i < m_ActionWindowProjections.Count; i++)
            {
                SimulationActionWindowProjectionCandidate candidate = m_ActionWindowProjections[i];
                if (candidate.ActorId == m_Frame.ActorId &&
                    candidate.LogicTick == m_Frame.Tick.Value &&
                    candidate.SourceContext.Equals(source) &&
                    Access.Services.GameplayEffectCatalog.IsTagOrParent(candidate.WindowTag, windowTag))
                {
                    if (m_Trace.Enabled)
                        m_Trace.Add(operation, "action_window_active", SimulationTraceSeverity.Detail, candidate.WindowId);
                    return true;
                }
            }
            if (m_Trace.Enabled)
                m_Trace.Add(operation, "action_window_inactive", SimulationTraceSeverity.Detail, windowTag);
            return false;
        }

        internal static bool IsFrameWindowActive(Float32GameplayAbilityExecutionInstallation installation,
            Float32CharacterRuntimeState character, in GameplayAbilitySourceContext source, string windowTag)
		{
            Float32AbilityRuntimeState ability = default;
            for (int i = 0; i < character.Abilities.Count; i++)
                if (character.Abilities[i].AbilityIdentity.AbilityId == installation.Data.AbilityId)
                {
                    ability = character.Abilities[i];
                    break;
                }
            var frames = ability.AbilityExecutionState.Frames;
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
			{
                var frame = frames[frameIndex];
                if (frame.ActionInstanceId != source.InstanceId || frame.SkillId != source.SkillId ||
                    frame.Generation != source.SkillExecutionGeneration)
                    continue;
                for (int scopeIndex = 0; scopeIndex < installation.Data.Scopes.Count; scopeIndex++)
                {
                    ProgramScopeLayout scope = installation.Data.Scopes[scopeIndex];
                    if (scope.Kind != ProgramScopeKind.Frame)
                        continue;
                    var groups = installation.Services.BlackboardGroups(scope);
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        SimulationBlackboardSlotGroup group = groups[groupIndex];
                        if (!installation.Layout.TryGetCatalogIdentity(group.Declaration, ProgramCatalogFieldId.ActionWindowTag, out string windowIdentity) ||
                            !installation.Services.GameplayEffectCatalog.IsTagOrParent(windowIdentity, windowTag))
                            continue;
                        var owner = new BlackboardOwnerToken(ProgramScopeKind.Frame, group.CompiledOwnerIndex, character.LastCompletedTick);
                        if (ReadSnapshotValue(installation, ability, frame, group.OwnerToken).BlackboardOwnerToken == owner &&
                            ReadSnapshotValue(installation, ability, frame, group.WriteStamp).BlackboardWriteStamp.SourceContext.Equals(source) &&
                            ReadSnapshotValue(installation, ability, frame, group.Value).Boolean)
                            return true;
                    }
                }
			}
			return false;
        }


        static AbilityStateValue ReadSnapshotValue(Float32GameplayAbilityExecutionInstallation installation,
            Float32AbilityRuntimeState ability, GameplayAbilityExecutionFrame<AbilityStateValue> frame, int slot)
        {
            AbilityStateValue value;
            bool stored = frame != null && installation.Layout.IsSkillExecutionStateSlot(slot)
                ? frame.TryGetValue(slot, out value)
                : ability.StateValues.TryGetValue(slot, out value);
            return stored ? value : installation.Data.DefaultStateValue(slot);
		}

        bool BlackboardRequiresActionWindowProjection(SimulationBlackboardSlotGroup group)
        {
            ProgramCatalogEntry declaration = group.Declaration;
            if (!TryCatalogInt32(declaration, ProgramCatalogFieldId.Projection, out int projection))
                return false;
            if (projection != (int)ProgramBlackboardFactProjectionKind.ActionWindow)
                throw new InvalidOperationException($"Blackboard projection '{declaration.Identity}' is unsupported.");
            return true;
        }

        void FlushBlackboardProjections()
        {
            for (int i = 0; i < m_ActionWindowProjections.Count; i++)
            {
                SimulationActionWindowProjectionCandidate candidate = m_ActionWindowProjections[i];
                SimulationEventHeader header = m_Facts.Next(candidate.Source, candidate.Generation);
                var window = new ActionWindowFact(
                    candidate.SourceContext,
                    candidate.ActionId,
                    candidate.WindowId,
                    candidate.WindowType,
                    candidate.WindowTag,
                    m_Frame.Tick.Value,
                    m_Frame.Tick.Value,
                    candidate.Digest);
                m_Facts.Add(new GameplayFact(header, window));
                if (m_Trace.Enabled)
                    m_Trace.Add(candidate.Source, "blackboard_action_window_projected", SimulationTraceSeverity.Information, candidate.DeclarationId, candidate.Generation);
            }
        }

        public IDisposable PushTimelineContext(
            SimulationOperation timeline,
            SimulationOperation clip,
            int cycle,
            Float32ActionInstanceState action)
        {
            m_TimelineBlackboardContexts.Push(new SimulationTimelineBlackboardContext(
                timeline.Handle,
                clip.Handle,
                cycle,
                action));
            TimelineBlackboardScope scope = m_TimelineBlackboardScopePool.Count > 0
                ? m_TimelineBlackboardScopePool.Pop()
                : new TimelineBlackboardScope();
            return scope.Begin(this);
        }

        AbilityStateValue DefaultValue(SimulationBlackboardSlotGroup group)
        {
            ProgramStateSlot value = m_Ability.StateSlots[group.Value];
            return AbilityStateValue.FromConstant(m_Ability.Constants[value.DefaultConstantIndex], value.ValueKind);
        }

        void MaterializeGroup(SimulationBlackboardSlotGroup group, BlackboardOwnerToken ownerToken)
        {
            m_State.Set(group.Value, DefaultValue(group));
            m_State.Set(group.OwnerToken, AbilityStateValue.FromBlackboardOwnerToken(ownerToken));
            m_State.Set(group.WriteStamp, AbilityStateValue.FromBlackboardWriteStamp(default));
        }

        sealed class TimelineBlackboardScope : IDisposable
        {
            Float32BlackboardRuntime m_Owner;

            public TimelineBlackboardScope Begin(Float32BlackboardRuntime owner)
            {
                m_Owner = owner;
                return this;
            }

            public void Dispose()
            {
                if (m_Owner == null)
                    return;
                m_Owner.m_TimelineBlackboardContexts.Pop();
                m_Owner.m_TimelineBlackboardScopePool.Push(this);
                m_Owner = null;
            }
        }
    }
}
