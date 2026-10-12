using System;
using BTSMTL.EventGraphs;

namespace ThirdPersonSimulation
{
    public interface IFloat32GraphHostContext
    {
        EventGraphInvocationContext Invocation { get; }
        AbilityStateValue ReadInput(int slot);
    }

    public readonly struct Float32GraphEventResult
    {
        Float32GraphEventResult(bool succeeded, Float32GraphVariableFrame frame, EventGraphExecutionFailure failure)
        {
            Succeeded = succeeded;
            Frame = frame;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public Float32GraphVariableFrame Frame { get; }
        public EventGraphExecutionFailure Failure { get; }
        internal static Float32GraphEventResult Success(Float32GraphVariableFrame frame) => new(true, frame, null);
        internal static Float32GraphEventResult Failed(EventGraphExecutionFailure failure) => new(false, default, failure);
    }

    public sealed partial class Float32GraphEventRuntime : IDisposable
    {
        enum EvaluationStatus : byte
        {
            Idle,
            Prepared,
            Succeeded,
            Failed
        }

        readonly Float32GraphBindings m_Bindings;
        readonly OperationHandle m_Start;
        readonly OperationHandle m_Update;
        readonly string m_UpdateEventId;
        readonly AbilityStateValue[] m_State;
        readonly AbilityStateValue[] m_Candidate;
        readonly AbilityStateValue[] m_Inputs;
        readonly Float32GraphEvaluationWorkspace m_Workspace;
        readonly Float32GraphVariableBuffer m_Output;
        EventGraphExecutionFailure m_LastFailure;
        EventGraphExecutionFailure m_CandidateFailure;
        EventGraphInvocationContext m_Invocation;
        EventGraphInvocationIdentity m_LastInvocation;
        ulong m_ResetGeneration;
        bool m_Started;
        bool m_RunStart;
        EvaluationStatus m_Status;
        bool m_Faulted;
        bool m_Disposed;

        public Float32GraphEventRuntime(Float32GraphBindings bindings, string updateEventId,
            OperationHandle start, OperationHandle update)
        {
            m_Bindings = bindings;
            m_Start = start;
            m_Update = update;
            m_UpdateEventId = updateEventId;
            m_State = new AbilityStateValue[bindings.Data.StateSlots.Count];
            m_Candidate = new AbilityStateValue[m_State.Length];
            m_Inputs = new AbilityStateValue[bindings.InputCapacity];
            m_Output = new Float32GraphVariableBuffer(bindings.PublishedSlots.Length);
#if UNITY_EDITOR
            m_PortValues = new Float32GraphPortValues(bindings.Data.ValueLayout);
            m_VariableOverrides = new AbilityStateValue[m_State.Length];
            m_Triggers = new System.Collections.Generic.List<(OperationHandle Operation, CanonicalTextValue Port, bool Input)>(bindings.Data.ValueLayout.PortSlotCount);
#endif
            m_Workspace = new Float32GraphEvaluationWorkspace(bindings.Data
#if UNITY_EDITOR
                , m_PortValues, m_VariableOverrides
#endif
                );
            Reset();
        }

        public ulong ResetGeneration => m_ResetGeneration;
        public bool IsStarted => m_Started;
        public bool IsFaulted => m_Faulted;
        public bool IsPrepared => m_Status != EvaluationStatus.Idle;
        public EventGraphExecutionFailure LastFailure => m_LastFailure;

        public void Prepare(EventGraphInvocationContext invocation, IFloat32GraphHostContext hostContext)
        {
            RequireIdle();
            m_Status = m_Faulted ? EvaluationStatus.Failed : EvaluationStatus.Prepared;
            m_Invocation = invocation;
            invocation.Identity.Retain();
            m_CandidateFailure = m_LastFailure;
            m_RunStart = !m_Started;
            if (m_Faulted)
                return;
            Float32GraphEvaluation.PrepareCandidate(m_State, m_Candidate, m_Workspace);
            if (!invocation.Identity.Equals(hostContext.Invocation.Identity) ||
                invocation.EventId != hostContext.Invocation.EventId ||
                invocation.DeltaSeconds != hostContext.Invocation.DeltaSeconds)
            {
                Reject("Event graph host context does not match the invocation.");
                return;
            }
            if (invocation.EventId != m_UpdateEventId)
            {
                Reject($"Event graph event '{invocation.EventId}' is not declared by host contract '{m_Bindings.GraphId}'.");
                return;
            }
            if (m_LastInvocation.IsValid && (m_LastInvocation.SourceId != invocation.Identity.SourceId ||
                invocation.Identity.Sequence <= m_LastInvocation.Sequence))
            {
                Reject("Event graph invocation identity is not increasing.");
                return;
            }
            try
            {
                for (int i = 0; i < m_Bindings.RequiredInputSlots.Length; i++)
                {
                    int slot = m_Bindings.RequiredInputSlots[i];
                    AbilityStateValue value = hostContext.ReadInput(slot);
                    value.Retain();
                    m_Inputs[slot] = value;
                }
            }
            catch (Exception exception)
            {
                Reject(exception.Message, exception);
            }
        }

        public void Compute()
        {
            if (m_Status == EvaluationStatus.Failed)
                return;
            if (m_Status != EvaluationStatus.Prepared)
                throw new InvalidOperationException("图实例只能计算本次已准备的输入和候选。");
            try
            {
                if (m_RunStart && m_Start.IsValid)
                    Float32GraphEvaluation.Execute(m_Bindings, m_Start, m_Inputs, m_Candidate, m_Workspace);
#if UNITY_EDITOR
                ExecuteTriggers();
#endif
                Float32GraphEvaluation.Execute(m_Bindings, m_Update, m_Inputs, m_Candidate, m_Workspace);
                m_Status = EvaluationStatus.Succeeded;
            }
            catch (Exception exception)
            {
                Reject(exception.Message, exception);
            }
        }

        public Float32GraphEventResult Adopt()
        {
            if (m_Status == EvaluationStatus.Prepared || m_Status == EvaluationStatus.Idle)
                throw new InvalidOperationException("图 owner 只能采用已完成的本次计算结果。");
            if (m_Status == EvaluationStatus.Failed)
                return AdoptFailure();
            try
            {
                m_Output.BeginWrite();
                for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
                {
                    int slot = m_Bindings.PublishedSlots[i];
#if UNITY_EDITOR
                    AbilityStateValue value = IsVariableLocked(slot) ? m_VariableOverrides[slot] : m_Candidate[slot];
#else
                    AbilityStateValue value = m_Candidate[slot];
#endif
                    m_Output.Write(i, value);
                }
            }
            catch (Exception exception)
            {
                Reject(exception.Message, exception);
                return AdoptFailure();
            }
            for (int i = 0; i < m_State.Length; i++)
                m_Candidate[i].Retain();
#if UNITY_EDITOR
            AdoptEditor();
#endif
            for (int i = 0; i < m_State.Length; i++)
            {
                m_State[i].Release();
                m_State[i] = m_Candidate[i];
            }
            m_Started = true;
            m_Invocation.Identity.Retain();
            m_LastInvocation.Release();
            m_LastInvocation = m_Invocation.Identity;
            Float32GraphVariableFrame frame = m_Output.Publish(m_Invocation.Identity, m_ResetGeneration);
            Discard();
            return Float32GraphEventResult.Success(frame);
        }

        Float32GraphEventResult AdoptFailure()
        {
            m_LastFailure = m_CandidateFailure;
            m_Faulted = true;
            ResetState();
            var failed = Float32GraphEventResult.Failed(m_LastFailure);
            Discard();
            return failed;
        }

        public void Discard()
        {
            for (int i = 0; i < m_Bindings.RequiredInputSlots.Length; i++)
            {
                int slot = m_Bindings.RequiredInputSlots[i];
                m_Inputs[slot].Release();
                m_Inputs[slot] = default;
            }
            Array.Clear(m_Candidate, 0, m_Candidate.Length);
            m_Invocation.Identity.Release();
            m_Invocation = default;
            m_CandidateFailure = null;
            m_Status = EvaluationStatus.Idle;
        }

        public void Reset()
        {
            RequireIdle();
            ResetState();
            m_LastFailure = null;
            m_LastInvocation.Release();
            m_LastInvocation = default;
            m_Faulted = false;
            m_ResetGeneration = checked(m_ResetGeneration + 1);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            RequireIdle();
            ReleaseState();
            m_LastInvocation.Release();
            m_LastInvocation = default;
            m_Disposed = true;
        }

        public AbilityStateValue ReadState(int slot)
        {
#if UNITY_EDITOR
            if (IsVariableLocked(slot))
                return m_VariableOverrides[slot];
#endif
            return m_State[slot];
        }

        public void WriteState(int slot, AbilityStateValue value)
        {
            value.Retain();
            m_State[slot].Release();
            m_State[slot] = value;
        }

        void ResetState()
        {
            m_Output.Invalidate();
            for (int i = 0; i < m_State.Length; i++)
                WriteState(i, m_Bindings.Data.DefaultStateValue(i));
#if UNITY_EDITOR
            ResetEditor();
#endif
            m_Started = false;
        }

        void ReleaseState()
        {
            m_Output.Invalidate();
            for (int i = 0; i < m_State.Length; i++)
                m_State[i].Release();
#if UNITY_EDITOR
            ResetEditor();
#endif
        }

        void Reject(string message, Exception exception = null)
        {
            OperationHandle operation = m_Workspace.CurrentOperation;
            string node = operation.IsValid ? m_Bindings.OperationNodes[operation.Value] : string.Empty;
            m_CandidateFailure = new EventGraphExecutionFailure(m_Bindings.GraphId, m_Invocation.EventId, node, message, exception);
            m_Status = EvaluationStatus.Failed;
        }

        public void RequireIdle()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(Float32GraphEventRuntime));
            if (m_Status != EvaluationStatus.Idle)
                throw new InvalidOperationException("图实例必须完成或丢弃已派发候选后才能改变生命周期。");
        }
    }
}
