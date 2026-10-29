using BTSMTL.Authoring.Graph;

namespace BTSMTL.Graphs
{
    public abstract partial class BtsmtlCompositeFlowNode<TStep> : IGraphNodePortsSource
        where TStep : IBtsmtlGraphStepPort
    {
        protected virtual bool HasExecutionInput => true;
        protected virtual string ExecutionInputId => "Input";

        public GraphNodePorts DeclarePorts()
        {
            var ports = new GraphNodePorts();
            if (HasExecutionInput)
                ports.FlowIn(ExecutionInputId, "执行");
            for (int i = 0; i < m_Steps.Count; i++)
                ports.FlowOut(m_Steps[i].Id, $"{i + 1}. {m_Steps[i].Name}");
            return ports;
        }
    }
}
