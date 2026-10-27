#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;

namespace BTSMTL.Authoring.Documents
{
    public static class AuthoringBlackboardDocumentBinding
    {
        public static AuthoringNamedValue[] Capture(AuthoringValueCodec values, BtsmtlBlackboardDeclaration declaration)
        {
            GraphAuthoringMember identity = Identity();
            return values.CaptureFields(declaration).Where(field => field.FieldId.Value != identity.Metadata.FieldId).ToArray();
        }

        public static BtsmtlBlackboardDeclaration Restore(AuthoringValueCodec values, AuthoringVariableDocument variable)
        {
            GraphAuthoringMember identity = Identity();
            var fields = variable.Declaration.ToArray().Append(new AuthoringNamedValue(
                new GraphAuthoringFieldId(identity.Metadata.FieldId), values.Capture(identity.ValueType, variable.Id.Value))).ToArray();
            return (BtsmtlBlackboardDeclaration)values.RestoreDeclaredFields(typeof(BtsmtlBlackboardDeclaration), fields);
        }

        static GraphAuthoringMember Identity() => GraphAuthoringMemberBindings.Contract(typeof(BtsmtlBlackboardDeclaration)).Fields
            .Single(field => field.Metadata.ValueKind == GraphAuthoringFieldValueKind.IdentityReference);
    }
}
#endif
