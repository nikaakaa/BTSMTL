using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCharacterInputRequestState : IFixedInputRequestStatePort
    {
        SimulationTick m_Tick;
        IReadOnlyList<string> m_RequestIds;
        FixedInputRequestPageReference m_BaseRequests;
        FixedInputRequestPageReference m_Requests;
        int m_ChangedRequestCount;
        bool m_Disposed;

        internal void Prepare(IReadOnlyList<string> requestIds)
        {
            m_RequestIds = requestIds;
        }

        public FixedCharacterInputRequestState Restart(
            SimulationTick tick,
            FixedInputRequestPageReference requests)
        {
            if (!tick.IsValid)
                throw new ArgumentException("Fixed Character input request state tick is invalid.", nameof(tick));
            m_Tick = tick;
            m_BaseRequests = requests;
            m_Requests = requests;
            m_ChangedRequestCount = 0;
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
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> baseline = m_BaseRequests.Values;
            int baselineIndex = FindRequest(baseline, requestId);
            bool wasChanged = index >= 0 &&
                (baselineIndex < 0 || !requests[index].Value.Equals(baseline[baselineIndex].Value));
            bool isChanged = baselineIndex < 0 || !state.Equals(baseline[baselineIndex].Value);
            m_ChangedRequestCount += (isChanged ? 1 : 0) - (wasChanged ? 1 : 0);
            if (ReferenceEquals(m_Requests.Page, m_BaseRequests.Page) || m_Requests.Page.Retainers > 1)
            {
                FixedInputRequestPageReference next = m_BaseRequests.Page.Bank.Begin(requests.Length);
                requests.CopyTo(next.Page.Values);
                next.Retain();
                if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                    m_Requests.Release();
                m_Requests = next;
            }
            FixedInputRequestPage page = m_Requests.Page;
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
                int index = FindRequest(m_Requests.Values, request.RequestId);
                if (index < 0)
                    continue;
                SimulationInputRequestState state = m_Requests.Page.Values[index].Value;
                if (!state.IsValid || request.Priority > state.Priority ||
                    request.Priority == state.Priority && request.Sequence > state.Sequence)
                {
                    SetInputRequest(request.RequestId, new SimulationInputRequestState(
                        request.RequestId,
                        request.Sequence,
                        request.SourceTick,
                        request.ExpireSimulationTick,
                        request.Priority));
                }
            }
        }

        internal FixedInputRequestPageReference Capture()
        {
            RequireActive();
            return m_ChangedRequestCount == 0 ? m_BaseRequests : m_Requests;
        }

        internal FixedInputRequestPageReference CaptureActivation(out int changedRequestCount)
        {
            changedRequestCount = m_ChangedRequestCount;
            return m_Requests;
        }

        internal void RestoreActivation(FixedInputRequestPageReference requests, int changedRequestCount)
        {
            if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                m_Requests.Release();
            m_Requests = requests;
            if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                m_Requests.Retain();
            m_ChangedRequestCount = changedRequestCount;
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
                throw new ObjectDisposedException(nameof(FixedCharacterInputRequestState));
        }

    }

    internal sealed class FixedInputRequestPageBank
    {
        readonly List<FixedInputRequestPage> m_Pages = new List<FixedInputRequestPage>();
        internal int RequestCapacity { get; private set; }

        internal FixedInputRequestPageBank(int requestCapacity)
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

        internal void PrepareCapacity(int statePageCount)
        {
            if (statePageCount <= m_Pages.Count)
                return;
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new FixedInputRequestPage(this, RequestCapacity));
        }

        internal FixedInputRequestPageReference Begin(int count)
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                FixedInputRequestPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Values, 0, page.Count);
                page.Count = count;
                page.Generation++;
                return new FixedInputRequestPageReference(page, page.Generation);
            }
            throw new InvalidOperationException("Fixed Input request page capacity does not cover the active transaction.");
        }
    }

    internal sealed class FixedInputRequestPage
    {
        internal FixedInputRequestPage(FixedInputRequestPageBank bank, int capacity)
        {
            Bank = bank;
            Values = new KeyValuePair<string, SimulationInputRequestState>[capacity];
        }

        internal readonly FixedInputRequestPageBank Bank;
        internal KeyValuePair<string, SimulationInputRequestState>[] Values;
        internal int Count;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct FixedInputRequestPageReference
    {
        internal FixedInputRequestPageReference(FixedInputRequestPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal FixedInputRequestPage Page { get; }
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
                for (int index = 0; index < Page.Count; index++)
                    Page.Values[index].Value.Retain();
        }

        internal void Release()
        {
            if (--Page.Retainers != 0)
                return;
            for (int index = 0; index < Page.Count; index++)
                Page.Values[index].Value.Release();
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("Fixed Input request page is outside its retained lifetime.");
        }
    }
}
