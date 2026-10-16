using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Btsmtl.DependencyGuard;

sealed partial class SourceGuard
{
    readonly Dictionary<SyntaxTree, SemanticModel> valueBindings = [];

    void PrepareValueBindings()
    {
        foreach (var group in documents.GroupBy(document => document.Owner.Path, StringComparer.OrdinalIgnoreCase))
        {
            CSharpCompilation compilation = CSharpCompilation.Create(group.First().Owner.Name,
                group.Select(document => document.Root.SyntaxTree));
            foreach (var document in group)
                valueBindings.Add(document.Root.SyntaxTree, compilation.GetSemanticModel(document.Root.SyntaxTree));
        }
    }

    bool IsValueReceiver(ExpressionSyntax expression)
    {
        while (expression is MemberAccessExpressionSyntax access) expression = access.Expression;
        SymbolInfo binding = valueBindings[expression.SyntaxTree].GetSymbolInfo(expression);
        return binding.Symbol is not null ? IsValueSymbol(binding.Symbol) :
            binding.CandidateSymbols.Length != 0 && binding.CandidateSymbols.All(IsValueSymbol);
    }

    static bool IsValueSymbol(ISymbol symbol) => symbol.Kind is
        SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.Property or SymbolKind.Event or SymbolKind.RangeVariable;
}
