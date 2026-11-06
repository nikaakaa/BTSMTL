#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Graph
{
    public static class GraphAuthoringMemberBindings
    {
        static readonly ConcurrentDictionary<Type, GraphAuthoringTypeContract> s_Contracts = new();

        public static GraphAuthoringTypeContract Contract(object target) =>
            target is IGraphAuthoringFieldSource source ? source.AuthoringFields : GraphAuthoringTypeContract.Empty;

        public static GraphAuthoringTypeContract Contract(Type type) => s_Contracts.GetOrAdd(type, declared =>
            typeof(IGraphAuthoringFieldSource).IsAssignableFrom(declared)
                ? (GraphAuthoringTypeContract)(declared.GetProperty("AuthoringContract",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) ??
                    throw new InvalidOperationException($"作者类型 '{declared.FullName}' 缺少正式生成合同。"))
                    .GetValue(null)
                : GraphAuthoringTypeContract.Empty);

        public static object DefaultValue(Type type, GraphAuthoringFieldAttribute field) =>
            Contract(type)[field.FieldId].DefaultValue(Activator.CreateInstance(type));

        public static Type ValueType(Type type, string fieldId) => Contract(type)[fieldId].ValueType;
        public static Delegate Reader(Type type, string fieldId) => Contract(type)[fieldId].Reader;
        public static Func<object, IReadOnlyList<TChoice>> Choices<TChoice>(Type type, string fieldId) =>
            (Func<object, IReadOnlyList<TChoice>>)Contract(type)[fieldId].Choices;

        public static object ResourceOwner(object target, string fieldId) =>
            Contract(target)[fieldId].ResourceOwner(target);

        public static object BindResourceAsset(object target, string fieldId, object source) =>
            Contract(target)[fieldId].BindResource(target, source);

        public static Func<GraphAuthoringCandidate, bool> Visibility(
            Type type, string fieldId, string expected)
        {
            GraphAuthoringMember field = Contract(type)[fieldId];
            object expectedValue = field.ValueType.IsEnum
                ? Enum.Parse(field.ValueType, expected)
                : Convert.ChangeType(expected, field.ValueType, CultureInfo.InvariantCulture);
            return candidate => Equals(field.Evaluate(candidate), expectedValue);
        }

        public static IReadOnlyList<GraphAuthoringFieldValue> ReadFields(object target)
        {
            object defaults = Activator.CreateInstance(target.GetType());
            return Contract(target).Fields.Select(field => new GraphAuthoringFieldValue(
                field.Describe(defaults), field.Read(target))).ToArray();
        }

        public static GraphAuthoringCandidate CreateCandidate(object target, bool fromDefaults = false) =>
            Contract(target).CreateCandidate(target, fromDefaults);

        public static GraphAuthoringResourceAttribute ResourceDeclaration(object owner, string fieldId,
            Func<GraphAuthoringResourceAttribute, string, object> resolveContext)
        {
            GraphAuthoringTypeContract contract = Contract(owner);
            GraphAuthoringMember member = contract[fieldId];
            GraphAuthoringResourceRuleAttribute rule = member.Annotation<GraphAuthoringResourceRuleAttribute>();
            if (rule == null)
                return member.Annotation<GraphAuthoringResourceAttribute>() ??
                    throw new InvalidOperationException($"作者字段 '{fieldId}' 没有正式资源声明。");
            GraphAuthoringMember context = contract[rule.ContextFieldId];
            object declaration = resolveContext(context.Annotation<GraphAuthoringResourceAttribute>(), (string)context.Read(owner));
            GraphAuthoringResourceAttribute result = rule.Resolve(owner, declaration);
            if (result == null || string.IsNullOrEmpty(result.Domain))
                throw new InvalidOperationException($"作者字段 '{fieldId}' 的资源规则没有返回正式资源领域。");
            return result;
        }

        public static IReadOnlyList<TElement> DocumentElements<TElement>(object authoring, JObject root,
            Func<string, string, JObject, object, TElement> create)
        {
            var elements = new List<TElement>();
            Visit(authoring, root);
            return elements;

            void Visit(object owner, JObject value)
            {
                foreach (GraphAuthoringMember member in Contract(owner).Fields)
                {
                    if (member.SerializedMemberPath.Count == 0)
                        continue;
                    if (member.Annotation<GraphAuthoringResourceAttribute>() != null ||
                        member.Annotation<GraphAuthoringResourceRuleAttribute>() != null ||
                        !string.IsNullOrEmpty(member.Metadata.ResourceDomain))
                        continue;
                    Type childType = member.ValueType.IsArray ? member.ValueType.GetElementType() : member.ValueType;
                    GraphAuthoringElementAttribute declaration = member.Annotation<GraphAuthoringElementAttribute>();
                    GraphAuthoringTypeContract child = Contract(childType);
                    GraphAuthoringMember identity = declaration == null ? null : child[declaration.IdentityFieldId];
                    if (identity != null && (identity.ValueType != typeof(string) || identity.SerializedMemberPath.Count == 0))
                        throw new InvalidOperationException($"作者元素 '{declaration.Domain}' 的身份字段 '{declaration.IdentityFieldId}' 必须声明已存盘的字符串。");
                    if (child.Fields.Count == 0)
                        continue;
                    JToken stored = Read(value, member.SerializedMemberPath);
                    if (stored.Type == JTokenType.Null)
                        continue;
                    if (member.ValueType.IsArray)
                    {
                        var items = (Array)member.Read(owner);
                        var values = (JArray)stored;
                        for (int index = 0; index < values.Count; index++)
                            Add((JObject)values[index], items.GetValue(index));
                    }
                    else
                        Add((JObject)stored, member.Read(owner));

                    void Add(JObject item, object authored)
                    {
                        if (declaration != null)
                            elements.Add(create(declaration.Domain, (string)Read(item, identity.SerializedMemberPath), item, authored));
                        Visit(authored, item);
                    }
                }
            }

            static JToken Read(JObject value, IReadOnlyList<string> path)
            {
                JToken stored = value;
                foreach (string member in path)
                    stored = stored[member];
                return stored;
            }
        }
    }
}
#endif
