using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32SimulationInputFrameStorage
    {
        readonly SimulationInputValues m_Data;
        CanonicalBuffer<SimulationInputValue> m_Values => m_Data.Values;
        CanonicalBuffer<SimulationInputRequest> m_Requests => m_Data.Requests;
        readonly SimulationInput m_Snapshot;
        readonly SimulationInputValue[] m_OwnedValues;
        readonly SimulationInputRequest[] m_OwnedRequests;
        internal SimulationNumericProfile NumericProfile { get; private set; }
        internal SimulationInputHeader Header { get; private set; }
        internal SimulationTickSourceIdentity TickSource => Header.TickSource;
        internal CanonicalTextValue InputSourceIdentity => Header.InputSourceIdentity;
        internal ulong Sequence => Header.Sequence;
        internal IReadOnlyList<SimulationInputValue> Values => m_Values;
        internal IReadOnlyList<SimulationInputRequest> Requests => m_Requests;
        internal SimulationInputValue[] OwnedValues => m_OwnedValues;
        internal SimulationInputRequest[] OwnedRequests => m_OwnedRequests;
        internal SimulationInputValues CanonicalValues => m_Data;

        public Float32SimulationInputFrameStorage(int values, int requests)
        {
            m_Data = new SimulationInputValues(values, requests);
            m_Snapshot = new SimulationInput(this);
        }

        internal Float32SimulationInputFrameStorage(SimulationNumericProfile profile, SimulationTickSourceIdentity source,
            CanonicalTextValue identity, ulong sequence, SimulationInputValue[] values, SimulationInputRequest[] requests)
        {
            NumericProfile = profile;
            Header = new SimulationInputHeader(source, identity, sequence);
            m_Data = new SimulationInputValues(values, requests);
            m_OwnedValues = values;
            m_OwnedRequests = requests;
        }

        internal Float32SimulationInputFrameStorage(SimulationNumericProfile profile, in SimulationInputHeader header,
            SimulationInputValues data)
        {
            NumericProfile = profile;
            Header = header;
            m_Data = data;
            m_OwnedValues = data.Values.OwnedArray;
            m_OwnedRequests = data.Requests.OwnedArray;
        }

        internal void Begin()
        {
            SimulationInputValues.CanonicalRelease(m_Data);
            SimulationInputHeader.CanonicalRelease(Header);
            m_Values.Clear(); m_Requests.Clear();
            Header = default;
        }

        internal Float32SimulationInputFrameStorage BeginFrame() { Begin(); return this; }

        public void AddValue(in SimulationInputValue value)
        {
            m_Values.Add(value); value.Retain();
        }

        public void AddRequest(in SimulationInputRequest request)
        {
            m_Requests.Add(request); SimulationInputRequest.CanonicalRetain(in request);
        }

        public SimulationInput Complete(SimulationNumericProfile profile, SimulationTickSourceIdentity source,
            CanonicalTextValue identity, ulong sequence)
        {
            SortValues(); SortRequests();
            for (int index = 1; index < m_Values.Count; index++)
                if (m_Values[index - 1].InputId == m_Values[index].InputId) throw new ArgumentException("Duplicate simulation input value.");
            for (int index = 1; index < m_Requests.Count; index++)
                if (m_Requests[index - 1].Sequence == m_Requests[index].Sequence) throw new ArgumentException("Duplicate simulation input request sequence.");
            return CommitMetadata(profile, source, identity, sequence);
        }

        SimulationInput CommitMetadata(SimulationNumericProfile profile, SimulationTickSourceIdentity source,
            CanonicalTextValue identity, ulong sequence)
        {
            return CommitMetadata(profile, new SimulationInputHeader(source, identity, sequence));
        }

        SimulationInput CommitMetadata(SimulationNumericProfile profile, in SimulationInputHeader header)
        {
            SimulationInputHeader.CanonicalRetain(in header);
            NumericProfile = profile;
            Header = header;
            return m_Snapshot;
        }

        internal SimulationInput CompleteRead(SimulationNumericProfile profile, in SimulationInputHeader header) =>
            CommitMetadata(profile, in header);

        internal SimulationInput Capture(SimulationInput input)
        {
            Begin();
            for (int index = 0; index < input.Values.Count; index++) AddValue(input.Values[index]);
            for (int index = 0; index < input.Requests.Count; index++) AddRequest(input.Requests[index]);
            return CommitMetadata(input.NumericProfile, input.TickSource, input.InputSourceIdentity, input.Sequence);
        }

        internal SimulationInput Remap(SimulationInput input, SimulationInputBuildContext context,
            CanonicalTextValue identity, ulong recordedTick)
        {
            Begin();
            for (int index = 0; index < input.Values.Count; index++) AddValue(input.Values[index]);
            for (int index = 0; index < input.Requests.Count; index++)
            {
                SimulationInputRequest request = input.Requests[index];
                AddRequest(new SimulationInputRequest(request.RequestId, request.Sequence,
                    RemapTick(request.SourceTick, recordedTick, context.SimulationTick.Value),
                    RemapTick(request.ExpireSimulationTick, recordedTick, context.SimulationTick.Value), request.Priority));
            }
            return Complete(input.NumericProfile, context.Source, identity, context.InputSequence);
        }

        static ulong RemapTick(ulong value, ulong before, ulong after) =>
            value == 0 || value < before ? value : checked(after + value - before);
        internal void Release() => Begin();

        void SortValues()
        {
            for (int index = m_Values.Count / 2 - 1; index >= 0; index--) SiftValues(index, m_Values.Count);
            for (int end = m_Values.Count - 1; end > 0; end--)
            { SimulationInputValue value = m_Values[end]; m_Values[end] = m_Values[0]; m_Values[0] = value; SiftValues(0, end); }
        }
        void SiftValues(int root, int count)
        {
            while (root * 2 + 1 < count)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && CanonicalTextValue.CompareOrdinal(m_Values[child].InputId, m_Values[child + 1].InputId) < 0) child++;
                if (CanonicalTextValue.CompareOrdinal(m_Values[root].InputId, m_Values[child].InputId) >= 0) break;
                SimulationInputValue value = m_Values[root]; m_Values[root] = m_Values[child]; m_Values[child] = value; root = child;
            }
        }
        void SortRequests()
        {
            for (int index = m_Requests.Count / 2 - 1; index >= 0; index--) SiftRequests(index, m_Requests.Count);
            for (int end = m_Requests.Count - 1; end > 0; end--)
            { SimulationInputRequest value = m_Requests[end]; m_Requests[end] = m_Requests[0]; m_Requests[0] = value; SiftRequests(0, end); }
        }
        void SiftRequests(int root, int count)
        {
            while (root * 2 + 1 < count)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && CompareRequests(m_Requests[child], m_Requests[child + 1]) < 0) child++;
                if (CompareRequests(m_Requests[root], m_Requests[child]) >= 0) break;
                SimulationInputRequest value = m_Requests[root]; m_Requests[root] = m_Requests[child]; m_Requests[child] = value; root = child;
            }
        }
        static int CompareRequests(SimulationInputRequest left, SimulationInputRequest right)
        {
            int order = left.Sequence.CompareTo(right.Sequence);
            return order != 0 ? order : CanonicalTextValue.CompareOrdinal(left.RequestId, right.RequestId);
        }
    }

}
