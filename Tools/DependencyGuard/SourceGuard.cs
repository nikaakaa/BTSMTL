using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace Btsmtl.DependencyGuard;

sealed class SourceGuard(DependencyCatalog catalog, GuardRules rules, string? clientRoot)
{
    readonly Dictionary<string, List<Module>> declarations = new(StringComparer.Ordinal);
    readonly HashSet<string> retiredTypes = new(rules.RetiredTypes, StringComparer.Ordinal);
    readonly HashSet<string> roles = new(rules.RoleNames, StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> skills = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(string Path, Module Owner, CompilationUnitSyntax Root)> documents = [];
    public int SourceCount => documents.Count;
    public string[] RoleNames => roles.Order(StringComparer.Ordinal).ToArray();
    public string[] SkillNames => skills.Order(StringComparer.Ordinal).ToArray();

    public void Check()
    {
        ReadAuthorIdentities();
        foreach ((string path, Module owner) in catalog.Sources())
        {
            string text = Regex.Replace(catalog.ReadText(path), @"(?m)^[ \t]*#(?:if|elif|else|endif)\b[^\r\n]*", match => new string(' ', match.Length));
            var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, path: path).GetRoot();
            documents.Add((path, owner, root));
            foreach (BaseTypeDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                int arity = declaration is TypeDeclarationSyntax type ? type.TypeParameterList?.Parameters.Count ?? 0 : 0;
                string key = TypeKey(ContainingName(declaration), declaration.Identifier.ValueText, arity);
                if (retiredTypes.Contains(key))
                    Report("RETIRED_TYPE", path, owner, declaration, $"重新声明已删除类型 {key}。");
                if (!declarations.TryGetValue(key, out List<Module>? owners)) declarations.Add(key, owners = []);
                owners.Add(owner);
            }
        }
        foreach ((string path, Module owner, CompilationUnitSyntax root) in documents)
        {
            DependencyRule[] applicable = rules.DependencyRules.Where(rule => rule.Applies(owner)).ToArray();
            bool checkNames = GuardRules.Matches(rules.SourceIdentityAssemblies, owner.Name);
            var aliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.Alias is not null)
                .GroupBy(item => item.Alias!.Name.Identifier.ValueText).ToDictionary(group => group.Key, group => Name(group.First().Name!), StringComparer.Ordinal);
            string[] imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(item => item.Alias is null && item.StaticKeyword.IsKind(SyntaxKind.None))
                .Select(item => Name(item.Name!)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (node is EnumDeclarationSyntax enumeration && enumeration.Identifier.ValueText == "OrchestrationOperationCode")
                    foreach (EnumMemberDeclarationSyntax member in enumeration.Members)
                        if (rules.RetiredDomainOpcodes.Contains(member.Identifier.ValueText, StringComparer.Ordinal))
                            Report("RETIRED_DOMAIN_OPCODE", path, owner, member, $"核心枚举重新声明已删除领域项 {member.Identifier.ValueText}。");
                if (node is MemberAccessExpressionSyntax memberAccess &&
                    rules.RetiredDomainOpcodes.Contains(memberAccess.Name.Identifier.ValueText, StringComparer.Ordinal))
                {
                    string receiver = Name(memberAccess.Expression);
                    if (aliases.TryGetValue(receiver, out string? alias)) receiver = alias;
                    if (receiver == "OrchestrationOperationCode" || receiver.EndsWith(".OrchestrationOperationCode", StringComparison.Ordinal))
                        Report("RETIRED_DOMAIN_OPCODE", path, owner, node, $"引用已删除领域指令 {memberAccess.Name.Identifier.ValueText}。");
                }
                if (node is IdentifierNameSyntax identifier && identifier.Parent is not MemberAccessExpressionSyntax &&
                    rules.RetiredDomainOpcodes.Contains(identifier.Identifier.ValueText, StringComparer.Ordinal) &&
                    root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(item => item.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) &&
                        Name(item.Name!).EndsWith("OrchestrationOperationCode", StringComparison.Ordinal)))
                    Report("RETIRED_DOMAIN_OPCODE", path, owner, node, $"using static 引用已删除领域指令 {identifier.Identifier.ValueText}。");
                if (node is UsingDirectiveSyntax usingDirective)
                    CheckNamespace(Name(usingDirective.Name!), path, owner, node, applicable);
                if (node is NameSyntax or MemberAccessExpressionSyntax)
                {
                    if (node is SimpleNameSyntax simple && !IsTypeUse(simple)) continue;
                    string raw = Name(node);
                    if (raw.Length == 0) continue;
                    CheckNamespace(raw, path, owner, node, applicable);
                    int arity = node is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count :
                        node is QualifiedNameSyntax { Right: GenericNameSyntax right } ? right.TypeArgumentList.Arguments.Count : 0;
                    foreach (string key in Resolve(raw, arity, node, aliases, imports))
                    {
                        if (retiredTypes.Contains(key))
                            Report("RETIRED_TYPE", path, owner, node, $"引用已删除类型 {key}。");
                        if (!declarations.TryGetValue(key, out List<Module>? owners)) continue;
                        foreach (Module target in owners.DistinctBy(item => item.Path))
                            if (target.Name != owner.Name)
                                foreach (DependencyRule rule in applicable.Where(rule => rule.Forbids(owner, target.Name, "assembly")))
                                    Report(rule.Id, path, owner, node, $"{rule.Reason} 源码类型 {key} 属于 {target.Name}（{target.Path}）。");
                    }
                }
                if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    string value = literal.Token.ValueText;
                    foreach (string retired in retiredTypes)
                        if (value.Contains(retired, StringComparison.Ordinal))
                            Report("RETIRED_TYPE", path, owner, node, $"字符串或反射身份引用已删除类型 {retired}。");
                    if (checkNames && roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase)))
                        Report("CONTENT_IDENTITY_IN_CODE", path, owner, node, $"共享/模拟代码固化角色身份：{value}。");
                    if (checkNames && skills.Contains(value) && IsSkillIdentityContext(literal))
                        Report("CONTENT_IDENTITY_IN_CODE", path, owner, node, $"共享/模拟代码按作者技能身份 {value} 声明常量或分派。");
                }
            }
            if (checkNames)
                foreach (SyntaxToken token in root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)))
                    if (roles.Any(role => token.ValueText.Contains(role, StringComparison.OrdinalIgnoreCase)))
                        Report("CONTENT_IDENTITY_IN_CODE", path, owner, token.Parent!, $"共享/模拟标识符含角色名：{token.ValueText}。");
        }
    }

    void ReadAuthorIdentities()
    {
        if (clientRoot is null) return;
        string directory = Path.Combine(clientRoot, rules.AuthorIdentityDirectory);
        foreach (string character in Directory.EnumerateDirectories(directory)) roles.Add(Path.GetFileName(character));
        string fieldPattern = @"(?m)^\s*(?:" + string.Join('|', rules.SkillIdentityFields.Select(Regex.Escape)) + @"):\s*([^\r\n]+)";
        foreach (string path in Directory.EnumerateFiles(directory, "*.asset", SearchOption.AllDirectories))
            foreach (Match match in Regex.Matches(catalog.ReadText(path), fieldPattern))
                skills.Add(match.Groups[1].Value.Trim().Trim('"', '\''));
    }

    IEnumerable<string> Resolve(string raw, int arity, SyntaxNode node, Dictionary<string, string> aliases, string[] imports)
    {
        string first = raw.Split('.')[0];
        if (aliases.TryGetValue(first, out string? alias)) raw = alias + raw[first.Length..];
        string key = TypeKey("", raw, arity);
        if (Known(key)) { yield return key; yield break; }
        string ns = ContainingName(node);
        while (ns.Length != 0)
        {
            key = TypeKey(ns, raw, arity);
            if (Known(key)) { yield return key; yield break; }
            int separator = ns.LastIndexOf('.');
            ns = separator < 0 ? "" : ns[..separator];
        }
        foreach (string import in imports)
        {
            key = TypeKey(import, raw, arity);
            if (Known(key)) yield return key;
        }
    }

    bool Known(string key) => retiredTypes.Contains(key) || declarations.ContainsKey(key);
    void CheckNamespace(string name, string path, Module owner, SyntaxNode node, DependencyRule[] applicable)
    {
        foreach (DependencyRule rule in applicable.Where(rule => GuardRules.Matches(rule.ForbiddenNamespaces, name)))
            Report(rule.Id, path, owner, node, $"{rule.Reason} 源码直接引用 {name}。");
    }
    void Report(string rule, string path, Module owner, SyntaxNode node, string detail) =>
        catalog.Violations.Add(new(rule, path, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, owner.Name, detail));
    static string TypeKey(string ns, string name, int arity) => (ns.Length == 0 ? name : ns + "." + name) + (arity == 0 ? "" : "`" + arity);
    static string Namespace(SyntaxNode node) => string.Join('.', node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(item => Name(item.Name)));
    static string ContainingName(SyntaxNode node)
    {
        string ns = Namespace(node);
        string parents = string.Join('.', node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(item =>
            TypeKey("", item.Identifier.ValueText, item.TypeParameterList?.Parameters.Count ?? 0)));
        return ns.Length == 0 ? parents : parents.Length == 0 ? ns : ns + "." + parents;
    }
    static string Name(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax name => name.Identifier.ValueText,
        GenericNameSyntax name => name.Identifier.ValueText,
        QualifiedNameSyntax name => Name(name.Left) + "." + Name(name.Right),
        AliasQualifiedNameSyntax name => name.Alias.Identifier.ValueText == "global" ? Name(name.Name) : Name(name.Alias) + "." + Name(name.Name),
        MemberAccessExpressionSyntax name => Name(name.Expression) + "." + Name(name.Name),
        _ => ""
    };
    static bool IsTypeUse(SimpleNameSyntax name)
    {
        if (name.Parent is MemberAccessExpressionSyntax member) return member.Expression == name;
        if (name.Parent is QualifiedNameSyntax qualified) return qualified.Left == name;
        SyntaxNode node = name;
        while (node.Parent is NullableTypeSyntax or ArrayTypeSyntax or PointerTypeSyntax) node = node.Parent;
        return node.Parent switch
        {
            VariableDeclarationSyntax declaration => declaration.Type == node,
            ParameterSyntax parameter => parameter.Type == node,
            PropertyDeclarationSyntax property => property.Type == node,
            MethodDeclarationSyntax method => method.ReturnType == node,
            ObjectCreationExpressionSyntax creation => creation.Type == node,
            CastExpressionSyntax cast => cast.Type == node,
            TypeOfExpressionSyntax or DefaultExpressionSyntax or BaseTypeSyntax or TypeArgumentListSyntax or TypeConstraintSyntax or UsingDirectiveSyntax or CatchDeclarationSyntax or TupleElementSyntax => true,
            ArgumentSyntax argument when argument.Parent?.Parent is InvocationExpressionSyntax invocation && Name(invocation.Expression) == "nameof" => true,
            _ => false
        };
    }
    static bool IsSkillIdentityContext(LiteralExpressionSyntax literal)
    {
        const string identity = @"(?:skill|ability)(?:id|name|identity)?\b";
        bool Matches(string text) => Regex.IsMatch(text, identity, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (SyntaxNode ancestor in literal.Ancestors())
        {
            if (ancestor is VariableDeclaratorSyntax variable) return Matches(variable.Identifier.ValueText);
            if (ancestor is AssignmentExpressionSyntax assignment) return Matches(assignment.Left.ToString());
            if (ancestor is BinaryExpressionSyntax binary) return Matches((binary.Left == literal ? binary.Right : binary.Left).ToString());
            if (ancestor is SwitchStatementSyntax statement) return Matches(statement.Expression.ToString());
            if (ancestor is SwitchExpressionSyntax expression) return Matches(expression.GoverningExpression.ToString());
            if (ancestor is InvocationExpressionSyntax invocation) return Matches(invocation.Expression.ToString()) ||
                invocation.ArgumentList.Arguments.Any(argument => argument.Expression is not LiteralExpressionSyntax && Matches(argument.Expression.ToString()));
            if (ancestor is ObjectCreationExpressionSyntax creation) return Matches(creation.Type.ToString());
            if (ancestor is StatementSyntax or MemberDeclarationSyntax) return false;
        }
        return false;
    }
}
