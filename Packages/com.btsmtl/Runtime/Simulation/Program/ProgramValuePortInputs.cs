namespace ThirdPersonSimulation
{
    public interface IProgramValuePortInputs<TValue> where TValue : struct
    {
        bool TryRead(OperationHandle operation, int portIndex, bool input, out TValue value);
    }

    public interface IProgramValuePortEdits<TValue> where TValue : struct
    {
        bool TryReadInput(OperationHandle operation, int portIndex, bool input, out TValue value);
        bool IsLocked(OperationHandle operation, int portIndex, bool input);
        void Set(OperationHandle operation, int portIndex, bool input, TValue value, bool locked);
        void Release(OperationHandle operation, int portIndex, bool input);
    }
}
