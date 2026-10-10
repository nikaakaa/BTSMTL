using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterInputRequestState : IFloat32InputRequestStatePort
    {
        SimulationTick m_Tick;
        IReadOnlyList<string> m_RequestIds;
        Float32InputRequestPageReference m_BaseRequests;
        Float32InputRequestPageReference m_Requests;
        int m_ChangedRequestCount;
        bool m_Disposed;

        internal void Prepare(IReadOnlyList<string> requestIds)
        {
            m_RequestIds = requestIds;
        }

        public Float32CharacterInputRequestState Restart(
            SimulationTick tick,
            Float32InputRequestPageReference requests)
        {
            if (!tick.IsValid)
                throw new ArgumentException("Float32 Character input request state tick is invalid.", nameof(tick));
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
            ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> requests = m_Requests.Values;
            int index = FindRequest(requests, requestId);
            return index >= 0
                ? requests[index].Value
                : default;
        }

        public void SetInputRequest(CanonicalTextValue requestId, SimulationInputRequestState state)
        {
            RequireActive();
            ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> requests = m_Requests.Values;
            int index = FindRequest(requests, requestId);
            if (index >= 0 && requests[index].Value.Equals(state))
                return;
            ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> baseline = m_BaseRequests.Values;
            int baselineIndex = FindRequest(baseline, requestId);
            bool wasChanged = index >= 0 &&
                (baselineIndex < 0 || !requests[index].Value.Equals(baseline[baselineIndex].Value));
            bool isChanged = baselineIndex < 0 || !state.Equals(baseline[baselineIndex].Value);
            m_ChangedRequestCount += (isChanged ? 1 : 0) - (wasChanged ? 1 : 0);
            if (ReferenceEquals(m_Requests.Page, m_BaseRequests.Page) || m_Requests.Page.Retainers > 1)
            {
                Float32InputRequestPageReference next = m_BaseRequests.Page.Bank.Begin(requests.Length);
                requests.CopyTo(next.Page.Values);
                if (!ReferenceEquals(m_Requests.Page, m_BaseRequests.Page))
                    m_Requests.Release();
                next.Retain();
                m_Requests = next;
            }
            Float32InputRequestPage page = m_Requests.Page;
            if (index < 0)
            {
                index = ~index;
                Array.Copy(page.Values, index, page.Values, index + 1, page.Count - index);
                page.Count++;
            }
            requestId.Retain();
            state.Retain();
            page.Values[index].Key.Release();
            page.Values[index].Value.Release();
            page.Values[index] = new KeyValuePair<CanonicalTextValue, SimulationInputRequestState>(requestId, state);
        }

        internal void ApplyRequests(IReadOnlyList<SimulationInputRequest> requests)
        {
            for (int keyIndex = 0; keyIndex < m_RequestIds.Count; keyIndex++)
            {
                CanonicalTextValue requestId = m_RequestIds[keyIndex];
                SimulationInputRequestState state = GetInputRequest(requestId);
                if (state.IsValid && state.ExpireTick < m_Tick.Value)
                    state = default;
                SetInputRequest(requestId, state);
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

        internal Float32InputRequestPageReference Capture()
        {
            RequireActive();
            return m_ChangedRequestCount == 0 ? m_BaseRequests : m_Requests;
        }

        internal Float32InputRequestPageReference CaptureActivation(out int changedRequestCount)
        {
            changedRequestCount = m_ChangedRequestCount;
            return m_Requests;
        }

        internal void RestoreActivation(Float32InputRequestPageReference requests, int changedRequestCount)
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

        static int FindRequest(ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> requests, CanonicalTextValue requestId)
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
                throw new ObjectDisposedException(nameof(Float32CharacterInputRequestState));
        }
    }

    internal sealed class Float32InputRequestPageBank
    {
        readonly List<Float32InputRequestPage> m_Pages = new List<Float32InputRequestPage>();
        internal int RequestCapacity { get; private set; }

        internal Float32InputRequestPageBank(int requestCapacity)
        {
            RequestCapacity = requestCapacity;
        }

        internal void PrepareRequestCapacity(int requestCapacity)
        {
            if (requestCapacity <= RequestCapacity)
                return;
            RequestCapacity = requestCapacity;
            for (int index = 0; index < m_Pages.Count; index++)
                m_Pages[index].Values = new KeyValuePair<CanonicalTextValue, SimulationInputRequestState>[requestCapacity];
        }

        internal void PrepareCapacity(int statePageCount)
        {
            if (statePageCount <= m_Pages.Count)
                return;
            m_Pages.Capacity = statePageCount;
            while (m_Pages.Count < statePageCount)
                m_Pages.Add(new Float32InputRequestPage(this, RequestCapacity));
        }

        internal Float32InputRequestPageReference Begin(int count)
        {
            for (int index = 0; index < m_Pages.Count; index++)
            {
                Float32InputRequestPage page = m_Pages[index];
                if (page.Retainers != 0)
                    continue;
                Array.Clear(page.Values, 0, page.Count);
                page.Count = count;
                page.Generation++;
                return new Float32InputRequestPageReference(page, page.Generation);
            }
            throw new InvalidOperationException("Float32 Input request page capacity does not cover the active transaction.");
        }
    }

    internal sealed class Float32InputRequestPage
    {
        internal Float32InputRequestPage(Float32InputRequestPageBank bank, int capacity)
        {
            Bank = bank;
            Values = new KeyValuePair<CanonicalTextValue, SimulationInputRequestState>[capacity];
        }

        internal readonly Float32InputRequestPageBank Bank;
        internal KeyValuePair<CanonicalTextValue, SimulationInputRequestState>[] Values;
        internal int Count;
        internal ulong Generation;
        internal int Retainers;
    }

    internal readonly struct Float32InputRequestPageReference
    {
        internal Float32InputRequestPageReference(Float32InputRequestPage page, ulong generation)
        {
            Page = page;
            Generation = generation;
        }

        internal Float32InputRequestPage Page { get; }
        internal ulong Generation { get; }
        internal ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> Values
        {
            get
            {
                RequireReadable();
                return new ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>>(Page.Values, 0, Page.Count);
            }
        }

        internal void Retain()
        {
            RequireReadable();
            if (Page.Retainers++ == 0)
                for (int i = 0; i < Page.Count; i++)
                    { Page.Values[i].Key.Retain(); Page.Values[i].Value.Retain(); }
        }

        internal void Release()
        {
            if (--Page.Retainers == 0)
                for (int i = 0; i < Page.Count; i++)
                    { Page.Values[i].Key.Release(); Page.Values[i].Value.Release(); }
        }

        void RequireReadable()
        {
            if (Generation != Page.Generation)
                throw new InvalidOperationException("Float32 Input request page is outside its retained lifetime.");
        }
    }
}
