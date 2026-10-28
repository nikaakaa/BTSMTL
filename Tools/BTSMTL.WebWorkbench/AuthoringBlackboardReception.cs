using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using ThirdPersonSimulation;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringBlackboardValueDefinition(SemanticValueKind Kind, TypeCode ScalarType,
    AuthoringValueDefinition Value);

public static class AuthoringBlackboardReception
{
    static readonly GraphAuthoringFieldId VariableIdentity = new(AuthoringObjectContract.MemberIdentity(
        typeof(BtsmtlBlackboardDeclaration).GetProperty(nameof(BtsmtlBlackboardDeclaration.VariableId))!,
        nameof(BtsmtlBlackboardDeclaration.VariableId)));

    public static IReadOnlyList<BtsmtlBlackboardDeclaration> Receive(AuthoringGraphDocument graph,
        AuthoringValueCodec values, Func<string, AuthoringBlackboardValueDefinition> valueType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new List<BtsmtlBlackboardDeclaration>(graph.Variables.Length);
        foreach (AuthoringVariableDocument variable in graph.Variables.Span)
        {
            if (string.IsNullOrWhiteSpace(variable.Key) || !names.Add(variable.Key))
                throw new InvalidOperationException($"graph:{graph.Id}/variable:{variable.Id}: 黑板变量名称为空或重复。");
            AuthoringNamedValue[] fields = new[] { new AuthoringNamedValue(VariableIdentity,
                values.Capture(typeof(string), variable.Id.Value)) }.Concat(variable.Declaration.ToArray()).ToArray();
            var declaration = (BtsmtlBlackboardDeclaration)values.RestoreDeclaredFields(
                typeof(BtsmtlBlackboardDeclaration), fields);
            AuthoringBlackboardValueDefinition type = valueType(variable.TypeId);
            type.Value.Receive(variable.DefaultValue, $"graph:{graph.Id}/variable:{variable.Id}/defaultValue");
            BtsmtlBlackboardDeclaration.ValidateDefinition(type.Kind, type.ScalarType, declaration.Scope,
                declaration.Lifetime, declaration.InputBinding);
            declarations.Add(declaration);
        }
        return declarations;
    }
}
