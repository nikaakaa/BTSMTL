using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.FPGameplay.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPCharacterActionRuntimeState : IFPActionRuntimeStatePort
    {
        readonly List<SimulationActionActivationRequestState> m_ActionActivationRequests;
        readonly List<FPActionInstanceState> m_ActionInstances;
        readonly int m_Capacity;
        readonly Dictionary<ulong, int> m_ActionInstanceIndices;
        readonly Dictionary<CharacterSkillId, List<int>> m_ActionInstanceIndicesBySkill;
        readonly Comparison<int> m_ActionInstanceIndexComparison;
        bool m_ActivationRequestsWritten;
        bool m_InstancesWritten;
        ulong m_ActionEventSequence;
        FPActionStatePageReference m_BaseState;
        bool m_Disposed;

        public FPCharacterActionRuntimeState(
            int capacity,
            IReadOnlyList<FPGameplayAbilityExecutionInstallation> installations)
        {
            m_Capacity = capacity;
            m_ActionActivationRequests = new List<SimulationActionActivationRequestState>(capacity);
            m_ActionInstances = new List<FPActionInstanceState>(capacity);
            m_ActionInstanceIndices = new Dictionary<ulong, int>(capacity);
            m_ActionInstanceIndicesBySkill = new Dictionary<CharacterSkillId, List<int>>(installations.Count);
            m_ActionInstanceIndexComparison = CompareActionInstanceIndices;
            for (int i = 0; i < installations.Count; i++)
            {
                FPGameplayAbilityExecutionInstallation installation = installations[i];
                m_ActionInstanceIndicesBySkill.Add(installation.Identity.AbilityId,
                    new List<int>(installation.Layout.ActionInstanceCapacity));
            }
        }

        public FPCharacterActionRuntimeState Restart(
            FPActionStatePageReference state,
            ulong actionEventSequence)
        {
            m_BaseState = state;
            ReadOnlySpan<SimulationActionActivationRequestState> requests = state.Requests;
            if (!Same(m_ActionActivationRequests, requests))
            {
                m_ActionActivationRequests.Clear();
                for (int index = 0; index < requests.Length; index++)
                    m_ActionActivationRequests.Add(requests[index]);
            }
            ReadOnlySpan<FPActionInstanceState> actions = state.Actions;
            if (!Same(m_ActionInstances, actions))
            {
                m_ActionInstances.Clear();
                for (int index = 0; index < actions.Length; index++)
                    m_ActionInstances.Add(actions[index]);
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

        public IReadOnlyList<FPActionInstanceState> GetActionInstances()
        {
            RequireActive();
            return m_ActionInstances;
        }

        public bool TryGetActionInstance(ulong instanceId, out FPActionInstanceState action)
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

        public void ReplaceActionInstanceAt(int index, FPActionInstanceState action)
        {
            RequireActive();
            if (index < 0 || index >= m_ActionInstances.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            FPActionInstanceState previous = m_ActionInstances[index];
            if (previous.Equals(action))
                return;
            m_ActionInstances[index] = action;
            UpdateActionInstanceIndex(index, previous, action);
            m_InstancesWritten = true;
        }

        public void AddActionInstance(FPActionInstanceState action)
        {
            RequireActive();
            if (m_ActionInstances.Count >= m_Capacity)
                throw new InvalidOperationException("Character action state capacity is exhausted.");
            m_ActionInstances.Add(action);
            UpdateActionInstanceIndex(m_ActionInstances.Count - 1, default, action);
            m_InstancesWritten = true;
        }

        public void AddActivationRequest(SimulationActionActivationRequestState request)
        {
            RequireActive();
            if (m_ActionActivationRequests.Count >= m_Capacity)
                throw new InvalidOperationException("Character action state capacity is exhausted.");
            m_ActionActivationRequests.Add(request);
            m_ActivationRequestsWritten = true;
        }

        public void RemoveActivationRequestAt(int index)
        {
            RequireActive();
            if (index < 0 || index >= m_ActionActivationRequests.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
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

        internal FPActionStatePageReference Capture()
        {
            RequireActive();
            bool requestsUnchanged = !m_ActivationRequestsWritten || Same(m_ActionActivationRequests, m_BaseState.Requests);
            bool actionsUnchanged = !m_InstancesWritten || Same(m_ActionInstances, m_BaseState.Actions);
            if (requestsUnchanged && actionsUnchanged)
                return m_BaseState;
            FPActionStatePageReference state = m_BaseState.Page.Bank.Begin();
            m_ActionActivationRequests.CopyTo(state.Page.Requests);
            m_ActionInstances.CopyTo(state.Page.Actions);
            state.Page.RequestCount = m_ActionActivationRequests.Count;
            state.Page.ActionCount = m_ActionInstances.Count;
            return state;
        }

        internal void RestoreActivation(FPActionStatePageReference state, ulong actionEventSequence)
        {
            ReadOnlySpan<SimulationActionActivationRequestState> requests = state.Requests;
            ReadOnlySpan<FPActionInstanceState> actions = state.Actions;
            m_ActionActivationRequests.Clear();
            for (int i = 0; i < requests.Length; i++)
                m_ActionActivationRequests.Add(requests[i]);
            m_ActionInstances.Clear();
            for (int i = 0; i < actions.Length; i++)
                m_ActionInstances.Add(actions[i]);
            RebuildActionInstanceIndex();
            m_ActionEventSequence = actionEventSequence;
            m_ActivationRequestsWritten = !Same(m_ActionActivationRequests, m_BaseState.Requests);
            m_InstancesWritten = !Same(m_ActionInstances, m_BaseState.Actions);
        }

        void UpdateActionInstanceIndex(
            int index,
            FPActionInstanceState previous,
            FPActionInstanceState action)
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
                FPActionInstanceState action = m_ActionInstances[index];
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
            m_ActionActivationRequests.Clear();
            m_ActionInstances.Clear();
            m_ActionInstanceIndices.Clear();
            ClearSkillActionInstanceIndices();
            m_BaseState = default;
            m_Disposed = true;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FPCharacterActionRuntimeState));
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

    internal sealed class FPActionStatePageBank
    {
        readonly List<FPActionStatePage> m_Pages = new List<FPActionStatePage>();
        internal int ActionCapacity { get; }

        internal FPActionStatePageBank(int actionCapacity)
        {
            ActionCapacity = actionCapacity;
        }

        internal void PrepareCapacity(int statePageCount)
        {
            if (statePageCount <= m_Pages.Count)
                return;
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new FPActionStatePage(this, ActionCapacity));
        }

        internal FPActionStatePageReference Begin()
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FPActionStatePage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Requests, 0, page.RequestCount);
                Array.Clear(page.Actions, 0, page.ActionCount);
                page.RequestCount = 0;
                page.ActionCount = 0;
                page.Generation++;
                return new FPActionStatePageReference(page, page.Generation);
            }
            throw new InvalidOperationException("FP Action state page capacity does not cover the active transaction.");
        }
    }

    internal sealed class FPActionStatePage
    {
        internal FPActionStatePage(FPActionStatePageBank bank, int capacity)
        {
            Bank = bank;
            Requests = new SimulationActionActivationRequestState[capacity];
            Actions = new FPActionInstanceState[capacity];
        }

        internal readonly FPActionStatePageBank Bank;
        internal readonly SimulationActionActivationRequestState[] Requests;
        internal readonly FPActionInstanceState[] Actions;
        internal int RequestCount;
        internal int ActionCount;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FPActionStatePageReference
    {
        internal FPActionStatePageReference(FPActionStatePage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FPActionStatePage Page { get; }
        internal ulong Generation { get; }
        internal ReadOnlySpan<SimulationActionActivationRequestState> Requests
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<SimulationActionActivationRequestState>(Page.Requests, 0, Page.RequestCount);
            }
        }
        internal ReadOnlySpan<FPActionInstanceState> Actions
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<FPActionInstanceState>(Page.Actions, 0, Page.ActionCount);
            }
        }

        internal void Retain()
        {
            RequireReadable();
            if (Page.Retainers++ == 0)
            {
                for (int index = 0; index < Page.RequestCount; index++)
                {
                    Page.Requests[index].Retain();
                    Page.Requests[index].TargetSnapshot.Retain();
                }
                for (int index = 0; index < Page.ActionCount; index++)
                    Page.Actions[index].Retain();
            }
        }

        internal void Release()
        {
            if (--Page.Retainers != 0)
                return;
            for (int index = 0; index < Page.RequestCount; index++)
            {
                Page.Requests[index].Release();
                Page.Requests[index].TargetSnapshot.Release();
            }
            for (int index = 0; index < Page.ActionCount; index++)
                Page.Actions[index].Release();
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("FP Action state page is outside its retained lifetime.");
        }
    }
}
