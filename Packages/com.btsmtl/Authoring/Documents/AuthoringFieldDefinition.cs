using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringFieldDefinition
    {
        public AuthoringFieldDefinition(GraphAuthoringFieldId id, string displayName, GraphAuthoringFieldValueKind valueKind,
            AuthoringValueDefinition valueDefinition, AuthoringValue defaultValue, bool visible, bool writable,
            bool optional, string pickerKind, GraphAuthoringFieldConstraint constraint,
            GraphAuthoringFieldVisibilityCondition visibility, GraphAuthoringFieldCompilation compilation)
        {
            Id = id; DisplayName = displayName; ValueKind = valueKind; ValueDefinition = valueDefinition;
            DefaultValue = defaultValue; Visible = visible; Writable = writable; Optional = optional;
            PickerKind = pickerKind; Constraint = constraint; Visibility = visibility; Compilation = compilation;
        }

        public GraphAuthoringFieldId Id { get; }
        public string DisplayName { get; }
        public GraphAuthoringFieldValueKind ValueKind { get; }
        public AuthoringValueDefinition ValueDefinition { get; }
        public AuthoringValue DefaultValue { get; }
        public bool Visible { get; }
        public bool Writable { get; }
        public bool Optional { get; }
        public string PickerKind { get; }
        public GraphAuthoringFieldConstraint Constraint { get; }
        public GraphAuthoringFieldVisibilityCondition Visibility { get; }
        public GraphAuthoringFieldCompilation Compilation { get; }

        public static IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> ReceiveFields(
            IReadOnlyList<AuthoringFieldDefinition> definitions, ReadOnlyMemory<AuthoringNamedValue> authored, string path)
        {
            var declared = definitions.ToDictionary(field => field.Id);
            var values = definitions.ToDictionary(field => field.Id, field => field.DefaultValue);
            var supplied = new HashSet<GraphAuthoringFieldId>();
            foreach (AuthoringNamedValue field in authored.Span)
            {
                if (!supplied.Add(field.FieldId))
                    throw new InvalidOperationException(path + "/" + field.FieldId + ": 作者字段重复。");
                if (!declared.ContainsKey(field.FieldId))
                    throw new InvalidOperationException(path + "/" + field.FieldId + ": 字段不属于原正式定义。");
                values[field.FieldId] = field.Value;
            }
            foreach (var field in values) declared[field.Key].Receive(field.Value, path + "/" + field.Key);
            return values;
        }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public static AuthoringFieldDefinition Capture(GraphAuthoringFieldDescriptor field, AuthoringValueCodec values) => new(
            field.FieldId, field.DisplayName, field.ValueKind, values.Describe(field.ValueType),
            values.Capture(field.ValueType, field.DefaultValue), field.AuthoringVisible, field.AuthoringWritable,
            field.Optional, field.PickerKind, field.Constraint, field.Visibility, field.Compilation);
#endif

        public void Receive(AuthoringValue value, string path)
        {
            ValueDefinition.Receive(value, path);
            if (value is AuthoringNullValue)
            {
                if (!Optional) throw new InvalidOperationException(path + ": 正式必需字段不能为 null。");
                return;
            }
            if (value is not AuthoringScalarValue scalar) return;
            if (Constraint.NonEmpty && string.IsNullOrWhiteSpace(scalar.Value))
                throw new InvalidOperationException(path + ": 正式字段不能为空。");
            if (Constraint.AllowedValues.Count != 0 && !Constraint.AllowedValues.Contains(scalar.Value, StringComparer.Ordinal))
                throw new InvalidOperationException(path + ": 值不属于原字段候选。");
            if (scalar.Kind != GraphAuthoringFieldValueKind.Integer && scalar.Kind != GraphAuthoringFieldValueKind.Float) return;
            double number = double.Parse(scalar.Value, CultureInfo.InvariantCulture);
            if (Constraint.Finite && (double.IsNaN(number) || double.IsInfinity(number)) ||
                Constraint.Minimum.HasValue && number < Constraint.Minimum.Value ||
                Constraint.Maximum.HasValue && number > Constraint.Maximum.Value)
                throw new InvalidOperationException(path + ": 数值不符合原字段范围或有限值要求。");
        }
    }
}
