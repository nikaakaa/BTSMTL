#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Documents
{
    public class AuthoringValueCodec
    {
        readonly Dictionary<Type, AuthoringObjectContract> m_Objects = new Dictionary<Type, AuthoringObjectContract>();
        readonly Dictionary<Type, AuthoringValueDefinition> m_Definitions = new Dictionary<Type, AuthoringValueDefinition>();

        public virtual AuthoringValueDefinition Describe(Type type)
        {
            if (m_Definitions.TryGetValue(type, out AuthoringValueDefinition known)) return known;
            string id = TypeIdentity(type);
            TypeCode scalar = Type.GetTypeCode(type);
            AuthoringValueDefinition definition;
            if (type == typeof(OrchestrationTime))
                definition = new(id, AuthoringValueDefinitionKind.Scalar, TypeCode.Int64, false,
                    Array.Empty<string>(), false, Array.Empty<AuthoringValueMemberDefinition>(), null);
            else if (type.IsEnum)
                definition = new(id, AuthoringValueDefinitionKind.Enumeration, scalar, false,
                    Enum.GetNames(type), type.IsDefined(typeof(FlagsAttribute), false), Array.Empty<AuthoringValueMemberDefinition>(), null);
            else if (scalar != TypeCode.Object)
                definition = new(id, AuthoringValueDefinitionKind.Scalar, scalar, !type.IsValueType,
                    Array.Empty<string>(), false, Array.Empty<AuthoringValueMemberDefinition>(), null);
            else if (typeof(IEnumerable).IsAssignableFrom(type))
                definition = new(id, AuthoringValueDefinitionKind.Sequence, TypeCode.Empty, !type.IsValueType,
                    Array.Empty<string>(), false, Array.Empty<AuthoringValueMemberDefinition>(), Describe(ElementType(type)));
            else
            {
                AuthoringObjectContract contract = Contract(type);
                var members = contract.Construction == AuthoringObjectConstruction.PublicFields
                    ? contract.Fields.Select(field => new AuthoringValueMemberDefinition(field.Name, Describe(field.FieldType))).ToList()
                    : contract.Parameters.Select((parameter, index) => new AuthoringValueMemberDefinition(
                        contract.ParameterMemberIds[index], Describe(parameter.ParameterType))).ToList();
                GraphAuthoringTypeContract authoring = typeof(IGraphAuthoringFieldSource).IsAssignableFrom(type)
                    ? GraphAuthoringMemberBindings.Contract(type) : GraphAuthoringTypeContract.Empty;
                foreach (GraphAuthoringMember member in authoring.Fields)
                {
                    members.RemoveAll(field => field.Id == member.Metadata.FieldId);
                    members.Add(new AuthoringValueMemberDefinition(member.Metadata.FieldId, Describe(member.ValueType)));
                }
                definition = new(id, AuthoringValueDefinitionKind.Object, TypeCode.Empty, !type.IsValueType,
                    Array.Empty<string>(), false, members.ToArray(), null);
            }
            m_Definitions.Add(type, definition);
            return definition;
        }

        public AuthoringNamedValue[] CaptureFields(object target) => GraphAuthoringMemberBindings.Contract(target).Fields
            .Where(field => field.Metadata.DocumentMember == GraphAuthoringDocumentMember.Field)
            .Select(field => Member(field.Metadata.FieldId, field.ValueType, field.Read(target))).ToArray();

        public object RestoreDeclaredFields(Type type, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            AuthoringObjectContract construction = Contract(type);
            if (construction.Construction != AuthoringObjectConstruction.Constructor)
                throw new InvalidOperationException($"声明类型 '{type.FullName}' 没有原公开构造合同。");
            var values = fields.ToArray().ToDictionary(field => field.FieldId.Value, field => field.Value, StringComparer.Ordinal);
            if (!values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(construction.ParameterMemberIds))
                throw new InvalidOperationException($"声明类型 '{type.FullName}' 的字段与原公开构造合同不一致。");
            object[] arguments = construction.Parameters.Select((parameter, index) =>
                Restore(parameter.ParameterType, values[construction.ParameterMemberIds[index]])).ToArray();
            return construction.Constructor.Invoke(arguments);
        }

        public object ApplyFields(object target, ReadOnlyMemory<AuthoringNamedValue> fields,
            bool fromDefaults = false, bool complete = false)
        {
            GraphAuthoringTypeContract contract = GraphAuthoringMemberBindings.Contract(target);
            GraphAuthoringCandidate candidate = contract.CreateCandidate(target, fromDefaults, complete);
            foreach (AuthoringNamedValue value in fields.Span)
            {
                GraphAuthoringMember member = contract[value.FieldId.Value];
                if (member.Metadata.ReadOnly)
                    throw new InvalidOperationException($"作者字段 '{value.FieldId}' 只读，不能作为修改提交。");
                member.WriteCandidate(candidate, Restore(member.ValueType, value.Value));
            }
            return candidate.Apply(target);
        }

        public virtual AuthoringValue Capture(Type type, object value)
        {
            string identity = TypeIdentity(type);
            if (value == null)
                return new AuthoringNullValue(identity);
            if (type == typeof(OrchestrationTime))
                return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Integer,
                    ((OrchestrationTime)value).RawValue.ToString(CultureInfo.InvariantCulture));
            if (type.IsEnum)
                return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Enum, value.ToString());
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.String, (string)value);
                case TypeCode.Boolean:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Boolean, (bool)value ? "true" : "false");
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Integer,
                        Convert.ToString(value, CultureInfo.InvariantCulture));
                case TypeCode.Single:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Float,
                        ((float)value).ToString("G9", CultureInfo.InvariantCulture));
                case TypeCode.Double:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Float,
                        ((double)value).ToString("G17", CultureInfo.InvariantCulture));
                case TypeCode.Decimal:
                    return new AuthoringScalarValue(identity, GraphAuthoringFieldValueKind.Float,
                        ((decimal)value).ToString(CultureInfo.InvariantCulture));
            }
            if (value is IEnumerable sequence)
            {
                Type element = ElementType(type);
                return new AuthoringSequenceValue(identity,
                    sequence.Cast<object>().Select(item => Capture(element, item)).ToArray());
            }
            AuthoringObjectContract contract = Contract(type);
            AuthoringNamedValue[] construction = contract.Construction == AuthoringObjectConstruction.PublicFields
                ? contract.Fields.Select(field => Member(field.Name, field.FieldType, field.GetValue(value))).ToArray()
                : contract.Parameters.Select((parameter, index) => Member(contract.ParameterMemberIds[index], parameter.ParameterType,
                    AuthoringObjectContract.ReadableProperty(type, parameter).GetValue(value))).ToArray();
            var members = construction.ToDictionary(member => member.FieldId.Value, StringComparer.Ordinal);
            foreach (AuthoringNamedValue field in CaptureFields(value))
                members[field.FieldId.Value] = field;
            return new AuthoringObjectValue(identity, members.Values.ToArray());
        }

        public object Restore(Type type, AuthoringValue value)
        {
            if (value.TypeId != TypeIdentity(type))
                throw new ArgumentException($"作者值类型 '{value.TypeId}' 与正式字段 '{TypeIdentity(type)}' 不匹配。");
            return RestoreContent(type, value);
        }

        protected virtual object RestoreContent(Type type, AuthoringValue value)
        {
            if (value is AuthoringNullValue)
                return null;
            if (type == typeof(OrchestrationTime) && value is AuthoringScalarValue time)
                return OrchestrationTime.FromRaw(long.Parse(time.Value, CultureInfo.InvariantCulture));
            if (value is AuthoringScalarValue scalar)
                return type.IsEnum ? Enum.Parse(type, scalar.Value, false) :
                    Convert.ChangeType(scalar.Value, type, CultureInfo.InvariantCulture);
            if (value is AuthoringSequenceValue sequence)
            {
                Type element = ElementType(type);
                Array items = Array.CreateInstance(element, sequence.Items.Length);
                for (int index = 0; index < items.Length; index++)
                    items.SetValue(Restore(element, sequence.Items.Span[index]), index);
                return type.IsAssignableFrom(items.GetType()) ? items :
                    Activator.CreateInstance(type, new object[] { items });
            }
            if (value is AuthoringObjectValue composite)
            {
                AuthoringObjectContract contract = Contract(type);
                var members = composite.Members.ToArray().ToDictionary(member => member.FieldId.Value, member => member.Value,
                    StringComparer.Ordinal);
                if (!members.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Describe(type).Members.Select(member => member.Id)))
                    throw new InvalidOperationException($"复合作者值 '{type.FullName}' 的成员与原公开构造及作者合同不一致。");
                if (contract.Construction == AuthoringObjectConstruction.PublicFields)
                {
                    object instance = Activator.CreateInstance(type);
                    foreach (var field in contract.Fields)
                        field.SetValue(instance, Restore(field.FieldType, members[field.Name]));
                    return ApplyMembers(instance, members, contract);
                }
                object[] arguments = contract.Parameters.Select((parameter, index) =>
                    Restore(parameter.ParameterType, members[contract.ParameterMemberIds[index]])).ToArray();
                if (contract.Construction == AuthoringObjectConstruction.Constructor)
                    return ApplyMembers(contract.Constructor.Invoke(arguments), members, contract);
                object configured = Activator.CreateInstance(type);
                contract.Configure.Invoke(configured, arguments);
                return ApplyMembers(configured, members, contract);
            }
            throw new ArgumentException($"作者值 '{value.GetType().Name}' 没有 '{type.FullName}' 的正式读取映射。");
        }

        protected AuthoringNamedValue Member(string name, Type type, object value) =>
            new AuthoringNamedValue(new GraphAuthoringFieldId(name), Capture(type, value));

        object ApplyMembers(object instance, IReadOnlyDictionary<string, AuthoringValue> members,
            AuthoringObjectContract construction)
        {
            GraphAuthoringTypeContract fields = GraphAuthoringMemberBindings.Contract(instance);
            GraphAuthoringCandidate candidate = fields.CreateCandidate(instance);
            foreach (GraphAuthoringMember field in fields.Fields.Where(field => !field.Metadata.ReadOnly &&
                !construction.ParameterMemberIds.Contains(field.Metadata.FieldId, StringComparer.Ordinal)))
                field.WriteCandidate(candidate, Restore(field.ValueType, members[field.Metadata.FieldId]));
            return candidate.Apply(instance);
        }

        public static string TypeIdentity(Type type)
        {
            if (type.IsArray)
                return TypeIdentity(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            string name = definition.FullName;
            if (type.IsGenericType)
                name += "<" + string.Join(",", type.GetGenericArguments().Select(TypeIdentity)) + ">";
            return definition.Assembly == typeof(object).Assembly ? name : definition.Assembly.GetName().Name + ":" + name;
        }

        AuthoringObjectContract Contract(Type type)
        {
            if (!m_Objects.TryGetValue(type, out AuthoringObjectContract contract))
            {
                contract = AuthoringObjectContract.Read(type);
                m_Objects.Add(type, contract);
            }
            return contract;
        }

        static Type ElementType(Type type) => type.IsArray ? type.GetElementType() :
            type.GetInterfaces().Concat(new[] { type }).Single(candidate => candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
    }
}
#endif
