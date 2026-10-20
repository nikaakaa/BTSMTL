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
    }
}
#endif
