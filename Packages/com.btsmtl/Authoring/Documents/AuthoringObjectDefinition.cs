using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringObjectDefinition
    {
        public AuthoringObjectDefinition(string typeId, IReadOnlyList<AuthoringFieldDefinition> fields,
            IReadOnlyDictionary<string, GraphAuthoringFieldId> members)
        { TypeId = typeId; Fields = fields; Members = members; }

        public string TypeId { get; }
        public IReadOnlyList<AuthoringFieldDefinition> Fields { get; }
        public IReadOnlyDictionary<string, GraphAuthoringFieldId> Members { get; }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public static AuthoringObjectDefinition Capture(object defaults, AuthoringValueCodec values)
        {
            AuthoringFieldDefinition[] fields = GraphAuthoringMemberBindings.Contract(defaults).Fields
                .OrderBy(field => field.Metadata.FieldId, StringComparer.Ordinal)
                .Select(field => AuthoringFieldDefinition.Capture(field.Describe(defaults), values)).ToArray();
            var ids = fields.Select(field => field.Id.Value).ToHashSet(StringComparer.Ordinal);
            var members = defaults.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => member is FieldInfo || member is PropertyInfo)
                .Select(member => (Member: member, Field: member.GetCustomAttribute<GraphAuthoringFieldAttribute>(true)))
                .Where(binding => binding.Field != null && ids.Contains(binding.Field.FieldId))
                .ToDictionary(binding => binding.Member.Name, binding => new GraphAuthoringFieldId(binding.Field.FieldId),
                    StringComparer.Ordinal);
            return new(AuthoringValueCodec.TypeIdentity(defaults.GetType()), fields, members);
        }
#endif
    }
}
