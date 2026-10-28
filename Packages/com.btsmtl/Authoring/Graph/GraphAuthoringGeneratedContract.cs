#if UNITY_EDITOR || BTSMTL_AUTHORING
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
            string[] fields)
        {
            Method = method;
            Arguments = arguments;
            Fields = fields;
        }
        public string Method { get; }
        public IReadOnlyList<GraphAuthoringArgument> Arguments { get; }
        public IReadOnlyList<string> Fields { get; }
    }

    public abstract class GraphAuthoringCandidate
    {
        public GraphAuthoringTypeContract Contract { get; internal set; }
        public abstract bool HasChanges { get; }
        public virtual void ResolveResources(object target) { }
        public abstract object Apply(object target);
    }

    public sealed class GraphAuthoringMember
    {
        public GraphAuthoringMember(GraphAuthoringFieldAttribute metadata, Type valueType,
            Func<object, object> read, Delegate reader,
            Delegate candidateReader, Func<GraphAuthoringCandidate, object> evaluate,
            Action<GraphAuthoringCandidate, object> writeCandidate,
            Func<GraphAuthoringCandidate, bool> isChanged, Func<object, object> externalValue,
            Func<object, object> defaultValue, Delegate choices = null,
            Func<object, object> resourceOwner = null,
            Action<object, object> writeResourceOwner = null,
            Func<object, object, object> bindResource = null,
            Action<GraphAuthoringCandidate, object> selectResource = null,
            Func<GraphAuthoringCandidate, bool> hasResourceSelection = null,
            Func<GraphAuthoringCandidate, object> selectedResource = null,
            params Attribute[] annotations)
        {
            Metadata = metadata;
            ValueType = valueType;
            Read = read;
            Reader = reader;
            CandidateReader = candidateReader;
            Evaluate = evaluate;
            WriteCandidate = writeCandidate;
            IsChanged = isChanged;
            ExternalValue = externalValue;
            DefaultValue = defaultValue;
            Choices = choices;
            ResourceOwner = resourceOwner;
            WriteResourceOwner = writeResourceOwner;
            BindResource = bindResource;
            SelectResource = selectResource;
            HasResourceSelection = hasResourceSelection;
            SelectedResource = selectedResource;
            Annotations = annotations;
            AllowedValues = metadata.AllowedValues;
            if ((AllowedValues == null || AllowedValues.Count == 0) && metadata.EnumType != null)
                AllowedValues = Enum.GetNames(metadata.EnumType);
        }
        public GraphAuthoringFieldAttribute Metadata { get; }
        public Type ValueType { get; }
        public Func<object, object> Read { get; }
        public Delegate Reader { get; }
        public Delegate CandidateReader { get; }
        public Func<GraphAuthoringCandidate, object> Evaluate { get; }
        public Action<GraphAuthoringCandidate, object> WriteCandidate { get; }
        public Func<GraphAuthoringCandidate, bool> IsChanged { get; }
        public Func<object, object> ExternalValue { get; }
        public Func<object, object> DefaultValue { get; }
        public Delegate Choices { get; }
        public Func<object, object> ResourceOwner { get; }
        public Action<object, object> WriteResourceOwner { get; }
        public Func<object, object, object> BindResource { get; }
        public Action<GraphAuthoringCandidate, object> SelectResource { get; }
        public Func<GraphAuthoringCandidate, bool> HasResourceSelection { get; }
        public Func<GraphAuthoringCandidate, object> SelectedResource { get; }
        public IReadOnlyList<Attribute> Annotations { get; }
        public IReadOnlyList<string> AllowedValues { get; }
        public T Annotation<T>() where T : Attribute => Annotations.OfType<T>().SingleOrDefault();

        public GraphAuthoringFieldDescriptor Describe(object defaults)
        {
            GraphAuthoringFieldAttribute field = Metadata;
            return new GraphAuthoringFieldDescriptor(new GraphAuthoringFieldId(field.FieldId),
                string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldId : field.DisplayName, field.ValueKind,
                (field.AuthoringVisible ? GraphAuthoringFieldAccess.AuthoringRead : GraphAuthoringFieldAccess.None) |
                (field.ReadOnly ? GraphAuthoringFieldAccess.None : GraphAuthoringFieldAccess.AuthoringWrite) |
                (field.ReferenceRead ? GraphAuthoringFieldAccess.ReferenceRead : GraphAuthoringFieldAccess.None),
                ValueType,
                defaultValue: ExternalValue(DefaultValue(defaults)),
                constraint: new GraphAuthoringFieldConstraint(field.HasMinimum ? field.Minimum : (double?)null,
                    field.HasMaximum ? field.Maximum : (double?)null, field.Finite, field.NonEmpty, AllowedValues),
                pickerKind: field.PickerKind, optional: field.Optional, objectType: field.ObjectType,
                visibility: string.IsNullOrEmpty(field.VisibleWhenField) ? null : new GraphAuthoringFieldVisibilityCondition(
                    new GraphAuthoringFieldId(field.VisibleWhenField), field.VisibleWhenValue),
                compilation: new GraphAuthoringFieldCompilation(field.CompilationImpact, field.Unit),
                documentMember: field.DocumentMember);
        }
    }

    public sealed class GraphAuthoringTypeContract
    {
        sealed class EmptyCandidate : GraphAuthoringCandidate
        {
            public override bool HasChanges => false;
            public override object Apply(object target) => target;
        }

        public static readonly GraphAuthoringTypeContract Empty = new(
            Array.Empty<GraphAuthoringMember>(), Array.Empty<GraphAuthoringConfiguration>(),
            (target, complete) => new EmptyCandidate());

        public GraphAuthoringTypeContract(GraphAuthoringMember[] fields,
            GraphAuthoringConfiguration[] configurations,
            Func<object, bool, GraphAuthoringCandidate> createCandidate,
            GraphAuthoringTypeContract inherited = null)
        {
            Fields = fields.Concat(inherited?.Fields ?? Array.Empty<GraphAuthoringMember>()).ToArray();
            Configurations = configurations.Concat(inherited?.Configurations ?? Array.Empty<GraphAuthoringConfiguration>()).ToArray();
            m_Fields = Fields.ToDictionary(field => field.Metadata.FieldId, StringComparer.Ordinal);
            m_CreateCandidate = createCandidate;
        }
        readonly IReadOnlyDictionary<string, GraphAuthoringMember> m_Fields;
        readonly Func<object, bool, GraphAuthoringCandidate> m_CreateCandidate;
        public IReadOnlyList<GraphAuthoringMember> Fields { get; }
        public IReadOnlyList<GraphAuthoringConfiguration> Configurations { get; }
        public GraphAuthoringMember this[string fieldId] => m_Fields[fieldId];

        public GraphAuthoringCandidate CreateCandidate(object target, bool fromDefaults = false, bool complete = false)
        {
            GraphAuthoringCandidate candidate = m_CreateCandidate(
                fromDefaults ? Activator.CreateInstance(target.GetType()) : target, fromDefaults || complete);
            candidate.Contract = this;
            return candidate;
        }
    }
}
#endif
