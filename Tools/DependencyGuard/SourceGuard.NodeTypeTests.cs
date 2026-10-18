using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Btsmtl.DependencyGuard;

sealed partial class SourceGuard
{
    static TypeSyntax? TestedType(SyntaxNode node) => node switch
    {
        BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.IsExpression or SyntaxKind.AsExpression => binary.Right as TypeSyntax,
        DeclarationPatternSyntax pattern => pattern.Type,
        TypePatternSyntax pattern => pattern.Type,
        RecursivePatternSyntax pattern => pattern.Type,
        _ => null
    };

    // 类型判断（is／as／模式）指向规则列出的节点类型即违规；转换和普通类型引用不受此规则约束。
    void CheckNodeTypeTest(SyntaxNode node, string path, Module owner, Dictionary<string, string> aliases, string[] imports)
    {
        if (TestedType(node) is not { } tested) return;
        NodeTypeTestRule[] applicable = rules.NodeTypeTestRules.Where(rule => rule.Applies(owner)).ToArray();
        if (applicable.Length == 0) return;
        string raw = Name(tested);
        if (raw.Length == 0) return;
        int arity = tested is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count :
            tested is QualifiedNameSyntax { Right: GenericNameSyntax right } ? right.TypeArgumentList.Arguments.Count : 0;
        foreach (string key in Resolve(raw, arity, tested, aliases, imports))
            foreach (NodeTypeTestRule rule in applicable.Where(rule => rule.Types.Contains(key, StringComparer.Ordinal)))
                Report(rule.Id, path, owner, tested, $"{rule.Reason} 类型判断 {key}。");
    }
}
