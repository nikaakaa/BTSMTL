using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BTSMTL.Authoring.Graph
{
    public sealed class GraphAuthoringFieldTuningMetadata
    {
        public GraphAuthoringFieldTuningMetadata(
            GraphAuthoringFieldInteractionPolicy interaction,
            GraphAuthoringFieldValueKind typedValueKind,
            string unit,
            double minimum,
            double maximum,
            bool finite,
            GraphAuthoringFieldApplyTiming applyTiming,
            GraphAuthoringFieldStatePolicy statePolicy,
            string consumerId,
            int consumerOperationStart,
            int consumerOperationCount)
        {
            if (minimum > maximum || consumerOperationStart < 0 || consumerOperationCount < 0)
                throw new ArgumentException("Graph authoring tuning metadata range is invalid.");
            Interaction = interaction;
            TypedValueKind = typedValueKind;
            Unit = unit ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
            Finite = finite;
            ApplyTiming = applyTiming;
            StatePolicy = statePolicy;
            ConsumerId = consumerId ?? string.Empty;
            ConsumerOperationStart = consumerOperationStart;
            ConsumerOperationCount = consumerOperationCount;
        }

        public GraphAuthoringFieldInteractionPolicy Interaction { get; }
        public GraphAuthoringFieldValueKind TypedValueKind { get; }
        public string Unit { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public bool Finite { get; }
        public GraphAuthoringFieldApplyTiming ApplyTiming { get; }
        public GraphAuthoringFieldStatePolicy StatePolicy { get; }
        public string ConsumerId { get; }
        public int ConsumerOperationStart { get; }
        public int ConsumerOperationCount { get; }
    }

    public sealed class GraphAuthoringFieldConstraint
    {
        public GraphAuthoringFieldConstraint(
            double? minimum = null,
            double? maximum = null,
            bool finite = false,
            bool nonEmpty = false,
            IReadOnlyList<string> allowedValues = null)
        {
            if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
                throw new ArgumentException("Graph authoring field constraint range is invalid.");
            Minimum = minimum;
            Maximum = maximum;
            Finite = finite;
            NonEmpty = nonEmpty;
            AllowedValues = allowedValues ?? Array.Empty<string>();
        }

        public double? Minimum { get; }
        public double? Maximum { get; }
        public bool Finite { get; }
        public bool NonEmpty { get; }
        public IReadOnlyList<string> AllowedValues { get; }
    }

    public sealed class GraphAuthoringFieldVisibilityCondition
    {
        public GraphAuthoringFieldVisibilityCondition(
            GraphAuthoringFieldId controllerFieldId,
            string expectedValue)
        {
            ControllerFieldId = controllerFieldId.IsValid
                ? controllerFieldId
                : throw new ArgumentException("Visibility controller field identity is missing.", nameof(controllerFieldId));
            ExpectedValue = GraphAuthoringIdentity.Require(expectedValue, nameof(expectedValue));
        }

        public GraphAuthoringFieldId ControllerFieldId { get; }
        public string ExpectedValue { get; }

        public bool IsVisible(Func<GraphAuthoringFieldId, object> readField) =>
            string.Equals(
                readField?.Invoke(ControllerFieldId)?.ToString(),
                ExpectedValue,
                StringComparison.Ordinal);
    }

    public sealed class GraphAuthoringResourceReference
    {
        public GraphAuthoringResourceReference(string domain, Type contextType, bool navigation,
            Func<object, UnityEngine.Object> readOwner, Action<object, UnityEngine.Object> writeOwner,
            Func<object, UnityEngine.Object, object> bindAsset)
        {
            Domain = domain;
            ContextType = contextType;
            Navigation = navigation;
            ReadOwner = readOwner;
            WriteOwner = writeOwner;
            BindAsset = bindAsset;
        }

        public string Domain { get; }
        public Type ContextType { get; }
        public bool Navigation { get; }
        public Func<object, UnityEngine.Object> ReadOwner { get; }
        public Action<object, UnityEngine.Object> WriteOwner { get; }
        public Func<object, UnityEngine.Object, object> BindAsset { get; }
    }

    public sealed class GraphAuthoringFieldDescriptor
    {
        public GraphAuthoringFieldDescriptor(
            GraphAuthoringFieldId fieldId,
            string displayName,
            GraphAuthoringFieldValueKind valueKind,
            GraphAuthoringFieldAccess access,
            GraphAuthoringDetailsSection section = GraphAuthoringDetailsSection.Authoring,
            object defaultValue = null,
            GraphAuthoringFieldConstraint constraint = null,
            string pickerKind = "",
            bool optional = false,
            Type objectType = null,
            GraphAuthoringFieldVisibilityCondition visibility = null,
            GraphAuthoringFieldTuningMetadata tuning = null,
            GraphAuthoringFieldCompilation compilation = default,
            GraphAuthoringResourceReference resourceReference = null)
        {
            FieldId = fieldId.IsValid ? fieldId : throw new ArgumentException("Field identity is missing.", nameof(fieldId));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Field display name is missing.", nameof(displayName)) : displayName;
            ValueKind = valueKind;
            Access = access;
            Section = section;
            DefaultValue = defaultValue;
            Constraint = constraint ?? new GraphAuthoringFieldConstraint();
            PickerKind = pickerKind ?? string.Empty;
            Optional = optional;
            ObjectType = objectType;
            Visibility = visibility;
            Tuning = tuning;
            Compilation = compilation;
            ResourceReference = resourceReference;
        }

        public GraphAuthoringFieldId FieldId { get; }
        public string DisplayName { get; }
        public GraphAuthoringFieldValueKind ValueKind { get; }
        public GraphAuthoringFieldAccess Access { get; }
        public GraphAuthoringDetailsSection Section { get; }
        public object DefaultValue { get; }
        public GraphAuthoringFieldConstraint Constraint { get; }
        public string PickerKind { get; }
        public bool Optional { get; }
        public Type ObjectType { get; }
        public GraphAuthoringFieldVisibilityCondition Visibility { get; }
        public GraphAuthoringFieldTuningMetadata Tuning { get; }
        public GraphAuthoringFieldCompilation Compilation { get; }
        public GraphAuthoringResourceReference ResourceReference { get; }
        public GraphAuthoringFieldInteractionPolicy Interaction =>
            Tuning?.Interaction ?? GraphAuthoringFieldInteractionPolicy.Unclassified;
        public bool AuthoringVisible => (Access & GraphAuthoringFieldAccess.AuthoringRead) != 0;
        public bool AuthoringWritable => (Access & GraphAuthoringFieldAccess.AuthoringWrite) != 0;
        public bool IsVisible(Func<GraphAuthoringFieldId, object> readField) =>
            Visibility == null || Visibility.IsVisible(readField);
    }

    public readonly struct GraphAuthoringFieldValue
    {
        readonly string m_FieldId;

        public GraphAuthoringFieldValue(string fieldId, object value)
        {
            m_FieldId = fieldId;
            Field = null;
            Value = value;
        }

        public GraphAuthoringFieldValue(
            GraphAuthoringFieldDescriptor field,
            object value)
        {
            m_FieldId = null;
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Value = value;
        }

        public GraphAuthoringFieldDescriptor Field { get; }
        public string FieldId => Field == null ? m_FieldId : Field.FieldId.Value;
        public object Value { get; }
        public bool IsMissing => Value == null || Value is UnityEngine.Object asset && !asset;
        public string CanonicalValue => Value switch
        {
            string text => text,
            Enum enumValue => enumValue.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Value?.ToString() ?? string.Empty
        };

        public bool IsValid
        {
            get
            {
                if (IsMissing)
                    return Field.Optional;
                if (!MatchesValueKind() ||
                    Field.Constraint.NonEmpty &&
                    Value is string text && string.IsNullOrWhiteSpace(text))
                    return false;
                if (Field.Constraint.AllowedValues.Count > 0 &&
                    !Field.Constraint.AllowedValues.Contains(CanonicalValue, StringComparer.Ordinal))
                    return false;
                if (Value is not IConvertible convertible ||
                    Field.ValueKind is not GraphAuthoringFieldValueKind.Integer and
                    not GraphAuthoringFieldValueKind.Float)
                    return true;
                double number = convertible.ToDouble(CultureInfo.InvariantCulture);
                return (!Field.Constraint.Finite ||
                        !double.IsNaN(number) && !double.IsInfinity(number)) &&
                       (!Field.Constraint.Minimum.HasValue ||
                        number >= Field.Constraint.Minimum.Value) &&
                       (!Field.Constraint.Maximum.HasValue ||
                        number <= Field.Constraint.Maximum.Value);
            }
        }

        bool MatchesValueKind()
        {
            if (Field.ObjectType != null && !Field.ObjectType.IsInstanceOfType(Value))
                return false;
            return Field.ValueKind switch
            {
                GraphAuthoringFieldValueKind.String => Value is string,
                GraphAuthoringFieldValueKind.Boolean => Value is bool,
                GraphAuthoringFieldValueKind.Integer => Value is sbyte or byte or short or ushort or int or uint or long or ulong,
                GraphAuthoringFieldValueKind.Float => Value is float or double or decimal or int,
                GraphAuthoringFieldValueKind.Vector2 => Value is Vector2,
                GraphAuthoringFieldValueKind.Vector3 => Value is Vector3,
                GraphAuthoringFieldValueKind.Quaternion => Value is Quaternion,
                GraphAuthoringFieldValueKind.Enum => Value is string || Value.GetType().IsEnum,
                GraphAuthoringFieldValueKind.AssetReference => Value is UnityEngine.Object,
                GraphAuthoringFieldValueKind.IdentityReference => Field.ObjectType != null || Value is string || Value is UnityEngine.Object,
                GraphAuthoringFieldValueKind.Object => true,
                _ => false
            };
        }
    }

    public readonly struct GraphAuthoringTypedPropertyValue
    {
        public GraphAuthoringTypedPropertyValue(
            GraphAuthoringFieldId fieldId,
            GraphAuthoringFieldValueKind valueKind,
            string canonicalValue)
        {
            FieldId = fieldId.IsValid
                ? fieldId
                : throw new ArgumentException("Typed property field identity is missing.", nameof(fieldId));
            ValueKind = valueKind;
            CanonicalValue = GraphAuthoringIdentity.Require(canonicalValue, nameof(canonicalValue));
        }

        public GraphAuthoringFieldId FieldId { get; }
        public GraphAuthoringFieldValueKind ValueKind { get; }
        public string CanonicalValue { get; }
    }
}
