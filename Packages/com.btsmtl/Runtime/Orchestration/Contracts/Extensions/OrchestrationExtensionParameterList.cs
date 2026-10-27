using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    // 作者与工具显示描述；内容字段本身随运行数据发布，不携带显示信息。
    public sealed class OrchestrationExtensionParameterPresentation
    {
        public OrchestrationExtensionParameterPresentation(string group, string label, string unit, string description)
            : this(group, label, unit, description, false, 0d, 0d)
        {
        }

        public OrchestrationExtensionParameterPresentation(string group, string label, string unit, string description,
            double minimum, double maximum) : this(group, label, unit, description, true, minimum, maximum)
        {
        }

        OrchestrationExtensionParameterPresentation(string group, string label, string unit, string description,
            bool hasRange, double minimum, double maximum)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("参数显示名不能为空。", nameof(label));
            if (hasRange && (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum))
                throw new ArgumentOutOfRangeException(nameof(minimum), $"参数 {label} 的范围无效。");
            Group = group ?? string.Empty;
            Label = label;
            Unit = unit ?? string.Empty;
            Description = description ?? string.Empty;
            HasRange = hasRange;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Group { get; }
        public string Label { get; }
        public string Unit { get; }
        public string Description { get; }
        public bool HasRange { get; }
        public double Minimum { get; }
        public double Maximum { get; }
    }

    public readonly struct OrchestrationExtensionParameter
    {
        internal OrchestrationExtensionParameter(int index, int scalarIndex, in OrchestrationExtensionContentField field,
            OrchestrationExtensionParameterPresentation presentation)
        {
            Index = index;
            ScalarIndex = scalarIndex;
            Field = field;
            Presentation = presentation;
        }

        public int Index { get; }
        // 标量参数在连续标量存储中的位置；非标量为 -1。
        public int ScalarIndex { get; }
        public OrchestrationExtensionContentField Field { get; }
        public OrchestrationExtensionParameterPresentation Presentation { get; }
        public string Id => Field.Id;
        public bool IsScalar => ScalarIndex >= 0;
    }

    // 声明顺序即内容字段顺序，改动会改变已发布内容布局。
    public sealed class OrchestrationExtensionParameterList
    {
        readonly List<OrchestrationExtensionParameter> m_Parameters = new();
        readonly HashSet<string> m_Ids = new(StringComparer.Ordinal);

        public int ScalarCount { get; private set; }

        public OrchestrationExtensionParameter Scalar(string id, OrchestrationExtensionParameterPresentation presentation) =>
            Add(OrchestrationExtensionContentField.Value(id, OrchestrationValueKind.Scalar), presentation);

        public OrchestrationExtensionParameter Add(in OrchestrationExtensionContentField field,
            OrchestrationExtensionParameterPresentation presentation)
        {
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation), $"参数 {field.Id} 缺少显示描述。");
            if (!m_Ids.Add(field.Id))
                throw new ArgumentException($"参数 {field.Id} 重复声明。", nameof(field));
            bool scalar = field.Kind == OrchestrationExtensionFieldKind.Value && field.ValueKind == OrchestrationValueKind.Scalar;
            var parameter = new OrchestrationExtensionParameter(m_Parameters.Count, scalar ? ScalarCount++ : -1, in field,
                presentation);
            m_Parameters.Add(parameter);
            return parameter;
        }

        public OrchestrationExtensionParameter[] ToParameters() => m_Parameters.ToArray();

        public OrchestrationExtensionContentField[] ToFields()
        {
            var fields = new OrchestrationExtensionContentField[m_Parameters.Count];
            for (int index = 0; index < fields.Length; index++)
                fields[index] = m_Parameters[index].Field;
            return fields;
        }
    }
}
