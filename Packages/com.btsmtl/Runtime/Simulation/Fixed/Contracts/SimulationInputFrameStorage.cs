using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class SimulationInputFrameStorage
    {
        readonly SimulationInputValues m_Data;
        readonly bool m_Owned;
        CanonicalBuffer<SimulationInputValue> m_Values => m_Data.Values;
        CanonicalBuffer<SimulationInputRequest> m_Requests => m_Data.Requests;
        static readonly IComparer<SimulationInputValue> s_ValueComparison = Comparer<SimulationInputValue>.Create(CompareValues);
        static readonly IComparer<SimulationInputRequest> s_RequestComparison = Comparer<SimulationInputRequest>.Create(CompareRequests);
        uint m_Generation;

        public SimulationInputFrameStorage(int valueCapacity, int requestCapacity)
        {
            m_Data = new SimulationInputValues(valueCapacity, requestCapacity);
        }

        internal SimulationInputFrameStorage(SimulationNumericProfile profile,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence,
            IReadOnlyList<SimulationInputValue> values, IReadOnlyList<SimulationInputRequest> requests)
        {
            NumericProfile = profile;
            Header = new SimulationInputHeader(source, sourceIdentity, sequence);
            m_Data = new SimulationInputValues(values.Count, requests.Count);
            m_Values.CopyFrom(values);
            m_Requests.CopyFrom(requests);
            m_Owned = true;
        }

        internal bool IsOwned => m_Owned;
        internal SimulationNumericProfile NumericProfile { get; private set; }
        internal SimulationInputHeader Header { get; private set; }
        internal SimulationTickSourceIdentity TickSource => Header.TickSource;
        internal CanonicalTextValue InputSourceIdentity => Header.InputSourceIdentity;
        internal ulong Sequence => Header.Sequence;
        internal IReadOnlyList<SimulationInputValue> Values => m_Values;
        internal IReadOnlyList<SimulationInputRequest> Requests => m_Requests;
        internal SimulationInputValues CanonicalValues => m_Data;

        SimulationInput Finish(SimulationNumericProfile profile,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence)
        {
            return Finish(profile, new SimulationInputHeader(source, sourceIdentity, sequence));
        }

        SimulationInput Finish(SimulationNumericProfile profile, in SimulationInputHeader header)
        {
            NumericProfile = profile;
            SimulationInputHeader.CanonicalRetain(in header);
            Header = header;
            return new SimulationInput(this, m_Generation);
        }

        internal SimulationInput CompleteRead(in SimulationInputHeader header) =>
            Finish(FixedSimulationNumericProfile.Value, in header);

        internal SimulationInputFrameStorage(in SimulationInputHeader header, SimulationInputValues data)
        {
            NumericProfile = FixedSimulationNumericProfile.Value;
            Header = header;
            m_Data = data;
            m_Owned = true;
        }

        public void Begin()
        {
            ReleaseText();
            m_Values.Clear();
            m_Requests.Clear();
            m_Generation++;
        }

        public void AddValue(SimulationInputValue value)
        {
            m_Values.Add(value);
            value.Retain();
        }

        public void AddRequest(SimulationInputRequest request)
        {
            m_Requests.Add(request);
            SimulationInputRequest.CanonicalRetain(in request);
        }

        public SimulationInput Complete(SimulationNumericProfile profile,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence)
        {
            m_Values.Sort(s_ValueComparison);
            m_Requests.Sort(s_RequestComparison);
            for (int index = 1; index < m_Values.Count; index++)
                if (m_Values[index - 1].InputId == m_Values[index].InputId)
                    throw new ArgumentException($"Duplicate simulation input value '{m_Values[index].InputId}'.");
            for (int index = 1; index < m_Requests.Count; index++)
                if (m_Requests[index - 1].Sequence == m_Requests[index].Sequence)
                    throw new ArgumentException($"Duplicate simulation input request sequence '{m_Requests[index].Sequence}'.");
            return Finish(profile, source, sourceIdentity, sequence);
        }

        public SimulationInput Capture(SimulationInput input)
        {
            Begin();
            IReadOnlyList<SimulationInputValue> values = input.Values;
            IReadOnlyList<SimulationInputRequest> requests = input.Requests;
            for (int index = 0; index < values.Count; index++)
                AddValue(values[index]);
            for (int index = 0; index < requests.Count; index++)
                AddRequest(requests[index]);
            return Finish(input.NumericProfile, input.TickSource, input.InputSourceIdentity, input.Sequence);
        }

        public SimulationInput BindValues(SimulationNumericProfile profile,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence,
            IReadOnlyList<SimulationInputValue> values)
        {
            Begin();
            m_Values.CopyFrom(values);
            for (int index = 0; index < values.Count; index++)
                values[index].Retain();
            return Finish(profile, source, sourceIdentity, sequence);
        }

        internal SimulationInput Remap(SimulationInput recorded,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence,
            ulong recordedTick, ulong replayTick)
        {
            Begin();
            for (int index = 0; index < recorded.Values.Count; index++)
                AddValue(recorded.Values[index]);
            for (int index = 0; index < recorded.Requests.Count; index++)
            {
                SimulationInputRequest request = recorded.Requests[index];
                AddRequest(new SimulationInputRequest(request.RequestId, request.Sequence,
                    SimulationInput.RemapTick(request.SourceTick, recordedTick, replayTick),
                    SimulationInput.RemapTick(request.ExpireSimulationTick, recordedTick, replayTick), request.Priority));
            }
            return Finish(recorded.NumericProfile, source, sourceIdentity, sequence);
        }

        internal SimulationInput Hold(SimulationInput recorded,
            SimulationTickSourceIdentity source, CanonicalTextValue sourceIdentity, ulong sequence)
        {
            Begin();
            for (int index = 0; index < recorded.Values.Count; index++)
                AddValue(recorded.Values[index]);
            return Finish(recorded.NumericProfile, source, sourceIdentity, sequence);
        }

        internal SimulationInputFrameStorage Read(uint generation)
        {
            RequireGeneration(generation);
            return this;
        }

        void RequireGeneration(uint generation)
        {
            if (generation != m_Generation)
                throw new InvalidOperationException("Fixed input frame storage has already been released.");
        }

        void ReleaseText()
        {
            SimulationInputHeader.CanonicalRelease(Header);
            SimulationInputValues.CanonicalRelease(m_Data);
            Header = default;
        }

        public void Release()
        {
            ReleaseText();
            m_Generation++;
            m_Values.Clear();
            m_Requests.Clear();
        }

        static int CompareValues(SimulationInputValue left, SimulationInputValue right) =>
            CanonicalTextValue.CompareOrdinal(left.InputId, right.InputId);

        static int CompareRequests(SimulationInputRequest left, SimulationInputRequest right)
        {
            int result = left.Sequence.CompareTo(right.Sequence);
            return result != 0 ? result : CanonicalTextValue.CompareOrdinal(left.RequestId, right.RequestId);
        }
    }

    internal sealed class SimulationInputFrameBank
    {
        readonly SimulationInputFrameStorage[] m_Frames;
        int m_Count;

        internal SimulationInputFrameBank(int valueCapacity, int requestCapacity, int frameCapacity)
        {
            m_Frames = new SimulationInputFrameStorage[frameCapacity];
            for (int index = 0; index < frameCapacity; index++)
                m_Frames[index] = new SimulationInputFrameStorage(valueCapacity, requestCapacity);
        }

        internal SimulationInputFrameStorage BeginFrame()
        {
            if (m_Count == m_Frames.Length)
                throw new InvalidOperationException("Fixed input frames exceed the prepared transaction capacity.");
            SimulationInputFrameStorage frame = m_Frames[m_Count++];
            frame.Begin();
            return frame;
        }

        internal void ReleaseFrames()
        {
            for (int index = 0; index < m_Count; index++)
                m_Frames[index].Release();
            m_Count = 0;
        }
    }
}
