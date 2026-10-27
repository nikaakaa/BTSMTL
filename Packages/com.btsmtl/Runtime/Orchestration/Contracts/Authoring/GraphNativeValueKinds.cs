using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    // 原生数值节点按 kind 的编译合同：操作码、变体与输入端口的编译身份；Unity 节点类型与文档节点共用。
    public sealed class GraphNativeValueKind
    {
        internal GraphNativeValueKind(string kind, OrchestrationOperationCode code, int variant,
            IReadOnlyDictionary<string, string> inputs)
        {
            Kind = kind;
            Code = code;
            Variant = variant;
            Inputs = inputs;
        }

        public string Kind { get; }
        public OrchestrationOperationCode Code { get; }
        public int Variant { get; }
        public IReadOnlyDictionary<string, string> Inputs { get; }

        public string Input(string nativeId) => Inputs[nativeId];

        public string Output(string nativeId) => nativeId == "Value"
            ? Code == OrchestrationOperationCode.Compare ? "m_Result" : "m_Output"
            : throw new InvalidOperationException($"Native value output '{nativeId}' is not declared for '{Kind}'.");
    }

    public static class GraphNativeValueKinds
    {
        static readonly IReadOnlyDictionary<string, GraphNativeValueKind> s_Kinds = Create();

        public static IEnumerable<GraphNativeValueKind> All => s_Kinds.Values;

        public static bool TryGet(string kind, out GraphNativeValueKind value) =>
            s_Kinds.TryGetValue(kind ?? string.Empty, out value);

        public static GraphNativeValueKind Require(string kind) => TryGet(kind, out GraphNativeValueKind value)
            ? value
            : throw new KeyNotFoundException($"原生数值节点 kind '{kind}' 未登记。");

        public static string Calculation(GraphCalculationKind kind) => "calculate." + kind;

        static IReadOnlyDictionary<string, GraphNativeValueKind> Create()
        {
            var kinds = new Dictionary<string, GraphNativeValueKind>(StringComparer.Ordinal);
            var boolean = Ports(("a", "m_Input1"), ("b", "m_Input2"));
            var comparison = Ports(("a", "m_InputValue1"), ("b", "m_InputValue2"));
            Add("and", OrchestrationOperationCode.And, boolean);
            Add("or", OrchestrationOperationCode.Or, boolean);
            Add("not", OrchestrationOperationCode.Not, Ports(("value", "m_Input")));
            Add("compare.number.equal", OrchestrationOperationCode.Compare, comparison, 0);
            Add("compare.number.not-equal", OrchestrationOperationCode.Compare, comparison, 1);
            Add("compare.number.less", OrchestrationOperationCode.Compare, comparison, 2);
            Add("compare.number.less-equal", OrchestrationOperationCode.Compare, comparison, 3);
            Add("compare.number.greater-equal", OrchestrationOperationCode.Compare, comparison, 4);
            Add("compare.number.greater", OrchestrationOperationCode.Compare, comparison, 5);
            Add("compare.int32.equal", OrchestrationOperationCode.Compare, comparison, 0);
            Add("compare.int32.not-equal", OrchestrationOperationCode.Compare, comparison, 1);
            Add("compare.int32.less", OrchestrationOperationCode.Compare, comparison, 2);
            Add("compare.int32.less-equal", OrchestrationOperationCode.Compare, comparison, 3);
            Add("compare.int32.greater-equal", OrchestrationOperationCode.Compare, comparison, 4);
            Add("compare.int32.greater", OrchestrationOperationCode.Compare, comparison, 5);
            foreach (GraphCalculationKind calculation in Enum.GetValues(typeof(GraphCalculationKind)))
            {
                var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (OperationValuePortDefinition port in GraphCalculationContracts.Require(calculation).Inputs)
                    inputs.Add(port.Identity, port.Identity);
                Add(Calculation(calculation), OrchestrationOperationCode.Calculate,
                    new ReadOnlyDictionary<string, string>(inputs), (int)calculation);
            }
            Add("select.number", OrchestrationOperationCode.SelectValue,
                Ports(("condition", "condition"), ("whenTrue", "whenTrue"), ("whenFalse", "whenFalse")));
            return new ReadOnlyDictionary<string, GraphNativeValueKind>(kinds);

            void Add(string kind, OrchestrationOperationCode code, IReadOnlyDictionary<string, string> inputs, int variant = 0) =>
                kinds.Add(kind, new GraphNativeValueKind(kind, code, variant, inputs));
        }

        static IReadOnlyDictionary<string, string> Ports(params (string Native, string Compiled)[] values)
        {
            var ports = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var value in values)
                ports.Add(value.Native, value.Compiled);
            return new ReadOnlyDictionary<string, string>(ports);
        }
    }
}
