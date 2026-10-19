#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;

namespace BTSMTL.Authoring.Graph
{
    public interface IGraphAuthoringFieldSource
    {
        GraphAuthoringTypeContract AuthoringFields { get; }
    }

    public sealed class GraphAuthoringArgument
    {
        public GraphAuthoringArgument(Type type, string parameterName, string fieldId, params GraphAuthoringArgument[] arguments)
        {
            Type = type;
            ParameterName = parameterName;
            FieldId = fieldId;
            Arguments = arguments;
        }
        public Type Type { get; }
        public string ParameterName { get; }
        public string FieldId { get; }
        public IReadOnlyList<GraphAuthoringArgument> Arguments { get; }
    }

    public sealed class GraphAuthoringConfiguration
    {
        public GraphAuthoringConfiguration(string method, GraphAuthoringArgument[] arguments,
            string[] fields, Action<object, IReadOnlyDictionary<string, object>> apply)
        {
            Method = method;
            Arguments = arguments;
            Fields = fields;
            Apply = apply;
        }
        public string Method { get; }
        public IReadOnlyList<GraphAuthoringArgument> Arguments { get; }
        public IReadOnlyList<string> Fields { get; }
        public Action<object, IReadOnlyDictionary<string, object>> Apply { get; }
    }

    public sealed class GraphAuthoringMember
    {
        public GraphAuthoringMember(GraphAuthoringFieldAttribute metadata, Type valueType,
            Func<object, object> read, Delegate reader,
            Func<object, IReadOnlyDictionary<string, object>, object> evaluate,
            Func<object, object> defaultValue, Delegate choices = null,
            Func<object, UnityEngine.Object> resourceOwner = null,
            Action<object, UnityEngine.Object> writeResourceOwner = null,
            Func<object, UnityEngine.Object, object> bindResource = null,
            params Attribute[] annotations)
        {
            Metadata = metadata;
            ValueType = valueType;
            Read = read;
            Reader = reader;
            Evaluate = evaluate;
            DefaultValue = defaultValue;
            Choices = choices;
            ResourceOwner = resourceOwner;
            WriteResourceOwner = writeResourceOwner;
            BindResource = bindResource;
            Annotations = annotations;
            AllowedValues = metadata.AllowedValues;
            if ((AllowedValues == null || AllowedValues.Count == 0) && metadata.EnumType != null)
                AllowedValues = Enum.GetNames(metadata.EnumType);
        }
        public GraphAuthoringFieldAttribute Metadata { get; }
        public Type ValueType { get; }
        public Func<object, object> Read { get; }
        public Delegate Reader { get; }
        public Func<object, IReadOnlyDictionary<string, object>, object> Evaluate { get; }
        public Func<object, object> DefaultValue { get; }
        public Delegate Choices { get; }
        public Func<object, UnityEngine.Object> ResourceOwner { get; }
        public Action<object, UnityEngine.Object> WriteResourceOwner { get; }
        public Func<object, UnityEngine.Object, object> BindResource { get; }
        public IReadOnlyList<Attribute> Annotations { get; }
        public IReadOnlyList<string> AllowedValues { get; }
        public T Annotation<T>() where T : Attribute => Annotations.OfType<T>().SingleOrDefault();

        public GraphAuthoringFieldDescriptor Describe(object defaults)
        {
            GraphAuthoringFieldAttribute field = Metadata;
            return new GraphAuthoringFieldDescriptor(new GraphAuthoringFieldId(field.FieldId),
                string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldId : field.DisplayName, field.ValueKind,
                (field.AuthoringVisible ? GraphAuthoringFieldAccess.AuthoringRead : GraphAuthoringFieldAccess.None) |
                (field.ReadOnly ? GraphAuthoringFieldAccess.None : GraphAuthoringFieldAccess.AuthoringWrite),
                defaultValue: DefaultValue(defaults),
                constraint: new GraphAuthoringFieldConstraint(field.HasMinimum ? field.Minimum : (double?)null,
                    field.HasMaximum ? field.Maximum : (double?)null, field.Finite, field.NonEmpty, AllowedValues),
                pickerKind: field.PickerKind, optional: field.Optional, objectType: field.ObjectType,
                visibility: string.IsNullOrEmpty(field.VisibleWhenField) ? null : new GraphAuthoringFieldVisibilityCondition(
                    new GraphAuthoringFieldId(field.VisibleWhenField), field.VisibleWhenValue),
                compilation: new GraphAuthoringFieldCompilation(field.CompilationImpact, field.Unit));
        }
    }

    public sealed class GraphAuthoringTypeContract
    {
        public static readonly GraphAuthoringTypeContract Empty = new(
            Array.Empty<GraphAuthoringMember>(), Array.Empty<GraphAuthoringConfiguration>());

        public GraphAuthoringTypeContract(GraphAuthoringMember[] fields,
            GraphAuthoringConfiguration[] configurations, GraphAuthoringTypeContract inherited = null)
        {
            Fields = fields.Concat(inherited?.Fields ?? Array.Empty<GraphAuthoringMember>()).ToArray();
            Configurations = configurations.Concat(inherited?.Configurations ?? Array.Empty<GraphAuthoringConfiguration>()).ToArray();
            m_Fields = Fields.ToDictionary(field => field.Metadata.FieldId, StringComparer.Ordinal);
        }
        readonly IReadOnlyDictionary<string, GraphAuthoringMember> m_Fields;
        public IReadOnlyList<GraphAuthoringMember> Fields { get; }
        public IReadOnlyList<GraphAuthoringConfiguration> Configurations { get; }
        public GraphAuthoringMember this[string fieldId] => m_Fields[fieldId];
    }
}
#endif
