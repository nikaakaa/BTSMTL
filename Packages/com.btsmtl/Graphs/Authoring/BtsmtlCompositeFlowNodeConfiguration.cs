using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Graphs
{
    public interface IBtsmtlGraphStepPort
    {
        string Id { get; }
        string Name { get; }
    }

    public abstract partial class BtsmtlCompositeFlowNode<TStep>
        where TStep : IBtsmtlGraphStepPort
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField, FlowCanvas.FlowNode.GatherPortsCallback]
#endif
        List<TStep> m_Steps = new();

        protected BtsmtlCompositeFlowNode() { }
        protected BtsmtlCompositeFlowNode(IEnumerable<TStep> defaultSteps) => m_Steps = defaultSteps.ToList();

        [GraphAuthoringField("steps", GraphAuthoringFieldValueKind.Object,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public IReadOnlyList<TStep> Steps => m_Steps;
        public int StepCount => m_Steps.Count;
        public IBtsmtlGraphStepPort GetStep(int index) => m_Steps[index];

        [GraphAuthoringConfigure]
        public void SetSteps(IEnumerable<TStep> steps)
        {
            List<TStep> next = steps.ToList();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TStep step in next)
                if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
                    throw new ArgumentException("组合步骤必须具有唯一的稳定端口身份。", nameof(steps));
#if UNITY_5_3_OR_NEWER
            foreach (FlowCanvas.BinderConnection connection in outConnections.OfType<FlowCanvas.BinderConnection>())
                if (!ids.Contains(connection.sourcePortID))
                    throw new InvalidOperationException("删除步骤前必须先断开其端口。");
#endif
            m_Steps = next;
#if UNITY_5_3_OR_NEWER
            GatherPorts();
#endif
        }
    }
}
