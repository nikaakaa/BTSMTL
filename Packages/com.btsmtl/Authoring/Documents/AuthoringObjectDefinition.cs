using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringObjectDefinition
    {
        public AuthoringObjectDefinition(string typeId, IReadOnlyList<AuthoringFieldDefinition> fields)
        { TypeId = typeId; Fields = fields; }

        public string TypeId { get; }
        public IReadOnlyList<AuthoringFieldDefinition> Fields { get; }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public static AuthoringObjectDefinition Capture(object defaults, AuthoringValueCodec values) => new(
            AuthoringValueCodec.TypeIdentity(defaults.GetType()), GraphAuthoringMemberBindings.Contract(defaults).Fields
                .OrderBy(field => field.Metadata.FieldId, StringComparer.Ordinal)
                .Select(field => AuthoringFieldDefinition.Capture(field.Describe(defaults), values)).ToArray());
#endif
    }
}
