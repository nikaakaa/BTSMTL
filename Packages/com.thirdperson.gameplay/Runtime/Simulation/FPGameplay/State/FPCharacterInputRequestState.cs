using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using ThirdPersonCharacter.Pipeline.Input;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPCharacterInputRequestState : IFPInputRequestStatePort
    {
        SimulationTick m_Tick;
        IReadOnlyList<string> m_RequestIds;
        FPInputRequestPageReference m_BaseRequests;
        FPInputRequestPageReference m_Requests;
        int m_OffensiveRequestDelayTicks;
        bool m_Disposed;

        internal void Prepare(IReadOnlyList<string> requestIds)
        {
            m_RequestIds = requestIds;
        }

        internal void PrepareTiming(int offensiveRequestDelayTicks)
        {
            m_OffensiveRequestDelayTicks = offensiveRequestDelayTicks;
        }

        public FPCharacterInputRequestState Restart(
            SimulationTick tick,
            FPInputRequestPageReference requests)
        {
            if (!tick.IsValid)
                throw new ArgumentException("FP Character input request state tick is invalid.", nameof(tick));
            m_Tick = tick;
            m_BaseRequests = requests;
            m_Requests = requests;
            m_Disposed = false;
            return this;
        }

        public SimulationTick Tick => m_Tick;

        public SimulationInputRequestState GetInputRequest(CanonicalTextValue requestId)
        {
            RequireActive();
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests = m_Requests.Values;
            int index = FindRequest(requests, requestId);
            return index >= 0
                ? requests[index].Value
                : default;
        }

        public void SetInputRequest(CanonicalTextValue requestId, SimulationInputRequestState state)
        {
            RequireActive();
            string declaredId = null;
            for (int key = 0; key < m_RequestIds.Count; key++)
                if (requestId.Equals(m_RequestIds[key]))
                {
                    declaredId = m_RequestIds[key];
                    break;
                }
            if (declaredId == null)
                throw new InvalidOperationException("Input request write identity is absent from its published binding.");
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests = m_Requests.Values;
            int index = FindRequest(requests, requestId);
            bool replacing = index >= 0;
            if (index >= 0 && requests[index].Value.Equals(state))
                return;
            FPInputRequestPage page = GetWritablePage();
            if (index < 0)
            {
                index = ~index;
                Array.Copy(page.Values, index, page.Values, index + 1, page.Count - index);
                page.Count++;
            }
            state.Retain();
            if (replacing)
                page.Values[index].Value.Release();
            page.Values[index] = new KeyValuePair<string, SimulationInputRequestState>(declaredId, state);
        }

        internal void ApplyRequests(IReadOnlyList<SimulationInputRequest> requests)
        {
            for (int keyIndex = 0; keyIndex < m_RequestIds.Count; keyIndex++)
            {
                CanonicalTextValue requestId = m_RequestIds[keyIndex];
                int index = FindRequest(m_Requests.Values, requestId);
                if (index < 0)
                {
                    SetInputRequest(requestId, default);
                    continue;
                }
                SimulationInputRequestState state = m_Requests.Page.Values[index].Value;
                if (state.IsValid && state.ExpireTick < m_Tick.Value)
                    SetInputRequest(requestId, default);
            }
            for (int inputIndex = 0; inputIndex < requests.Count; inputIndex++)
            {
                SimulationInputRequest request = requests[inputIndex];
                if (request.Sequence <= m_Requests.Page.LastAcceptedSequence)
                    continue;
                FPInputRequestPage page = GetWritablePage();
                if (page.PendingCount == page.PendingRequests.Length)
                    throw new InvalidOperationException("FP input request FIFO exceeds its prepared capture and delay capacity.");
                request.Retain();
                page.PendingRequests[page.PendingCount++] = request;
                page.LastAcceptedSequence = request.Sequence;
            }
            if (m_Requests.Page.PendingCount == 0)
                return;
            FPInputRequestPage pendingPage = m_Requests.Page;
            int released = 0;
            for (; released < pendingPage.PendingCount; released++)
            {
                SimulationInputRequest request = pendingPage.PendingRequests[released];
                ulong eligibleTick = checked(request.SourceTick +
                    (request.TimingClass == CharacterActionRequestTimingClass.Offensive
                        ? (ulong)m_OffensiveRequestDelayTicks : 0UL));
                if (eligibleTick > m_Tick.Value)
                    break;
                if (released == 0)
                    pendingPage = GetWritablePage();
                int index = FindRequest(m_Requests.Values, request.RequestId);
                SimulationInputRequestState state = m_Requests.Page.Values[index].Value;
                if (!state.IsValid || request.Priority > state.Priority ||
                    request.Priority == state.Priority && request.Sequence > state.Sequence)
                {
                    SetInputRequest(request.RequestId, new SimulationInputRequestState(
                        request.RequestId,
                        request.Sequence,
                        request.SourceTick,
                        checked(m_Tick.Value + request.ExpireSimulationTick - request.SourceTick),
                        request.Priority));
                }
                request.Release();
            }
            if (released == 0)
                return;
            int remaining = pendingPage.PendingCount - released;
            Array.Copy(pendingPage.PendingRequests, released, pendingPage.PendingRequests, 0, remaining);
            Array.Clear(pendingPage.PendingRequests, remaining, released);
            pendingPage.PendingCount = remaining;
        }

        internal FPInputRequestPageReference Capture()
        {
            RequireActive();
            return m_Requests;
        }

        FPInputRequestPage GetWritablePage()
        {
            FPInputRequestPage current = m_Requests.Page;
            if (!ReferenceEquals(current, m_BaseRequests.Page) && current.Retainers == 1)
                return current;
            FPInputRequestPageReference next = current.Bank.Begin(current.Count);
            Array.Copy(current.Values, next.Page.Values, current.Count);
            Array.Copy(current.PendingRequests, next.Page.PendingRequests, current.PendingCount);
            next.Page.PendingCount = current.PendingCount;
            next.Page.LastAcceptedSequence = current.LastAcceptedSequence;
            next.Retain();
            if (!ReferenceEquals(current, m_BaseRequests.Page))
                m_Requests.Release();
            m_Requests = next;
            return next.Page;
        }

        internal void RestoreActivation(FPInputRequestPageReference requests)
        {
            if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                m_Requests.Release();
            m_Requests = requests;
            if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                m_Requests.Retain();
        }

        internal void Dispose()
        {
            if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                m_Requests.Release();
            m_Disposed = true;
        }

        static int FindRequest(ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests, CanonicalTextValue requestId)
        {
            int lower = 0;
            int upper = requests.Length - 1;
            while (lower <= upper)
            {
                int index = lower + ((upper - lower) >> 1);
                int comparison = CanonicalTextValue.CompareOrdinal(requests[index].Key, requestId);
                if (comparison == 0)
                    return index;
                if (comparison < 0)
                    lower = index + 1;
                else
                    upper = index - 1;
            }
            return ~lower;
        }

        void RequireActive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FPCharacterInputRequestState));
        }

    }

    internal sealed class FPInputRequestPageBank
    {
        readonly List<FPInputRequestPage> m_Pages = new List<FPInputRequestPage>();
        internal int RequestCapacity { get; private set; }
        internal int PendingRequestCapacity { get; private set; }

        internal FPInputRequestPageBank(int requestCapacity)
        {
            RequestCapacity = requestCapacity;
        }

        internal void PrepareRequestCapacity(int requestCapacity)
        {
            if (requestCapacity <= RequestCapacity)
                return;
            RequestCapacity = requestCapacity;
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].Values = new KeyValuePair<string, SimulationInputRequestState>[requestCapacity];
        }

        internal void PreparePendingRequestCapacity(int pendingRequestCapacity)
        {
            PendingRequestCapacity = pendingRequestCapacity;
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].PendingRequests = new SimulationInputRequest[pendingRequestCapacity];
        }

        internal void PrepareCapacity(int statePageCount)
        {
            if (statePageCount <= m_Pages.Count)
                return;
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new FPInputRequestPage(this, RequestCapacity, PendingRequestCapacity));
        }

        internal FPInputRequestPageReference Begin(int count)
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FPInputRequestPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Values, 0, page.Count);
                Array.Clear(page.PendingRequests, 0, page.PendingCount);
                page.Count = count;
                page.PendingCount = 0;
                page.LastAcceptedSequence = 0;
                page.Generation++;
                return new FPInputRequestPageReference(page, page.Generation);
            }
            throw new InvalidOperationException("FP Input request page capacity does not cover the active transaction.");
        }
    }

    internal sealed class FPInputRequestPage
    {
        internal FPInputRequestPage(FPInputRequestPageBank bank, int capacity, int pendingRequestCapacity)
        {
            Bank = bank;
            Values = new KeyValuePair<string, SimulationInputRequestState>[capacity];
            PendingRequests = new SimulationInputRequest[pendingRequestCapacity];
        }

        internal readonly FPInputRequestPageBank Bank;
        internal KeyValuePair<string, SimulationInputRequestState>[] Values;
        internal SimulationInputRequest[] PendingRequests;
        internal int Count;
        internal int PendingCount;
        internal ulong LastAcceptedSequence;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FPInputRequestPageReference
    {
        internal FPInputRequestPageReference(FPInputRequestPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FPInputRequestPage Page { get; }
        internal ulong Generation { get; }
        internal ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> Values
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>>(Page.Values, 0, Page.Count);
            }
        }

        internal void Retain()
        {
            RequireReadable();
            if (Page.Retainers++ == 0)
            {
                for (int index = 0; index < Page.Count; index++)
                    Page.Values[index].Value.Retain();
                for (int index = 0; index < Page.PendingCount; index++)
                    Page.PendingRequests[index].Retain();
            }
        }

        internal void Release()
        {
            if (--Page.Retainers != 0)
                return;
            for (int index = 0; index < Page.Count; index++)
                Page.Values[index].Value.Release();
            for (int index = 0; index < Page.PendingCount; index++)
                Page.PendingRequests[index].Release();
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("FP Input request page is outside its retained lifetime.");
        }
    }
}
