#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace BTSMTL.Authoring.Graph
{
    public static class GraphAuthoringMemberBindings
    {
        static readonly Dictionary<Type, GraphAuthoringTypeContract> s_Contracts = new();

        public static GraphAuthoringTypeContract Contract(object target) =>
            target is IGraphAuthoringFieldSource source ? source.AuthoringFields : GraphAuthoringTypeContract.Empty;

        public static GraphAuthoringTypeContract Contract(Type type)
        {
            if (!s_Contracts.TryGetValue(type, out GraphAuthoringTypeContract contract))
            {
                contract = Contract(Activator.CreateInstance(type));
                s_Contracts.Add(type, contract);
            }
            return contract;
        }

        public static object DefaultValue(Type type, GraphAuthoringFieldAttribute field) =>
            Contract(type)[field.FieldId].DefaultValue(Activator.CreateInstance(type));

        public static Type ValueType(Type type, string fieldId) => Contract(type)[fieldId].ValueType;
        public static Delegate Reader(Type type, string fieldId) => Contract(type)[fieldId].Reader;
        public static Func<object, IReadOnlyList<TChoice>> Choices<TChoice>(Type type, string fieldId) =>
            (Func<object, IReadOnlyList<TChoice>>)Contract(type)[fieldId].Choices;

        public static UnityEngine.Object ResourceOwner(object target, string fieldId) =>
            Contract(target)[fieldId].ResourceOwner(target);

        public static object BindResourceAsset(object target, string fieldId, UnityEngine.Object source) =>
            Contract(target)[fieldId].BindResource(target, source);

        public static T Candidate<T>(object target, string fieldId, IReadOnlyDictionary<string, object> candidates) =>
            candidates.TryGetValue(fieldId, out object candidate) ? (T)candidate : (T)Contract(target)[fieldId].Read(target);

        public static Func<object, IReadOnlyDictionary<string, object>, bool> Visibility(
            Type type, string fieldId, string expected)
        {
            GraphAuthoringMember field = Contract(type)[fieldId];
            object expectedValue = field.ValueType.IsEnum
                ? Enum.Parse(field.ValueType, expected)
                : Convert.ChangeType(expected, field.ValueType, CultureInfo.InvariantCulture);
            return (target, candidates) => Equals(field.Evaluate(target, candidates), expectedValue);
        }

        public static IReadOnlyDictionary<string, object> ReadDefaults(Type type)
        {
            object defaults = Activator.CreateInstance(type);
            return Contract(defaults).Fields.ToDictionary(
                field => field.Metadata.FieldId, field => field.DefaultValue(defaults), StringComparer.Ordinal);
        }

        public static IReadOnlyList<GraphAuthoringFieldValue> ReadFields(object target)
        {
            object defaults = Activator.CreateInstance(target.GetType());
            return Contract(target).Fields.Select(field => new GraphAuthoringFieldValue(
                field.Describe(defaults), field.Read(target))).ToArray();
        }

        public static void Apply(object node, IReadOnlyDictionary<string, object> values)
        {
            foreach (GraphAuthoringConfiguration operation in Contract(node).Configurations)
                if (operation.Fields.Any(values.ContainsKey))
                    operation.Apply(node, values);
        }
    }
}
#endif
