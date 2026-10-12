using System;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed partial class HostEventGraphRuntime : IDisposable
    {
        readonly HostEventGraph m_Source;
        readonly EventGraphHostContract m_Contract;
        readonly EventGraphCompiledBindings m_Bindings;
        readonly Float32GraphEventRuntime m_Runtime;

        public HostEventGraphRuntime(HostEventGraph source, EventGraphHostContract contract)
        {
            m_Source = source;
            m_Contract = contract;
            m_Bindings = new EventGraphCompiledBindings(source, contract);
            m_Runtime = new Float32GraphEventRuntime(m_Bindings.Runtime, contract.UpdateEventId,
                m_Bindings.Runtime.FindEntry("event:start"), m_Bindings.Runtime.FindEntry("event:update"));
#if UNITY_EDITOR
            PrepareEditor();
#endif
        }

        public HostEventGraph Source => m_Source;
        public EventGraphHostContract Contract => m_Contract;
        public EventGraphVariableContract VariableContract => m_Bindings.Variables;
        public ulong ResetGeneration => m_Runtime.ResetGeneration;
        public bool IsStarted => m_Runtime.IsStarted;
        public bool IsFaulted => m_Runtime.IsFaulted;
        public bool IsPrepared => m_Runtime.IsPrepared;
        public EventGraphExecutionFailure LastFailure => m_Runtime.LastFailure;

        public void Prepare(EventGraphInvocationContext invocation, IFloat32GraphHostContext hostContext) =>
            m_Runtime.Prepare(invocation, hostContext);

        public void Compute() => m_Runtime.Compute();

        public void Discard() => m_Runtime.Discard();

        public EventGraphExecutionResult Adopt()
        {
            Float32GraphEventResult result = m_Runtime.Adopt();
            return result.Succeeded
                ? EventGraphExecutionResult.Success(new EventGraphVariableFrame(VariableContract, result.Frame))
                : EventGraphExecutionResult.Failed(result.Failure);
        }

        public EventGraphExecutionResult Execute(EventGraphInvocationContext invocation, IFloat32GraphHostContext hostContext)
        {
            Prepare(invocation, hostContext);
            Compute();
            return Adopt();
        }

        public void Reset() => m_Runtime.Reset();

        public void Dispose() => m_Runtime.Dispose();
    }
}
