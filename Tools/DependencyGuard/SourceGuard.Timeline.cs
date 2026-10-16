using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Btsmtl.DependencyGuard;

sealed partial class SourceGuard
{
    sealed record TimelineDocument(string Path, Module Owner, CompilationUnitSyntax Root,
        Dictionary<string, string> Aliases, string[] Imports, string[] StaticImports);
    sealed record TimelineType(string Key, BaseTypeDeclarationSyntax Declaration, TimelineDocument Document);
    sealed record TimelineConstant(ExpressionSyntax Value, TimelineDocument Document);

    void CheckTimelineBoundaries()
    {
        var timelineDocuments = documents.Select(document => new TimelineDocument(document.Path, document.Owner, document.Root,
            document.Root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.Alias is not null)
                .GroupBy(item => item.Alias!.Name.Identifier.ValueText).ToDictionary(group => group.Key, group => Name(group.First().Name!), StringComparer.Ordinal),
            document.Root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.Alias is null && item.StaticKeyword.IsKind(SyntaxKind.None))
                .Select(item => Name(item.Name!)).Distinct(StringComparer.Ordinal).ToArray(),
            document.Root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                .Select(item => Name(item.Name!)).Distinct(StringComparer.Ordinal).ToArray())).ToArray();
        TimelineType[] types = timelineDocuments.SelectMany(document => document.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Select(declaration => new TimelineType(DeclarationKey(declaration), declaration, document))).ToArray();
        var byType = types.GroupBy(type => type.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var constants = new Dictionary<string, List<TimelineConstant>>(StringComparer.Ordinal);
        foreach (TimelineDocument document in timelineDocuments)
            foreach (FieldDeclarationSyntax field in document.Root.DescendantNodes().OfType<FieldDeclarationSyntax>().Where(field =>
                Name(field.Declaration.Type) is "string" or "System.String" || field.Declaration.Type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.StringKeyword)))
                if (field.Modifiers.Any(SyntaxKind.ConstKeyword) || field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables.Where(variable => variable.Initializer is not null))
                    {
                        string key = ContainingName(variable) + "." + variable.Identifier.ValueText;
                        if (!constants.TryGetValue(key, out List<TimelineConstant>? values)) constants.Add(key, values = []);
                        values.Add(new(variable.Initializer!.Value, document));
                    }

        foreach (TimelineBoundaryRule rule in rules.TimelineBoundaryRules)
        {
            foreach (TimelineType type in types.Where(type => rule.Applies(type.Document.Owner)))
                foreach (TimelineTypeFamily family in rule.TypeFamilies)
                    if (!family.BaseTypes.Contains(type.Key, StringComparer.Ordinal) && !family.AllowedTypes.Contains(type.Key, StringComparer.Ordinal) &&
                        DerivesFrom(type.Key, family.BaseTypes, byType, new HashSet<string>(StringComparer.Ordinal)))
                        Report("TIMELINE_DOMAIN_TYPE", type.Document.Path, type.Document.Owner, type.Declaration,
                            $"BTSMTL 不得定义领域 {family.Name}：{type.Key}。领域实现应在内容 owner 装配的领域包中声明。");
            foreach (TimelineDocument document in timelineDocuments.Where(document => rule.Applies(document.Owner)))
            {
                foreach (GenericNameSyntax container in document.Root.DescendantNodes().OfType<GenericNameSyntax>())
                    if (ResolveType(container, document).Any(key => rule.SampleContainers.Contains(key, StringComparer.Ordinal)))
                        foreach (TypeSyntax element in container.TypeArgumentList.Arguments)
                            foreach (string key in ResolveType(element, document).Where(key => !rule.AllowedSampleTypes.Contains(key, StringComparer.Ordinal)))
                                Report("TIMELINE_DOMAIN_SAMPLE", document.Path, document.Owner, element,
                                    $"BTSMTL 样本容器不得输出领域载荷类型 {key}；通用样本只发布片段、种类、权重、时间、循环与原因。");
                foreach (FieldDeclarationSyntax field in document.Root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                    if (rule.KindHolderTypes.Contains(ContainingName(field), StringComparer.Ordinal))
                        foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables.Where(variable => variable.Initializer is not null))
                            CheckKind(variable.Initializer!.Value, document, rule, constants);
                foreach (BaseObjectCreationExpressionSyntax creation in document.Root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                    if (CreationType(creation) is TypeSyntax creationType && ResolveType(creationType, document).Any(key => rule.ContractTypes.Contains(key, StringComparer.Ordinal)) && creation.ArgumentList is not null)
                    {
                        ArgumentSyntax? kind = creation.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == "kind") ??
                            creation.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon is null);
                        if (kind is not null) CheckKind(kind.Expression, document, rule, constants);
                    }
            }
        }
    }

    bool DerivesFrom(string key, string[] baseTypes, Dictionary<string, TimelineType[]> types, HashSet<string> visited)
    {
        if (baseTypes.Contains(key, StringComparer.Ordinal)) return true;
        if (!visited.Add(key) || !types.TryGetValue(key, out TimelineType[]? declarationsForType)) return false;
        foreach (TimelineType type in declarationsForType)
            if (type.Declaration.BaseList is not null)
                foreach (BaseTypeSyntax parent in type.Declaration.BaseList.Types)
                    foreach (string parentKey in ResolveType(parent.Type, type.Document))
                        if (DerivesFrom(parentKey, baseTypes, types, visited)) return true;
        return false;
    }

    IEnumerable<string> ResolveType(TypeSyntax type, TimelineDocument document)
    {
        while (type is ArrayTypeSyntax array) type = array.ElementType;
        while (type.Parent is QualifiedNameSyntax qualified && qualified.Right == type) type = qualified;
        if (type.Parent is AliasQualifiedNameSyntax aliasQualified) type = aliasQualified;
        int arity = type is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count :
            type is QualifiedNameSyntax { Right: GenericNameSyntax right } ? right.TypeArgumentList.Arguments.Count : 0;
        return Resolve(Name(type), arity, type, document.Aliases, document.Imports);
    }

    void CheckKind(ExpressionSyntax expression, TimelineDocument document, TimelineBoundaryRule rule,
        Dictionary<string, List<TimelineConstant>> constants)
    {
        foreach (string value in ConstantValues(expression, document, constants, new HashSet<string>(StringComparer.Ordinal)))
            if (!rule.AllowedKinds.Contains(value, StringComparer.Ordinal))
                Report("TIMELINE_DOMAIN_KIND", document.Path, document.Owner, expression,
                    $"BTSMTL 不得定义领域 Timeline 合同种类 {value}；只内置通用原语与场景参数合同。");
    }

    IEnumerable<string> ConstantValues(ExpressionSyntax expression, TimelineDocument document,
        Dictionary<string, List<TimelineConstant>> constants, HashSet<string> visited)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
            return ConstantValues(parenthesized.Expression, document, constants, visited);
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return [literal.Token.ValueText];
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            return from left in ConstantValues(binary.Left, document, constants, new HashSet<string>(visited, StringComparer.Ordinal))
                   from right in ConstantValues(binary.Right, document, constants, new HashSet<string>(visited, StringComparer.Ordinal))
                   select left + right;
        string raw = Name(expression);
        string first = raw.Split('.')[0];
        if (document.Aliases.TryGetValue(first, out string? alias)) raw = alias + raw[first.Length..];
        var candidates = new List<string> { raw };
        for (string scope = ContainingName(expression); scope.Length != 0;)
        {
            candidates.Add(scope + "." + raw);
            int separator = scope.LastIndexOf('.');
            scope = separator < 0 ? "" : scope[..separator];
        }
        candidates.AddRange(document.Imports.Concat(document.StaticImports).Select(import => import + "." + raw));
        foreach (string candidate in candidates)
            if (constants.TryGetValue(candidate, out List<TimelineConstant>? values))
            {
                if (!visited.Add(candidate)) return [];
                return values.SelectMany(value => ConstantValues(value.Value, value.Document, constants, new HashSet<string>(visited, StringComparer.Ordinal)));
            }
        return [];
    }

    static string DeclarationKey(BaseTypeDeclarationSyntax declaration) => TypeKey(ContainingName(declaration), declaration.Identifier.ValueText,
        declaration is TypeDeclarationSyntax type ? type.TypeParameterList?.Parameters.Count ?? 0 : 0);

    static TypeSyntax? CreationType(BaseObjectCreationExpressionSyntax creation)
    {
        if (creation is ObjectCreationExpressionSyntax explicitCreation) return explicitCreation.Type;
        foreach (SyntaxNode ancestor in creation.Ancestors())
            switch (ancestor)
            {
                case VariableDeclarationSyntax variable: return variable.Type;
                case PropertyDeclarationSyntax property: return property.Type;
                case MethodDeclarationSyntax method: return method.ReturnType;
                case LocalFunctionStatementSyntax function: return function.ReturnType;
                case ArgumentSyntax: return null;
            }
        return null;
    }
}
