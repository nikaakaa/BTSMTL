using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    // 操作端口的唯一声明入口：槽位按方向依声明顺序分配，合同端口表取自同一组声明。
    public sealed class OrchestrationExtensionPorts<TOperation> where TOperation : struct
    {
        readonly List<OrchestrationExtensionPort> m_Declarations = new List<OrchestrationExtensionPort>();
        bool m_Sealed;

        public int InputCount { get; private set; }
        public int OutputCount { get; private set; }

        public OrchestrationExtensionInputPort<TOperation> Input(string id, OrchestrationValueKind valueKind,
            OrchestrationObjectKind objectKind = null)
        {
            RequireOpen(id);
            var port = new OrchestrationExtensionInputPort<TOperation>(id, valueKind, InputCount, objectKind);
            m_Declarations.Add(port.Declaration);
            InputCount++;
            return port;
        }

        public OrchestrationExtensionOutputPort<TOperation> Output(string id, OrchestrationValueKind valueKind,
            OrchestrationObjectKind objectKind = null)
        {
            RequireOpen(id);
            var port = new OrchestrationExtensionOutputPort<TOperation>(id, valueKind, OutputCount, objectKind);
            m_Declarations.Add(port.Declaration);
            OutputCount++;
            return port;
        }

        // 端口表只交给一份合同；之后再声明端口会让合同与类型化端口分叉，直接拒绝。
        public OrchestrationExtensionPort[] Seal()
        {
            if (m_Sealed)
                throw new InvalidOperationException($"{typeof(TOperation).FullName} 的端口表已交给合同。");
            m_Sealed = true;
            return m_Declarations.ToArray();
        }

        void RequireOpen(string id)
        {
            if (m_Sealed)
                throw new InvalidOperationException($"{typeof(TOperation).FullName} 的端口表已交给合同，不能再声明 '{id}'。");
        }
    }
}
