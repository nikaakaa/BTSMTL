using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal static class GameplayAbilityExecutionSlotMap
    {
        public static HashSet<int> Build(
            int operationCount,
            IReadOnlyList<ProgramControlFlowEdge> controlFlow,
            IReadOnlyList<ProgramScopeLayout> scopes,
            GameplayAbilityExecutionCatalog skills,
            IReadOnlyList<ProgramReference> references,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            Func<int, IReadOnlyList<int>> operationStateSlots)
        {
            var result = new HashSet<int>();
            var characterScoped = new HashSet<int>();
            for (int i = 0; i < scopes.Count; i++)
            {
                ProgramScopeLayout scope = scopes[i];
                if (scope.Kind != ProgramScopeKind.Character)
                    continue;
                for (int slotIndex = 0; slotIndex < scope.StateSlots.Count; slotIndex++)
                    characterScoped.Add(scope.StateSlots[slotIndex]);
            }

            var outgoing = new List<int>[operationCount];
            for (int i = 0; i < controlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = controlFlow[i];
                if (!edge.Source.IsValid || !edge.Target.IsValid)
                    continue;
                List<int> targets = outgoing[edge.Source.Value];
                if (targets == null)
                {
                    targets = new List<int>();
                    outgoing[edge.Source.Value] = targets;
                }
                targets.Add(edge.Target.Value);
            }

            var visited = new bool[operationCount];
            for (int i = 0; i < skills.Bindings.Count; i++)
                VisitSkillOperation(skills.Bindings[i].EntryOperation, outgoing, visited);

            for (int operationIndex = 0; operationIndex < visited.Length; operationIndex++)
            {
                if (!visited[operationIndex])
                    continue;
                IReadOnlyList<int> operationSlots = operationStateSlots(operationIndex);
                for (int slotIndex = 0; slotIndex < operationSlots.Count; slotIndex++)
                    AddSkillExecutionStateSlot(operationSlots[slotIndex], stateSlots, characterScoped, result);
                for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
                {
                    ProgramReference reference = references[referenceIndex];
                    if (!reference.HasSourceOperation ||
                        !reference.SourceOperation.Equals(new OperationHandle(operationIndex)) ||
                        reference.Kind != ProgramReferenceKind.StateSlot)
                        continue;
                    AddSkillExecutionStateSlot(reference.TargetIndex, stateSlots, characterScoped, result);
                }
            }

            for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
            {
                ProgramScopeLayout scope = scopes[scopeIndex];
                if (scope.Kind == ProgramScopeKind.Character ||
                    !scope.OwnerOperation.IsValid ||
                    !visited[scope.OwnerOperation.Value])
                    continue;
                for (int slotIndex = 0; slotIndex < scope.StateSlots.Count; slotIndex++)
                    AddSkillExecutionStateSlot(scope.StateSlots[slotIndex], stateSlots, characterScoped, result);
            }
            return result;
        }

        static void VisitSkillOperation(
            OperationHandle operation,
            List<int>[] outgoing,
            bool[] visited)
        {
            if (!operation.IsValid || operation.Value < 0 || operation.Value >= visited.Length || visited[operation.Value])
                return;
            visited[operation.Value] = true;
            List<int> targets = outgoing[operation.Value];
            if (targets == null)
                return;
            for (int i = 0; i < targets.Count; i++)
                VisitSkillOperation(new OperationHandle(targets[i]), outgoing, visited);
        }

        static void AddSkillExecutionStateSlot(
            int slotIndex,
            IReadOnlyList<ProgramStateSlot> stateSlots,
            HashSet<int> characterScoped,
            HashSet<int> result)
        {
            if (slotIndex < 0 || slotIndex >= stateSlots.Count || characterScoped.Contains(slotIndex))
                return;
            ProgramStateOwnerKind owner = stateSlots[slotIndex].OwnerKind;
            if (owner == ProgramStateOwnerKind.Runnable ||
                owner == ProgramStateOwnerKind.StateMachine ||
                owner == ProgramStateOwnerKind.Timeline ||
                owner == ProgramStateOwnerKind.Blackboard)
                result.Add(slotIndex);
        }
    }

    public sealed class GameplayAbilityExecutionFrame<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        readonly GameplayAbilityExecutionStateBank<TValue> m_Bank;
        AbilityStateValuePageReference<TValue> m_StateValues;
        int m_Retainers;
        AbilityExecutionContext m_Identity;

        internal GameplayAbilityExecutionFrame(GameplayAbilityExecutionStateBank<TValue> bank)
        {
            m_Bank = bank;
        }

        public CharacterSkillId SkillId => m_Identity.SkillId;
        public OperationHandle EntryOperation => m_Identity.EntryOperation;
        public ulong ActionInstanceId => m_Identity.ActionInstanceId;
        public ulong PredictionKey => m_Identity.PredictionKey;
        public ulong Generation => m_Identity.Generation;
        public AbilityStateValuePageReference<TValue> StateValues => m_StateValues;
        internal int Retainers => m_Retainers;
        public ref readonly AbilityExecutionContext Identity => ref m_Identity;

        internal void Begin(AbilityExecutionContext identity)
        {
            m_Identity = identity;
            m_StateValues = m_Bank.Values.BeginEmpty();
            m_StateValues.Retain();
            m_Retainers = 1;
        }

        internal void BeginCopy(GameplayAbilityExecutionFrame<TValue> source)
        {
            m_Identity = source.m_Identity;
            m_StateValues = m_Bank.Values.BeginWrite(source.m_StateValues);
            m_Retainers = 1;
        }

        internal void RetainStateStorage()
        {
            m_Retainers++;
        }

        internal void ReleaseStateStorage()
        {
            if (--m_Retainers != 0)
                return;
            m_StateValues.Release();
            m_StateValues = default;
        }

        public bool TryGetValue(int slotIndex, out TValue value) => m_StateValues.TryGetValue(slotIndex, out value);

        internal void SetValue(int slotIndex, TValue value)
        {
            m_StateValues.Set.Set(slotIndex, value);
        }

        internal void BindGeneration(ulong generation)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (Generation != 0 && Generation != generation)
                throw new InvalidOperationException($"Skill '{SkillId}' execution frame generation changed.");
            m_Identity = m_Identity.WithGeneration(generation);
        }

        public bool Equals(GameplayAbilityExecutionFrame<TValue> other)
        {
            return other != null && m_Identity.Equals(other.m_Identity) &&
                m_StateValues.ContentEquals(other.m_StateValues);
        }
    }

    public sealed class GameplayAbilityExecutionAggregate<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        readonly List<GameplayAbilityExecutionFrame<TValue>> m_Frames;
        readonly GameplayAbilityExecutionStateBank<TValue> m_Bank;
        int m_Retainers;

        internal GameplayAbilityExecutionAggregate(GameplayAbilityExecutionStateBank<TValue> bank, int frameCapacity)
        {
            m_Bank = bank;
            m_Frames = new List<GameplayAbilityExecutionFrame<TValue>>(frameCapacity);
        }

        public IReadOnlyList<GameplayAbilityExecutionFrame<TValue>> Frames => m_Frames;
        internal int Retainers => m_Retainers;

        internal void BeginCopy(GameplayAbilityExecutionAggregate<TValue> source)
        {
            m_Frames.Clear();
            m_Retainers = 1;
            if (source == null)
                return;
            for (int index = 0; index < source.m_Frames.Count; index++)
            {
                GameplayAbilityExecutionFrame<TValue> frame = source.m_Frames[index];
                frame.RetainStateStorage();
                m_Frames.Add(frame);
            }
        }

        internal void RetainStateStorage()
        {
            m_Retainers++;
        }

        internal void ReleaseStateStorage()
        {
            if (--m_Retainers != 0)
                return;
            for (int index = 0; index < m_Frames.Count; index++)
                m_Frames[index].ReleaseStateStorage();
            m_Frames.Clear();
        }

        internal GameplayAbilityExecutionFrame<TValue> CreateFrame(AbilityExecutionContext identity) => m_Bank.CreateFrame(identity);

        internal GameplayAbilityExecutionFrame<TValue> AppendRestoredFrame(AbilityExecutionContext identity)
        {
            GameplayAbilityExecutionFrame<TValue> frame = m_Bank.CreateFrame(identity);
            m_Frames.Add(frame);
            return frame;
        }

        public GameplayAbilityExecutionFrame<TValue> Find(ulong actionInstanceId)
        {
            for (int index = 0; index < m_Frames.Count; index++)
                if (m_Frames[index].ActionInstanceId == actionInstanceId)
                    return m_Frames[index];
            return null;
        }

        internal void Add(GameplayAbilityExecutionFrame<TValue> frame)
        {
            if (frame == null || Find(frame.ActionInstanceId) != null)
                throw new ArgumentException("Skill execution state frame is invalid or duplicated.", nameof(frame));
            if (m_Frames.Count == m_Frames.Capacity)
                throw new InvalidOperationException("Skill execution frame count exceeds the declared Action capacity.");
            InsertOrdered(frame);
        }

        void InsertOrdered(GameplayAbilityExecutionFrame<TValue> frame)
        {
            int first = 0;
            int last = m_Frames.Count;
            while (first < last)
            {
                int middle = first + ((last - first) >> 1);
                if (m_Frames[middle].ActionInstanceId < frame.ActionInstanceId)
                    first = middle + 1;
                else
                    last = middle;
            }
            m_Frames.Insert(first, frame);
        }

        internal bool Remove(ulong actionInstanceId)
        {
            for (int index = 0; index < m_Frames.Count; index++)
            {
                if (m_Frames[index].ActionInstanceId != actionInstanceId)
                    continue;
                m_Frames[index].ReleaseStateStorage();
                m_Frames.RemoveAt(index);
                return true;
            }
            return false;
        }

        internal GameplayAbilityExecutionAggregate<TValue> CreateMutableShell() => m_Bank.Capture(this);

        internal GameplayAbilityExecutionAggregate<TValue> CloneForActiveFrameMutation(
            ulong actionInstanceId, out GameplayAbilityExecutionFrame<TValue> frame)
        {
            GameplayAbilityExecutionAggregate<TValue> mutable = CreateMutableShell();
            frame = mutable.MakeFrameMutable(actionInstanceId);
            return mutable;
        }

        internal GameplayAbilityExecutionFrame<TValue> MakeFrameMutable(ulong actionInstanceId)
        {
            for (int index = 0; index < m_Frames.Count; index++)
            {
                GameplayAbilityExecutionFrame<TValue> source = m_Frames[index];
                if (source.ActionInstanceId != actionInstanceId)
                    continue;
                if (source.Retainers == 1)
                    return source;
                GameplayAbilityExecutionFrame<TValue> frame = m_Bank.CopyFrame(source);
                m_Frames[index] = frame;
                source.ReleaseStateStorage();
                return frame;
            }
            throw new InvalidOperationException($"Skill execution frame '{actionInstanceId}' is absent from the mutable aggregate.");
        }

        public bool Equals(GameplayAbilityExecutionAggregate<TValue> other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || m_Frames.Count != other.m_Frames.Count)
                return false;
            for (int index = 0; index < m_Frames.Count; index++)
            {
                GameplayAbilityExecutionFrame<TValue> frame = m_Frames[index];
                if (!frame.Equals(other.Find(frame.ActionInstanceId)))
                    return false;
            }
            return true;
        }
    }

    internal sealed class GameplayAbilityExecutionStateBank<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        readonly List<GameplayAbilityExecutionAggregate<TValue>> m_States = new();
        readonly List<GameplayAbilityExecutionFrame<TValue>> m_Frames = new();
        readonly int m_FrameCapacity;
        GameplayAbilityExecutionAggregate<TValue> m_Current;

        internal GameplayAbilityExecutionStateBank(GameplayAbilityExecutionIdentity identity, int slotCount,
            int actionCapacity, Func<int, bool> ownsSlot, Action<TValue> retainValue, Action<TValue> releaseValue,
            GeneratedBlackboardStateBinding<TValue> blackboards)
        {
            m_FrameCapacity = checked(actionCapacity + 1);
            Values = new AbilityStateValuePageBank<TValue>(identity, slotCount, ownsSlot, retainValue, releaseValue,
                blackboards);
            PrepareCapacity(0);
        }

        internal AbilityStateValuePageBank<TValue> Values { get; }
        internal int FrameCapacity => m_FrameCapacity;

        internal void PrepareCapacity(int states)
        {
            int frames = checked(states * m_FrameCapacity + 1);
            Values.PrepareCapacity(frames);
            m_States.Capacity = Math.Max(m_States.Capacity, states);
            while (m_States.Count < states)
                m_States.Add(new GameplayAbilityExecutionAggregate<TValue>(this, m_FrameCapacity));
            m_Frames.Capacity = Math.Max(m_Frames.Capacity, frames);
            while (m_Frames.Count < frames)
                m_Frames.Add(new GameplayAbilityExecutionFrame<TValue>(this));
        }

        internal GameplayAbilityExecutionAggregate<TValue> Capture(GameplayAbilityExecutionAggregate<TValue> source = null)
        {
            for (int index = 0; index < m_States.Count; index++)
            {
                GameplayAbilityExecutionAggregate<TValue> state = m_States[index];
                if (state.Retainers != 0 || ReferenceEquals(state, source))
                    continue;
                state.BeginCopy(source);
                ReleaseCurrent();
                m_Current = state;
                return state;
            }
            throw new InvalidOperationException("Skill execution state page capacity does not cover retained steps.");
        }

        internal GameplayAbilityExecutionFrame<TValue> CreateFrame(AbilityExecutionContext identity)
        {
            GameplayAbilityExecutionFrame<TValue> frame = RentFrame();
            frame.Begin(identity);
            return frame;
        }

        internal GameplayAbilityExecutionFrame<TValue> CopyFrame(GameplayAbilityExecutionFrame<TValue> source)
        {
            GameplayAbilityExecutionFrame<TValue> frame = RentFrame();
            frame.BeginCopy(source);
            return frame;
        }

        GameplayAbilityExecutionFrame<TValue> RentFrame()
        {
            for (int index = 0; index < m_Frames.Count; index++)
                if (m_Frames[index].Retainers == 0)
                    return m_Frames[index];
            throw new InvalidOperationException("Skill execution frame page capacity does not cover retained steps.");
        }

        internal void ReleaseCurrent()
        {
            if (m_Current == null)
                return;
            m_Current.ReleaseStateStorage();
            m_Current = null;
        }
    }

    internal sealed class GameplayAbilityExecutionManager<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        readonly IGameplayAbilityExecutionStorage<TValue> m_Storage;
        GameplayAbilityExecutionAggregate<TValue> m_States;
        bool m_StatesShared;
        GameplayAbilityExecutionFrame<TValue> m_Active;
        bool m_ActiveFrameCloned;
        readonly Scope m_Scope;

        public GameplayAbilityExecutionManager(IGameplayAbilityExecutionStorage<TValue> storage)
        {
            m_Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            m_Scope = new Scope(this);
        }

        public void BeginEvaluation()
        {
            if (m_Active != null)
                throw new InvalidOperationException("Skill execution state retained an active frame across evaluations.");
        }

        public void EndEvaluation()
        {
            if (m_Active != null)
                throw new InvalidOperationException("Skill execution state has an unclosed frame.");
            m_States = null;
            m_StatesShared = false;
        }

        public IDisposable Enter(AbilityExecutionContext identity)
        {
            if (!identity.IsValid)
                throw new ArgumentException("Skill execution frame identity is incomplete.", nameof(identity));
            if (m_Active != null)
                throw new InvalidOperationException("Skill execution frames cannot be nested.");
            EnsureStates();
            GameplayAbilityExecutionFrame<TValue> frame = m_States.Find(identity.ActionInstanceId);
            if (frame == null)
            {
                EnsureMutableShell();
                frame = m_States.CreateFrame(identity);
                m_States.Add(frame);
                m_Storage.WriteAggregate(m_States);
                m_ActiveFrameCloned = true;
            }
            else if (frame.SkillId != identity.SkillId ||
                     !frame.EntryOperation.Equals(identity.EntryOperation) ||
                     frame.PredictionKey != identity.PredictionKey ||
                     frame.Generation != 0 && identity.Generation != 0 &&
                     frame.Generation != identity.Generation)
            {
                throw new InvalidOperationException($"Skill execution frame does not match Action instance '{identity.ActionInstanceId}'.");
            }
            m_Active = frame;
            return m_Scope.Begin(frame.ActionInstanceId);
        }

        public bool Remove(ulong actionInstanceId)
        {
            if (m_Active != null && m_Active.ActionInstanceId == actionInstanceId)
                throw new InvalidOperationException("Active Skill execution frame cannot be removed while active.");
            EnsureStates();
            if (m_States.Find(actionInstanceId) == null)
                return false;
            EnsureMutableShell();
            if (!m_States.Remove(actionInstanceId))
                return false;
            m_Storage.WriteAggregate(m_States);
            return true;
        }

        public bool IsActive(ulong actionInstanceId) =>
            m_Active != null && m_Active.ActionInstanceId == actionInstanceId;

        public bool HasFrame(ulong actionInstanceId) =>
            m_Storage.ReadAggregate().Find(actionInstanceId) != null;

        public bool BindGeneration(ulong actionInstanceId, ulong generation)
        {
            if (m_Active == null || m_Active.ActionInstanceId != actionInstanceId)
                return false;
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (m_Active.Generation == generation)
                return true;
            MakeActiveFrameMutable();
            m_Active.BindGeneration(generation);
            m_Storage.WriteAggregate(m_States);
            return true;
        }

        public bool TryGet(int slotIndex, out TValue value)
        {
            if (!m_Storage.IsAbilityStateSlot(slotIndex))
            {
                value = default;
                return false;
            }
            RequireActiveFrame(slotIndex);
            if (m_Active.TryGetValue(slotIndex, out value))
                return true;
            value = m_Storage.DefaultValue(slotIndex);
            return true;
        }

        public bool TrySet(int slotIndex, TValue value)
        {
            if (!m_Storage.IsAbilityStateSlot(slotIndex))
                return false;
            RequireActiveFrame(slotIndex);
            if (!m_Storage.IsValueValid(slotIndex, value))
                throw new InvalidOperationException($"Skill execution state slot '{slotIndex}' contains a value with the wrong kind.");
            if (m_Active.TryGetValue(slotIndex, out TValue existing) && existing.Equals(value))
                return true;
            MakeActiveFrameMutable();
            m_Active.SetValue(slotIndex, value);
            m_Storage.WriteAggregate(m_States);
            return true;
        }

        public bool TryReset(int slotIndex)
        {
            if (!m_Storage.IsAbilityStateSlot(slotIndex))
                return false;
            RequireActiveFrame(slotIndex);
            TValue defaultValue = m_Storage.DefaultValue(slotIndex);
            if (m_Active.TryGetValue(slotIndex, out TValue existing) && existing.Equals(defaultValue))
                return true;
            MakeActiveFrameMutable();
            m_Active.SetValue(slotIndex, defaultValue);
            m_Storage.WriteAggregate(m_States);
            return true;
        }

        void EnsureStates()
        {
            if (m_States != null)
                return;
            m_States = m_Storage.ReadAggregate();
            m_StatesShared = true;
        }

        void EnsureMutableShell()
        {
            EnsureStates();
            if (!m_StatesShared)
                return;
            m_States = m_States.CreateMutableShell();
            m_StatesShared = false;
        }

        void MakeActiveFrameMutable()
        {
            RequireActiveFrame(0);
            EnsureStates();
            if (m_StatesShared)
            {
                m_States = m_States.CloneForActiveFrameMutation(m_Active.ActionInstanceId, out m_Active);
                m_StatesShared = false;
                m_ActiveFrameCloned = true;
            }
            else if (!m_ActiveFrameCloned)
            {
                m_Active = m_States.MakeFrameMutable(m_Active.ActionInstanceId);
                m_ActiveFrameCloned = true;
            }
        }

        void Exit(ulong actionInstanceId)
        {
            if (m_Active.ActionInstanceId != actionInstanceId)
                throw new InvalidOperationException("Skill execution frame scope is unbalanced.");
            if (m_Active.Generation == 0)
            {
                EnsureMutableShell();
                m_States.Remove(actionInstanceId);
            }
            m_Storage.WriteAggregate(m_States);
            m_Active = null;
            m_ActiveFrameCloned = false;
        }

        void RequireActiveFrame(int slotIndex)
        {
            if (m_Active == null)
                throw new InvalidOperationException($"Skill execution state slot '{slotIndex}' requires a bound Action instance.");
        }

        sealed class Scope : IDisposable
        {
            readonly GameplayAbilityExecutionManager<TValue> m_Owner;
            ulong m_ActionInstanceId;
            bool m_Disposed;

            public Scope(GameplayAbilityExecutionManager<TValue> owner)
            {
                m_Owner = owner;
            }

            public IDisposable Begin(ulong actionInstanceId)
            {
                m_ActionInstanceId = actionInstanceId;
                m_Disposed = false;
                return this;
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_Owner.Exit(m_ActionInstanceId);
                m_ActionInstanceId = 0;
            }
        }
    }

    internal sealed class ActionSkillCommitFlow<TTargetSnapshot, TActionState>
        where TTargetSnapshot : struct
        where TActionState : struct
    {
        readonly IActionSkillCommitPort<TTargetSnapshot, TActionState> m_Port;

        public ActionSkillCommitFlow(IActionSkillCommitPort<TTargetSnapshot, TActionState> port)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
        }

        public bool TryCommit(
            ActionSkillActivationRequest<TTargetSnapshot> request,
            ActionAdmissionProfile profile,
            out ulong instanceId,
            out GameplayEffectApplyResult failure)
        {
            instanceId = m_Port.NextActionInstanceId();
            ulong predictionKey = m_Port.NextPredictionKey();
            TActionState action = m_Port.CreatePredictedAction(request, instanceId, predictionKey);
            m_Port.WriteAction(action, request.ReplacementSource);
            if (!m_Port.ApplyStartupEffects(action, out failure))
                return false;
            failure = m_Port.SetActionTags(instanceId, profile);
            if (!failure.AcceptedMutation)
                return false;
            failure = m_Port.CommitActionMutation(request.Source);
            if (!failure.AcceptedMutation)
                return false;
            m_Port.EmitActionFact(request.Source, action);
            m_Port.ClearRequest(request);
            return true;
        }
    }

    internal sealed class ActionSkillActivationFlow<TTargetSnapshot, TOperation, TActionState, TSavepoint>
        where TTargetSnapshot : struct
        where TOperation : class
        where TActionState : struct
        where TSavepoint : struct
    {
        readonly ActionAdmissionControl m_Admission;
        readonly IActionSkillActivationPort<TTargetSnapshot, TOperation, TSavepoint> m_Port;
        readonly ActionSkillCommitFlow<TTargetSnapshot, TActionState> m_Commit;

        public ActionSkillActivationFlow(
            ActionAdmissionControl admission,
            IActionSkillActivationPort<TTargetSnapshot, TOperation, TSavepoint> port,
            ActionSkillCommitFlow<TTargetSnapshot, TActionState> commit)
        {
            m_Admission = admission;
            m_Port = port;
            m_Commit = commit;
        }

        public bool ActivateImmediate(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionAdmissionProfile profile)
        {
            TTargetSnapshot targetSnapshot = NormalizeTarget(profile, candidate.TargetSnapshot);
            ActionAdmissionDecision admission = Evaluate(candidate.SkillId, profile, targetSnapshot,
                ActionAdmissionEvaluationMode.CommitActivation);
            if (!admission.Allowed)
            {
                ReportAdmissionFailure(candidate.Source, admission);
                TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                    InputSequenceFor(candidate), SimulationActionResultKind.Rejected, AdmissionReason(admission), admission.BlockingEffectHandle);
                return false;
            }
            GameplayCommandBatchAdmission commandBatch = m_Port.ReceiveStartupCommands(candidate.Source);
            if (!commandBatch.Accepted)
            {
                TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                    InputSequenceFor(candidate), SimulationActionResultKind.Rejected, "GameplayCommandBatchCapacityExceeded");
                return false;
            }
            TSavepoint savepoint = m_Port.CreateMutationSavepoint(0);
            try
            {
                if (!TryCreateRequest(candidate, profile, targetSnapshot, out ActionSkillActivationRequest<TTargetSnapshot> request))
                {
                    m_Port.RestoreMutation(savepoint);
                    TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                        InputSequenceFor(candidate), SimulationActionResultKind.Rejected, "InputRequestUnavailable");
                    return false;
                }
                m_Port.StageRequest(request);
                ConsumeInput(request);
                if (!m_Commit.TryCommit(request, profile, out ulong instanceId, out GameplayEffectApplyResult failure))
                {
                    m_Port.RestoreMutation(savepoint);
                    m_Port.ReportStartupFailure(failure, request.Source, false);
                    TraceActionResult(candidate, request.ActionId, request.SkillId, 0,
                        request.InputSequence, SimulationActionResultKind.Rejected, "GameplayEffectStartupFailed");
                    return false;
                }
                m_Port.ReleaseMutation(savepoint);
                TraceActivated(candidate, request, profile, instanceId);
                TraceActionResult(candidate, request.ActionId, request.SkillId, instanceId,
                    request.InputSequence, SimulationActionResultKind.Accepted, string.Empty, request.Opportunity.Handle);
                return true;
            }
            finally
            {
                if (m_Port.MutationIsActive(savepoint))
                    m_Port.RestoreMutation(savepoint);
            }
        }

        public ActionAdmissionDecision Preview(
            CharacterSkillId candidateSkillId,
            ActionAdmissionProfile profile,
            TTargetSnapshot targetSnapshot,
            in GameplayAbilitySourceContext replacementSource,
            ulong executingActionInstanceId)
        {
            if (!TryResolveSource(in replacementSource, out ActionAdmissionActiveAction activeSource, out _))
                return new ActionAdmissionDecision(false, ActionAdmissionRejectReason.ReplacementSourceMissing, string.Empty);
            return Evaluate(candidateSkillId, profile, NormalizeTarget(profile, targetSnapshot),
                ActionAdmissionEvaluationMode.PreviewReplacement, activeSource, executingActionInstanceId);
        }

        public ActionAdmissionDecision Preview(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionAdmissionProfile profile,
            ulong executingActionInstanceId)
        {
            if (m_Port.HasPendingRequest(profile.ActionId))
                return new ActionAdmissionDecision(false, ActionAdmissionRejectReason.ActivationRequestPending, string.Empty);
            if (!TryResolveCandidateTarget(candidate, profile, out TTargetSnapshot target,
                    out ActionAdmissionActiveAction activeSource))
                return new ActionAdmissionDecision(false,
                    candidate.RequiresOpportunity && !candidate.Opportunity.IsValid
                        ? ActionAdmissionRejectReason.OpportunityUnavailable
                        : ActionAdmissionRejectReason.ReplacementSourceMissing, string.Empty);
            return Evaluate(candidate.SkillId, profile, target, ActionAdmissionEvaluationMode.PreviewReplacement,
                activeSource, executingActionInstanceId);
        }

        public bool TryCommitPending(CharacterSkillId skillId, ActionAdmissionProfile profile)
        {
            if (!m_Port.TryReadPendingRequest(skillId, out ActionSkillActivationRequest<TTargetSnapshot> request))
                return false;
            if (request.ReplacementSource.IsValid)
            {
                ActionReplacementStopStatus stop = m_Port.ReadReplacementStopStatus(request.ReplacementSource);
                if (stop == ActionReplacementStopStatus.SourceChanged)
                    return RejectPending(request, "ReplacementSourceChanged");
                if (stop == ActionReplacementStopStatus.Waiting)
                    return false;
            }
            ActionAdmissionDecision admission = Evaluate(request.SkillId, profile, request.TargetSnapshot,
                ActionAdmissionEvaluationMode.CommitAcceptedRequest);
            if (!admission.Allowed)
            {
                m_Port.ClearPendingRequest(request);
                ReportAdmissionFailure(request.Source, admission);
                m_Port.TraceActionResult(request.Source, profile.ActionId, request.SkillId, 0,
                    request.InputSequence, SimulationActionResultKind.Rejected, AdmissionReason(admission), request.ReplacementSource, admission.BlockingEffectHandle);
                return false;
            }
            GameplayCommandBatchAdmission commandBatch = m_Port.ReceiveStartupCommands(request.Source);
            if (!commandBatch.Accepted)
            {
                m_Port.ClearPendingRequest(request);
                m_Port.TraceActionResult(request.Source, profile.ActionId, request.SkillId, 0,
                    request.InputSequence, SimulationActionResultKind.Rejected, "GameplayCommandBatchCapacityExceeded", request.ReplacementSource);
                return false;
            }
            TSavepoint savepoint = m_Port.CreateMutationSavepoint(0);
            try
            {
                if (!m_Commit.TryCommit(request, profile, out ulong instanceId, out GameplayEffectApplyResult failure))
                {
                    m_Port.RestoreMutation(savepoint);
                    m_Port.ClearPendingRequest(request);
                    m_Port.ReportStartupFailure(failure, request.Source, false);
                    m_Port.TraceActionResult(request.Source, request.ActionId, request.SkillId, 0,
                        request.InputSequence, SimulationActionResultKind.Rejected, "GameplayEffectStartupFailed", request.ReplacementSource);
                    return false;
                }
                m_Port.ReleaseMutation(savepoint);
                TraceActivated(request.Source, request, profile, instanceId);
                m_Port.TraceActionResult(request.Source, request.ActionId, request.SkillId, instanceId,
                    request.InputSequence, SimulationActionResultKind.Accepted, string.Empty, request.ReplacementSource, request.Opportunity.Handle);
                return true;
            }
            finally
            {
                if (m_Port.MutationIsActive(savepoint))
                    m_Port.RestoreMutation(savepoint);
            }
        }

        public ActionActivationSubmission ActivatePending(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionAdmissionProfile profile)
        {
            if (m_Port.HasPendingRequest(profile.ActionId))
            {
                TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                    InputSequenceFor(candidate), SimulationActionResultKind.Rejected, "ActivationRequestPending");
                return ActionActivationSubmission.Rejected;
            }
            if (!TryResolveCandidateTarget(candidate, profile, out TTargetSnapshot targetSnapshot,
                    out ActionAdmissionActiveAction activeSource))
            {
                TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                    InputSequenceFor(candidate), SimulationActionResultKind.Rejected,
                    candidate.RequiresOpportunity && !candidate.Opportunity.IsValid
                        ? "OpportunityUnavailable" : "ReplacementSourceMissing");
                return ActionActivationSubmission.Rejected;
            }
            ActionAdmissionDecision admission = Evaluate(candidate.SkillId, profile, targetSnapshot,
                candidate.ReplacementSource.IsValid
                    ? ActionAdmissionEvaluationMode.PreviewReplacement
                    : ActionAdmissionEvaluationMode.CommitActivation,
                activeSource);
            if (!admission.Allowed)
            {
                ReportAdmissionFailure(candidate.Source, admission);
                TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                    InputSequenceFor(candidate), SimulationActionResultKind.Rejected, AdmissionReason(admission), admission.BlockingEffectHandle);
                return admission.RejectReason == ActionAdmissionRejectReason.StartupRequirementsFailed
                    ? ActionActivationSubmission.StartupRejected : ActionActivationSubmission.Rejected;
            }
            TSavepoint savepoint = m_Port.CreateMutationSavepoint(candidate.ReplacementSource.InstanceId);
            try
            {
                if (!TryCreateRequest(candidate, profile, targetSnapshot, out ActionSkillActivationRequest<TTargetSnapshot> request))
                {
                    m_Port.RestoreMutation(savepoint);
                    TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                        InputSequenceFor(candidate), SimulationActionResultKind.Rejected, "InputRequestUnavailable");
                    return ActionActivationSubmission.Rejected;
                }
                if (request.Opportunity.IsValid && !m_Port.ConsumeOpportunity(request.Opportunity, request.Source))
                {
                    m_Port.RestoreMutation(savepoint);
                    TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                        request.InputSequence, SimulationActionResultKind.Rejected, "OpportunityUnavailable");
                    return ActionActivationSubmission.Rejected;
                }
                if (candidate.ReplacementSource.IsValid)
                {
                    if (!m_Port.InterruptActive(candidate.ReplacementSource, candidate.Source, "SkillReplacement"))
                    {
                        m_Port.RestoreMutation(savepoint);
                        TraceActionResult(candidate, profile.ActionId, candidate.SkillId, 0,
                            request.InputSequence, SimulationActionResultKind.Rejected, "ReplacementStopRejected");
                        return ActionActivationSubmission.Rejected;
                    }
                }
                m_Port.StageRequest(request);
                ConsumeInput(request);
                m_Port.ReleaseMutation(savepoint);
                return ActionActivationSubmission.Submitted;
            }
            finally
            {
                if (m_Port.MutationIsActive(savepoint))
                    m_Port.RestoreMutation(savepoint);
            }
        }

        void ReportAdmissionFailure(SimulationExecutionSource source, ActionAdmissionDecision admission)
        {
            if (admission.RejectReason == ActionAdmissionRejectReason.StartupRequirementsFailed)
                m_Port.ReportStartupFailure(admission.StartupFailure, source, true);
        }

        static string AdmissionReason(ActionAdmissionDecision decision) => decision.RejectReason switch
        {
            ActionAdmissionRejectReason.TargetBlocked => "TargetBlocked",
            ActionAdmissionRejectReason.ActiveSourceNotCancelable => "ActiveSourceNotCancelable",
            ActionAdmissionRejectReason.SourceActionStillActive => "SourceActionStillActive",
            ActionAdmissionRejectReason.TargetSnapshotRequired => "TargetSnapshotRequired",
            ActionAdmissionRejectReason.RequiredTagsMissing => "RequiredTagsMissing",
            ActionAdmissionRejectReason.ActionCapacityExceeded => "ActionCapacityExceeded",
            ActionAdmissionRejectReason.ReplacementSourceMissing => "ReplacementSourceMissing",
            ActionAdmissionRejectReason.StartupRequirementsFailed => "GameplayEffectStartupRequirementsFailed",
            ActionAdmissionRejectReason.ActivationRequestPending => "ActivationRequestPending",
            ActionAdmissionRejectReason.ActorAbilityBlocked => "ActorAbilityBlocked",
            ActionAdmissionRejectReason.SourceAbilityBlocked => "SourceAbilityBlocked",
            ActionAdmissionRejectReason.ReplacementSourceRequirementFailed => "ReplacementSourceRequirementFailed",
            _ => string.Empty
        };

        ActionAdmissionDecision Evaluate(
            CharacterSkillId candidateSkillId,
            ActionAdmissionProfile profile,
            TTargetSnapshot targetSnapshot,
            ActionAdmissionEvaluationMode mode,
            ActionAdmissionActiveAction replacementActiveAction = default,
            ulong executingActionInstanceId = 0)
        {
            return m_Admission.Evaluate(new ActionAdmissionRequest(
                candidateSkillId,
                profile,
                new ActionAdmissionTargetCandidate(m_Port.TargetId(targetSnapshot)),
                mode,
                replacementActiveAction,
                executingActionInstanceId));
        }

        bool TryResolveSource(in GameplayAbilitySourceContext source,
            out ActionAdmissionActiveAction activeSource, out TTargetSnapshot targetSnapshot)
        {
            if (source.IsValid)
                return m_Port.TryReadReplacementSource(in source, out activeSource, out targetSnapshot);
            activeSource = default;
            targetSnapshot = default;
            return true;
        }

        bool TryResolveCandidateTarget(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionAdmissionProfile profile,
            out TTargetSnapshot targetSnapshot,
            out ActionAdmissionActiveAction activeSource)
        {
            if (candidate.RequiresOpportunity && !candidate.Opportunity.IsValid ||
                candidate.TargetPolicy == GameplayAbilityRuleTargetPolicy.SelectedSource && !candidate.ReplacementSource.IsValid ||
                !TryResolveSource(candidate.ReplacementSource, out activeSource, out TTargetSnapshot sourceTarget))
            {
                targetSnapshot = default;
                activeSource = default;
                return false;
            }
            targetSnapshot = NormalizeTarget(profile,
                candidate.Opportunity.IsValid ? candidate.TargetSnapshot :
                    candidate.TargetPolicy == GameplayAbilityRuleTargetPolicy.SelectedSource
                        ? sourceTarget : candidate.TargetSnapshot);
            return true;
        }

        bool InputIsCurrent(in SimulationInputRequestState input) =>
            m_Port.TryReadInputRequest(input.RequestId, out SimulationInputRequestState current) &&
            current.Equals(input);

        bool TryCreateRequest(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionAdmissionProfile profile,
            TTargetSnapshot targetSnapshot,
            out ActionSkillActivationRequest<TTargetSnapshot> request)
        {
            SimulationInputRequestState input = candidate.SourceInputRequest;
            if (candidate.RequiresSourceInputRequest &&
                (!input.IsValid || !InputIsCurrent(in input) || m_Port.IsInputRequestPending(in input)))
            {
                Trace(candidate, "action_request_unavailable", ActionSkillTraceSeverity.Detail, "InputRequestUnavailable");
                request = default;
                return false;
            }
            request = new ActionSkillActivationRequest<TTargetSnapshot>(
                profile.ActionId,
                candidate.SkillId,
                candidate.SkillEntryOperation,
                candidate.ContextId,
                input,
                input.IsValid ? input.Sequence : m_Port.InputSequence,
                m_Port.Tick,
                candidate.TargetKey,
                targetSnapshot,
                candidate.Source,
                candidate.EquipmentContext,
                candidate.ReplacementSource,
                candidate.ActivationEntryId,
                candidate.ConsumeSourceInputRequest, candidate.Opportunity);
            return true;
        }

        void ConsumeInput(ActionSkillActivationRequest<TTargetSnapshot> request)
        {
            if (request.ConsumeSourceInputRequest && request.SourceInputRequest.IsValid)
                m_Port.ClearInputRequest(request.SourceInputRequestId);
        }

        bool RejectPending(ActionSkillActivationRequest<TTargetSnapshot> request, string reason)
        {
            m_Port.ClearPendingRequest(request);
            m_Port.TraceActionResult(request.Source, request.ActionId, request.SkillId, 0,
                request.InputSequence, SimulationActionResultKind.Rejected, reason, request.ReplacementSource);
            return false;
        }

        TTargetSnapshot NormalizeTarget(
            ActionAdmissionProfile profile,
            TTargetSnapshot targetSnapshot)
        {
            return profile.TargetRequirement == ActionTargetRequirement.None
                ? m_Port.NoneTarget
                : targetSnapshot;
        }

        void Trace(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            string code,
            ActionSkillTraceSeverity severity,
            string detail)
        {
            if (!m_Port.TraceEnabled)
                return;
            if (candidate.Operation != null)
                m_Port.Trace(candidate.Operation, code, severity, detail);
            else
                m_Port.Trace(candidate.Source, code, severity, detail);
        }

        void Trace(
            SimulationExecutionSource source,
            string code,
            ActionSkillTraceSeverity severity,
            string detail)
        {
            if (m_Port.TraceEnabled)
                m_Port.Trace(source, code, severity, detail);
        }

        ulong InputSequenceFor(ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate) =>
            candidate.SourceInputRequest.IsValid ? candidate.SourceInputRequest.Sequence : m_Port.InputSequence;

        void TraceActionResult(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            string actionId,
            CharacterSkillId skillId,
            ulong actionInstanceId,
            ulong inputSequence,
            SimulationActionResultKind result,
            CanonicalTextValue reason,
            ulong effectHandle = 0)
        {
            if (!m_Port.TraceEnabled)
                return;
            if (candidate.Operation != null)
                m_Port.TraceActionResult(
                    candidate.Operation,
                    actionId,
                    skillId,
                    actionInstanceId,
                    inputSequence,
                    result,
                    reason, candidate.ReplacementSource, effectHandle);
            else
                m_Port.TraceActionResult(
                    candidate.Source,
                    actionId,
                    skillId,
                    actionInstanceId,
                    inputSequence,
                    result,
                    reason, candidate.ReplacementSource, effectHandle);
        }

        void TraceActivated(
            ActionSkillActivationCandidate<TTargetSnapshot, TOperation> candidate,
            ActionSkillActivationRequest<TTargetSnapshot> request,
            ActionAdmissionProfile profile,
            ulong instanceId)
        {
            TraceActivated(request.Source, request, profile, instanceId, candidate.Operation);
        }

        void TraceActivated(
            SimulationExecutionSource source,
            ActionSkillActivationRequest<TTargetSnapshot> request,
            ActionAdmissionProfile profile,
            ulong instanceId,
            TOperation operation = null)
        {
            if (!m_Port.TraceEnabled)
                return;
            string detail = "ActionActivated";
            if (operation != null)
                m_Port.Trace(operation, "action_activated", ActionSkillTraceSeverity.Information, detail);
            else
                m_Port.Trace(source, "action_activated", ActionSkillTraceSeverity.Information, detail);
        }
    }

    internal sealed class AbilityExecution<TActionState, TReconciliationCommand>
        where TActionState : struct
    {
        readonly IAbilityLifecyclePort<TActionState, TReconciliationCommand> m_Port;

        public AbilityExecution(IAbilityLifecyclePort<TActionState, TReconciliationCommand> port)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
        }

        public bool Submit(
            CanonicalTextValue contextId,
            int transitionValue,
            CanonicalTextValue reason,
            SimulationExecutionSource source)
        {
            if (!m_Port.TryFindActive(contextId, out TActionState action))
                return false;
            return Resolve(source, action, RequireTransition(transitionValue), reason, 0, Array.Empty<TReconciliationCommand>(), true);
        }

        public bool ApplyIngress(AbilityLifecycleIngress ingress, IReadOnlyList<TReconciliationCommand> commands)
        {
            TActionState match = default;
            int matches = 0;
            IReadOnlyList<TActionState> actions = m_Port.ActionStates;
            for (int index = 0; index < actions.Count; index++)
            {
                TActionState action = actions[index];
                if (!m_Port.IsActive(action) || !Matches(action, ingress))
                    continue;
                match = action;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException($"Action lifecycle ingress '{ingress.Identity}' matched {matches} active Action instances.");
            return Resolve(
                m_Port.Source(match),
                match,
                RequireTransition(ingress.TransitionValue),
                ingress.Reason,
                ingress.SourceTick, commands, false);
        }

        public bool Resolve(
            TActionState action,
            SimulationExecutionSource source,
            AbilityLifecycleTransition transition,
            CanonicalTextValue reason)
        {
            if (!m_Port.IsActive(action))
                return false;
            return Resolve(source, action, transition, reason, 0, Array.Empty<TReconciliationCommand>(), true);
        }

        public void Resolve(
            CharacterSkillId skillId,
            GameplayAbilityExecutionBinding binding,
            string trigger,
            SimulationExecutionSource source,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason,
            string actionWindowType = "")
        {
            if (!m_Port.TryFindActive(skillId, out TActionState action))
                return;
            Resolve(action, binding, trigger, source, defaultTransition, reason, actionWindowType);
        }

        public bool Resolve(
            TActionState action,
            GameplayAbilityExecutionBinding binding,
            string trigger,
            SimulationExecutionSource source,
            AbilityLifecycleTransition defaultTransition,
            CanonicalTextValue reason,
            string actionWindowType = "")
        {
            AbilityLifecycleTransition transition = defaultTransition;
            CanonicalTextValue resolvedReason = reason.IsNull ? CanonicalTextValue.Empty : reason;
            if (binding != null && binding.TryGetEndRule(trigger, actionWindowType, out GameplayAbilityExecutionEndRule rule))
            {
                transition = RequireTerminalTransition(rule.Transition);
                if (!string.IsNullOrEmpty(rule.Reason) &&
                    (resolvedReason.IsEmpty ||
                     string.Equals(trigger, GameplayAbilityEndTriggerNames.ExecutionCompleted, StringComparison.Ordinal)))
                    resolvedReason = rule.Reason;
            }
            return Resolve(source, action, transition, resolvedReason, 0, Array.Empty<TReconciliationCommand>(), true);
        }

        public void Interrupt(
            TActionState action,
            SimulationExecutionSource source,
            CanonicalTextValue reason)
        {
            Resolve(source, action, AbilityLifecycleTransition.Interrupt, reason, 0, Array.Empty<TReconciliationCommand>(), true);
        }

        bool Resolve(
            SimulationExecutionSource source,
            TActionState action,
            AbilityLifecycleTransition transition,
            CanonicalTextValue reason,
            ulong sourceTick,
            IReadOnlyList<TReconciliationCommand> commands,
            bool requiresCommandReception)
        {
            if (!m_Port.IsActive(action))
                throw new InvalidOperationException($"Action '{m_Port.ActionId(action)}/{m_Port.InstanceId(action)}' is not active.");
            AbilityLifecycleState previousState = m_Port.State(action);
            if (transition == AbilityLifecycleTransition.Confirm &&
                previousState != AbilityLifecycleState.Predicted &&
                previousState != AbilityLifecycleState.Corrected)
            {
                throw new InvalidOperationException($"Action '{m_Port.ActionId(action)}/{m_Port.InstanceId(action)}' cannot confirm from '{previousState}'.");
            }
            if (transition == AbilityLifecycleTransition.Confirm || transition == AbilityLifecycleTransition.Reject ||
                transition == AbilityLifecycleTransition.Correct)
            {
                GameplayEffectReconciliationResult reconciliation = m_Port.Reconcile(action, transition, commands, source, requiresCommandReception);
                if (!reconciliation.Succeeded)
                    return false;
            }
            using (m_Port.EnterExecution(action))
            {
            AbilityLifecyclePhase phase = m_Port.Phase(action);
            AbilityLifecycleState state = previousState;
            CanonicalTextValue nextReason = reason.IsNull ? CanonicalTextValue.Empty : reason;
            switch (transition)
            {
                case AbilityLifecycleTransition.Confirm:
                    state = AbilityLifecycleState.Confirmed;
                    nextReason = string.Empty;
                    break;
                case AbilityLifecycleTransition.Complete:
                    phase = AbilityLifecyclePhase.Ended;
                    state = AbilityLifecycleState.Ended;
                    break;
                case AbilityLifecycleTransition.Cancel:
                    phase = AbilityLifecyclePhase.Cancel;
                    state = AbilityLifecycleState.Cancelled;
                    break;
                case AbilityLifecycleTransition.Interrupt:
                    phase = AbilityLifecyclePhase.Cancel;
                    state = AbilityLifecycleState.Interrupted;
                    break;
                case AbilityLifecycleTransition.Reject:
                    phase = AbilityLifecyclePhase.Ended;
                    state = AbilityLifecycleState.Rejected;
                    break;
                case AbilityLifecycleTransition.Correct:
                    state = AbilityLifecycleState.Corrected;
                    break;
                case AbilityLifecycleTransition.Abort:
                    phase = AbilityLifecyclePhase.Ended;
                    state = AbilityLifecycleState.Aborted;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(transition));
            }

            TActionState next = m_Port.WithLifecycle(
                action,
                new AbilityLifecycleUpdate(
                    phase,
                    state,
                    transition,
                    m_Port.Tick,
                    sourceTick,
                    nextReason));
            m_Port.WriteState(next);
            m_Port.EmitActionFact(source, next);
            if (!m_Port.IsActive(next))
                m_Port.RemoveActionTags(next, source);
            SimulationActionResultKind result = ResolveResult(transition);
            if (result != SimulationActionResultKind.None)
            {
                m_Port.TraceActionResult(
                    source,
                    m_Port.ActionId(next),
                    m_Port.SkillId(next),
                    m_Port.InstanceId(next),
                    m_Port.InputSequence(next),
                    result,
                    nextReason);
            }
            if (m_Port.TraceEnabled)
            {
                m_Port.Trace(
                    source,
                    "action_lifecycle",
                    ActionSkillTraceSeverity.Information,
                    "ActionLifecycle",
                    m_Port.SourceGeneration(source));
            }
            }
            return true;
        }

        bool Matches(TActionState action, AbilityLifecycleIngress ingress) =>
            (ingress.ActionInstanceId == 0 || ingress.ActionInstanceId == m_Port.InstanceId(action)) &&
            (ingress.PredictionKey == 0 || ingress.PredictionKey == m_Port.PredictionKey(action)) &&
            (ingress.InputSequence == 0 || ingress.InputSequence == m_Port.InputSequence(action));

        static AbilityLifecycleTransition RequireTransition(int value)
        {
            if (value < byte.MinValue || value > byte.MaxValue)
                throw new InvalidOperationException($"Action lifecycle transition '{value}' is invalid.");
            var transition = (AbilityLifecycleTransition)(byte)value;
            if (transition < AbilityLifecycleTransition.Confirm || transition > AbilityLifecycleTransition.Abort)
                throw new InvalidOperationException($"Action lifecycle transition '{value}' is invalid.");
            return transition;
        }

        static AbilityLifecycleTransition RequireTerminalTransition(int value)
        {
            AbilityLifecycleTransition transition = RequireTransition(value);
            if (transition != AbilityLifecycleTransition.Complete &&
                transition != AbilityLifecycleTransition.Cancel &&
                transition != AbilityLifecycleTransition.Interrupt &&
                transition != AbilityLifecycleTransition.Abort)
                throw new InvalidOperationException($"Action end rule transition '{value}' is not terminal.");
            return transition;
        }

        static SimulationActionResultKind ResolveResult(AbilityLifecycleTransition transition)
        {
            return transition switch
            {
                AbilityLifecycleTransition.Complete => SimulationActionResultKind.Completed,
                AbilityLifecycleTransition.Cancel => SimulationActionResultKind.Cancelled,
                AbilityLifecycleTransition.Interrupt => SimulationActionResultKind.Interrupted,
                AbilityLifecycleTransition.Reject => SimulationActionResultKind.Rejected,
                AbilityLifecycleTransition.Correct => SimulationActionResultKind.Corrected,
                AbilityLifecycleTransition.Abort => SimulationActionResultKind.Aborted,
                _ => SimulationActionResultKind.None
            };
        }

    }
}
