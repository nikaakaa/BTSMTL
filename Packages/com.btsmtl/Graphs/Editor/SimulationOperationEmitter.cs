using BTSMTL.Compilation;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using FlowCanvas;

namespace BTSMTL.Graphs.Editor
{
    public interface IBtsmtlCanvasCompilationBindings
    {
        string MapPort(FlowNode node, string port, BTSMTL.Orchestration.OrchestrationExtensionPortDirection direction);
        void BindInput(string graphId, string nodeId, string portId, int constant);
        void BindConstant(string graphId, string nodeId, string field, int constant,
            Func<FlowNode, SimulationCompileReport, SimulationNodeEmission> describe);
        void BindOperation(string graphId, string nodeId, OperationHandle operation,
            Func<FlowNode, SimulationCompileReport, SimulationNodeEmission> describe);
    }

    public readonly struct SimulationConstantInput
    {
        public SimulationConstantInput(string portId, SemanticValueKind kind, object value, SimulationSourceLocation source)
        {
            PortId = portId;
            Kind = kind;
            Value = value;
            Source = source;
        }

        public string PortId { get; }
        public SemanticValueKind Kind { get; }
        public object Value { get; }
        public SimulationSourceLocation Source { get; }
    }

    public sealed class SimulationOperationEmitter
    {
        readonly BtsmtlGraphSemanticBuilder m_Builder;
        readonly IBtsmtlCanvasCompilationBindings m_Bindings;

        public SimulationOperationEmitter(BtsmtlGraphSemanticBuilder builder,
            IBtsmtlCanvasCompilationBindings bindings = null)
        {
            m_Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            m_Bindings = bindings;
        }

        public OperationHandle Emit(
            SimulationSourceLocation source,
            SimulationNodeEmission emission,
            IReadOnlyList<SimulationConstantInput> inputs,
            Func<FlowNode, SimulationCompileReport, SimulationNodeEmission> describe = null)
        {
            var constants = new List<int>();
            var payloadFields = new List<SemanticPayloadField>();
            var payloadByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var bindings = new List<(SimulationConstantInput Input, int Constant)>();
            for (int i = 0; i < inputs.Count; i++)
            {
                SimulationConstantInput input = inputs[i];
                int constant = m_Builder.DeclareConstant(input.Source, "default-value", input.Value);
                if (constant >= 0)
                {
                    constants.Add(constant);
                    bindings.Add((input, constant));
                    m_Bindings?.BindInput(input.Source.GraphId, input.Source.NodeId, input.Source.PortId, constant);
                }
            }
            for (int i = 0; i < emission.Constants.Count; i++)
            {
                KeyValuePair<string, object> pair = emission.Constants[i];
                int constant = m_Builder.DeclareConstant(source, pair.Key, pair.Value);
                if (constant >= 0)
                {
                    constants.Add(constant);
                    payloadFields.Add(new SemanticPayloadField(pair.Key, constant));
                    payloadByName.Add(pair.Key, constant);
                    if (describe != null)
                        m_Bindings?.BindConstant(source.GraphId, source.NodeId, pair.Key, constant, describe);
                }
            }
            var constantOutputs = new List<SemanticConstantOutputBinding>();
            foreach (SemanticConstantPortDeclaration projection in emission.ConstantOutputs)
                constantOutputs.Add(new SemanticConstantOutputBinding(projection.PortId,
                    payloadByName[projection.PayloadFieldName], projection.ValueKind, projection.ValueTypeIdentity));
            OperationHandle operation = m_Builder.DeclareOperation(
                source,
                emission.Code,
                constants,
                emission.Integer0,
                emission.Integer1,
                emission.Unsigned0,
                default,
                emission.Text0,
                emission.Flags,
                extension: emission.Extension,
                payloadFields: payloadFields,
                output: emission.Output,
                hostInputs: emission.HostInputs,
                blackboardInputs: emission.BlackboardInputs,
                blackboardOutputs: emission.BlackboardOutputs,
                constantOutputs: constantOutputs,
                derivedInputs: emission.DerivedInputs);
            if (describe != null)
                m_Bindings?.BindOperation(source.GraphId, source.NodeId, operation, describe);
            foreach (var binding in bindings)
                m_Builder.DeclareConstantInputBinding(operation, binding.Input.PortId,
                    binding.Constant, binding.Input.Kind, binding.Input.Source);
            foreach (SemanticConstantPortDeclaration input in emission.ConstantInputs)
                m_Builder.DeclareConstantInputBinding(operation, input.PortId,
                    payloadByName[input.PayloadFieldName], input.ValueKind, source);
            if (emission.Output.HasValue)
                foreach (var port in emission.Output.Value.Ports.Span)
                    if (payloadByName.TryGetValue(port.Id, out int constant))
                        m_Builder.DeclareConstantInputBinding(operation, port.Id, constant,
                            OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), source);
            return operation;
        }
    }
}
