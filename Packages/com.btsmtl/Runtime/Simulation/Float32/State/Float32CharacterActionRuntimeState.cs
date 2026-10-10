using System;
using System.Collections.Generic;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterActionRuntimeState : IFloat32ActionRuntimeStatePort
    {
        readonly List<SimulationActionActivationRequestState> m_ActionActivationRequests;
        readonly List<Float32ActionInstanceState> m_ActionInstances;
        readonly int m_Capacity;
        readonly Dictionary<ulong, int> m_ActionInstanceIndices;
        readonly Dictionary<CharacterSkillId, List<int>> m_ActionInstanceIndicesBySkill;
        readonly Comparison<int> m_ActionInstanceIndexComparison;
        bool m_ActivationRequestsWritten;
        bool m_InstancesWritten;
        ulong m_ActionEventSequence;
        Float32ActionStatePageReference m_BaseState;
        bool m_Disposed;

        public Float32CharacterActionRuntimeState(
            int capacity,
            IReadOnlyList<Float32GameplayAbilityExecutionInstallation> installations)
        {
            m_Capacity = capacity;
            m_ActionActivationRequests = new List<SimulationActionActivationRequestState>(capacity);
            m_ActionInstances = new List<Float32ActionInstanceState>(capacity);
            m_ActionInstanceIndices = new Dictionary<ulong, int>(capacity);
            m_ActionInstanceIndicesBySkill = new Dictionary<CharacterSkillId, List<int>>(installations.Count);
            m_ActionInstanceIndexComparison = CompareActionInstanceIndices;
            for (int i = 0; i < installations.Count; i++)
            {
                Float32GameplayAbilityExecutionInstallation installation = installations[i];
                m_ActionInstanceIndicesBySkill.Add(installation.Identity.AbilityId,
                    new List<int>(installation.Layout.ActionInstanceCapacity));
            }
        }

        public Float32CharacterActionRuntimeState Restart(
            Float32ActionStatePageReference state,
            ulong actionEventSequence)
        {
            m_BaseState = state;
            ReadOnlySpan<SimulationActionActivationRequestState> requests = state.Requests;
            if (!Same(m_ActionActivationRequests, requests))
            {
                ReleaseRequests();
                for (int index = 0; index < requests.Length; index++)
                    { requests[index].Retain(); requests[index].TargetSnapshot.Retain(); m_ActionActivationRequests.Add(requests[index]); }
            }
            ReadOnlySpan<Float32ActionInstanceState> actions = state.Actions;
            if (!Same(m_ActionInstances, actions))
            {
                ReleaseActions();
                for (int index = 0; index < actions.Length; index++)
                    { actions[index].Retain(); m_ActionInstances.Add(actions[index]); }
                RebuildActionInstanceIndex();
            }
            m_ActionEventSequence = actionEventSequence;
            m_ActivationRequestsWritten = false;
            m_InstancesWritten = false;
            m_Disposed = false;
            return this;
        }

        public ulong NextActionEventSequence()
        {
            RequireActive();
            m_ActionEventSequence = checked(m_ActionEventSequence + 1UL);
            if (m_ActionEventSequence == 0)
                throw new OverflowException("Action event sequence overflowed.");
            return m_ActionEventSequence;
        }

        public IReadOnlyList<SimulationActionActivationRequestState> GetActionActivationRequests()
        {
            RequireActive();
            return m_ActionActivationRequests;
        }

        public IReadOnlyList<Float32ActionInstanceState> GetActionInstances()
        {
            RequireActive();
            return m_ActionInstances;
        }

        public bool TryGetActionInstance(ulong instanceId, out Float32ActionInstanceState action)
        {
            RequireActive();
            if (m_ActionInstanceIndices.TryGetValue(instanceId, out int index))
            {
                action = m_ActionInstances[index];
                return true;
            }
            action = default;
            return false;
        }

        public bool TryFindActionInstanceIndex(ulong instanceId, out int index)
        {
            RequireActive();
            return m_ActionInstanceIndices.TryGetValue(instanceId, out index);
        }

        public IReadOnlyList<int> GetActionInstanceIndices(CharacterSkillId skillId)
        {
            RequireActive();
            return m_ActionInstanceIndicesBySkill.TryGetValue(skillId, out List<int> indices)
                ? indices
                : Array.Empty<int>();
        }

        public void ReplaceActionInstanceAt(int index, Float32ActionInstanceState action)
        {
            RequireActive();
            if (index < 0 || index >= m_ActionInstances.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            Float32ActionInstanceState previous = m_ActionInstances[index];
            if (previous.Equals(action))
                return;
            action.Retain();
            previous.Release();
            m_ActionInstances[index] = action;
            UpdateActionInstanceIndex(index, previous, action);
            m_InstancesWritten = true;
        }

        public void AddActionInstance(Float32ActionInstanceState action)
        {
            RequireActive();
            if (m_ActionInstances.Count >= m_Capacity)
                throw new InvalidOperationException("Character action state capacity is exhausted.");
            action.Retain();
            m_ActionInstances.Add(action);
            UpdateActionInstanceIndex(m_ActionInstances.Count - 1, default, action);
            m_InstancesWritten = true;
        }

        public void AddActivationRequest(SimulationActionActivationRequestState request)
        {
            RequireActive();
            if (m_ActionActivationRequests.Count >= m_Capacity)
                throw new InvalidOperationException("Character action state capacity is exhausted.");
            request.Retain();
            request.TargetSnapshot.Retain();
            m_ActionActivationRequests.Add(request);
            m_ActivationRequestsWritten = true;
        }

        public void RemoveActivationRequestAt(int index)
        {
            RequireActive();
            if (index < 0 || index >= m_ActionActivationRequests.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            m_ActionActivationRequests[index].Release();
            m_ActionActivationRequests[index].TargetSnapshot.Release();
            m_ActionActivationRequests.RemoveAt(index);
            m_ActivationRequestsWritten = true;
        }

        internal ulong ActionEventSequence
        {
            get
            {
                RequireActive();
                return m_ActionEventSequence;
            }
        }

        internal Float32ActionStatePageReference Capture()
        {
            RequireActive();
            bool requestsUnchanged = !m_ActivationRequestsWritten || Same(m_ActionActivationRequests, m_BaseState.Requests);
            bool actionsUnchanged = !m_InstancesWritten || Same(m_ActionInstances, m_BaseState.Actions);
            if (requestsUnchanged && actionsUnchanged)
                return m_BaseState;
            Float32ActionStatePageReference state = m_BaseState.Page.Bank.Begin();
            m_ActionActivationRequests.CopyTo(state.Page.Requests);
            m_ActionInstances.CopyTo(state.Page.Actions);
            state.Page.RequestCount = m_ActionActivationRequests.Count;
            state.Page.ActionCount = m_ActionInstances.Count;
            return state;
        }

        internal void RestoreActivation(Float32ActionStatePageReference state, ulong actionEventSequence)
        {
            ReadOnlySpan<SimulationActionActivationRequestState> requests = state.Requests;
            ReadOnlySpan<Float32ActionInstanceState> actions = state.Actions;
            ReleaseRequests();
            for (int i = 0; i < requests.Length; i++)
                { requests[i].Retain(); requests[i].TargetSnapshot.Retain(); m_ActionActivationRequests.Add(requests[i]); }
            ReleaseActions();
            for (int i = 0; i < actions.Length; i++)
                { actions[i].Retain(); m_ActionInstances.Add(actions[i]); }
            RebuildActionInstanceIndex();
            m_ActionEventSequence = actionEventSequence;
            m_ActivationRequestsWritten = !Same(m_ActionActivationRequests, m_BaseState.Requests);
            m_InstancesWritten = !Same(m_ActionInstances, m_BaseState.Actions);
        }

        void UpdateActionInstanceIndex(
            int index,
            Float32ActionInstanceState previous,
            Float32ActionInstanceState action)
        {
            bool previousValid = previous.IsValid;
            bool actionValid = action.IsValid;
            if (previousValid && previous.InstanceId != action.InstanceId)
                m_ActionInstanceIndices.Remove(previous.InstanceId);
            m_ActionInstanceIndices[action.InstanceId] = index;
            if (previous.InstanceId == action.InstanceId && previous.SkillId == action.SkillId &&
                previousValid == actionValid)
                return;
            if (previousValid && previous.SkillId.IsValid)
                m_ActionInstanceIndicesBySkill[previous.SkillId].Remove(index);
            if (actionValid && action.SkillId.IsValid)
                InsertSkillActionInstanceIndex(index, action.SkillId, action.InstanceId);
        }

        void InsertSkillActionInstanceIndex(int index, CharacterSkillId skillId, ulong instanceId)
        {
            List<int> indices = m_ActionInstanceIndicesBySkill[skillId];
            int lower = 0;
            int upper = indices.Count;
            while (lower < upper)
            {
                int middle = lower + ((upper - lower) >> 1);
                if (m_ActionInstances[indices[middle]].InstanceId < instanceId)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            indices.Insert(lower, index);
        }

        void ClearSkillActionInstanceIndices()
        {
            foreach (KeyValuePair<CharacterSkillId, List<int>> group in m_ActionInstanceIndicesBySkill)
                group.Value.Clear();
        }

        void RebuildActionInstanceIndex()
        {
            m_ActionInstanceIndices.Clear();
            ClearSkillActionInstanceIndices();
            for (int index = 0; index < m_ActionInstances.Count; index++)
            {
                Float32ActionInstanceState action = m_ActionInstances[index];
                if (!action.IsValid)
                    continue;
                if (!m_ActionInstanceIndices.TryAdd(action.InstanceId, index))
                    throw new InvalidOperationException($"Action instance '{action.InstanceId}' is duplicated.");
                if (action.SkillId.IsValid)
                    m_ActionInstanceIndicesBySkill[action.SkillId].Add(index);
            }
            foreach (KeyValuePair<CharacterSkillId, List<int>> group in m_ActionInstanceIndicesBySkill)
                group.Value.Sort(m_ActionInstanceIndexComparison);
        }

        internal void Dispose()
        {
            ReleaseRequests();
            ReleaseActions();
            m_ActionInstanceIndices.Clear();
            ClearSkillActionInstanceIndices();
            m_Disposed = true;
        }

        void ReleaseRequests()
        {
            for (int i = 0; i < m_ActionActivationRequests.Count; i++)
            {
                m_ActionActivationRequests[i].Release();
                m_ActionActivationRequests[i].TargetSnapshot.Release();
            }
            m_ActionActivationRequests.Clear();
        }

        void ReleaseActions()
        {
            for (int i = 0; i < m_ActionInstances.Count; i++)
                m_ActionInstances[i].Release();
            m_ActionInstances.Clear();
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32CharacterActionRuntimeState));
        }

        int CompareActionInstanceIndices(int left, int right) =>
            m_ActionInstances[left].InstanceId.CompareTo(m_ActionInstances[right].InstanceId);

        static bool Same<T>(List<T> left, ReadOnlySpan<T> right)
            where T : struct, IEquatable<T>
        {
            if (left.Count != right.Length)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                    return false;
            }
            return true;
        }
    }

    internal sealed class Float32ActionStatePageBank
    {
        readonly List<Float32ActionStatePage> m_Pages = new List<Float32ActionStatePage>();
        internal int ActionCapacity { get; }

        internal Float32ActionStatePageBank(int actionCapacity) => ActionCapacity = actionCapacity;

        internal void PrepareCapacity(int statePageCount)
        {
            if (statePageCount <= m_Pages.Count)
                return;
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Float32ActionStatePage(this, ActionCapacity));
        }

        internal Float32ActionStatePageReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                Float32ActionStatePage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Requests, 0, page.RequestCount);
                Array.Clear(page.Actions, 0, page.ActionCount);
                page.RequestCount = 0;
                page.ActionCount = 0;
                page.Generation++;
                return new Float32ActionStatePageReference(page, page.Generation);
            }
            throw new InvalidOperationException("Float32 Action state page capacity does not cover retained state versions and the active transaction.");
        }
    }

    internal sealed class Float32ActionStatePage
    {
        internal Float32ActionStatePage(Float32ActionStatePageBank bank, int capacity)
        {
            Bank = bank;
            Requests = new SimulationActionActivationRequestState[capacity];
            Actions = new Float32ActionInstanceState[capacity];
        }

        internal readonly Float32ActionStatePageBank Bank;
        internal readonly SimulationActionActivationRequestState[] Requests;
        internal readonly Float32ActionInstanceState[] Actions;
        internal int RequestCount;
        internal int ActionCount;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct Float32ActionStatePageReference
    {
        internal Float32ActionStatePageReference(Float32ActionStatePage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal Float32ActionStatePage Page { get; }
        internal ulong Generation { get; }
        internal ReadOnlySpan<SimulationActionActivationRequestState> Requests
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<SimulationActionActivationRequestState>(Page.Requests, 0, Page.RequestCount);
            }
        }
        internal ReadOnlySpan<Float32ActionInstanceState> Actions
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<Float32ActionInstanceState>(Page.Actions, 0, Page.ActionCount);
            }
        }

        internal void Retain()
        {
            RequireReadable();
            if (Page.Retainers++ != 0)
                return;
            for (int i = 0; i < Page.RequestCount; i++)
            {
                Page.Requests[i].Retain();
                Page.Requests[i].TargetSnapshot.Retain();
            }
            for (int i = 0; i < Page.ActionCount; i++)
                Page.Actions[i].Retain();
        }

        internal void Release()
        {
            if (--Page.Retainers != 0)
                return;
            for (int i = 0; i < Page.RequestCount; i++)
            {
                Page.Requests[i].Release();
                Page.Requests[i].TargetSnapshot.Release();
            }
            for (int i = 0; i < Page.ActionCount; i++)
                Page.Actions[i].Release();
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("Float32 Action state page is outside its retained lifetime.");
        }
    }
}
