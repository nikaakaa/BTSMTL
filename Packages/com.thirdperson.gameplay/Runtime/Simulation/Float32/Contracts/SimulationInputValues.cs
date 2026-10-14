namespace ThirdPersonSimulation
{
    [ValueSnapshot]
    [ValueOperations(OwnsMemberResources = true, CopyMethod = nameof(CopyFrom), ClearMethod = nameof(Clear))]
    internal sealed partial class SimulationInputValues
    {
        internal partial void CopyFrom(SimulationInputValues value);
        internal partial void Clear();

        internal SimulationInputValues(int valueCapacity, int requestCapacity)
        {
            Values = new CanonicalBuffer<SimulationInputValue>(valueCapacity);
            Requests = new CanonicalBuffer<SimulationInputRequest>(requestCapacity);
        }

        [ValueSnapshot]
        internal CanonicalBuffer<SimulationInputValue> Values;
        [ValueSnapshot]
        internal CanonicalBuffer<SimulationInputRequest> Requests;

        internal SimulationInputValues(CanonicalBuffer<SimulationInputValue> values,
            CanonicalBuffer<SimulationInputRequest> requests)
        {
            Values = values;
            Requests = requests;
        }

        [ValueValidation]
        internal void RequireOrder()
        {
            for (int i = 1; i < Values.Count; i++)
                if (CanonicalTextValue.CompareOrdinal(Values[i - 1].InputId, Values[i].InputId) >= 0)
                    throw new System.IO.InvalidDataException("Input values must have unique identities in ordinal order.");
            for (int i = 1; i < Requests.Count; i++)
                if (Requests[i - 1].Sequence >= Requests[i].Sequence)
                    throw new System.IO.InvalidDataException("Input requests must have unique increasing sequences.");
        }

        internal SimulationInputValues(SimulationInputValue[] values, SimulationInputRequest[] requests)
        {
            Values = new CanonicalBuffer<SimulationInputValue>(values);
            Requests = new CanonicalBuffer<SimulationInputRequest>(requests);
        }
    }
}
