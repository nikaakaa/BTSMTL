using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Btsmtl.DependencyGuard;

sealed class WorkbenchSourceGuard(DependencyCatalog catalog, GuardRules rules, string sdkRoot)
{
    static readonly JsonSerializerOptions s_Json = new() { PropertyNameCaseInsensitive = true };
    static readonly HashSet<string> s_Maps = ["Map", "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "MapGroup"];
    readonly WorkbenchSourceRules m_Rules = rules.WorkbenchSourceRules;
    public int SourceCount { get; private set; }

    public void Check()
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (Source source in Sources(m_Rules.HostRoot, ".cs"))
            Routes(source, routes, groups);
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in m_Rules.FieldDeclarationRoots)
        foreach (Source source in Sources(root, ".cs"))
        {
            SyntaxNode syntax = SourceGuard.ParseAllBranches(source.Text, source.File);
            foreach (VariableDeclaratorSyntax field in syntax.DescendantNodes().OfType<FieldDeclarationSyntax>()
                         .SelectMany(field => field.Declaration.Variables))
                fields.Add(field.Identifier.ValueText);
            foreach (PropertyDeclarationSyntax property in syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>())
                fields.Add(property.Identifier.ValueText);
        }
        Source[] web = Sources(m_Rules.WebRoot, ".ts", ".tsx").ToArray();
        string parser = Path.Combine(AppContext.BaseDirectory, "workbench-source-parser.cjs");
        string typescript = Path.GetFullPath(Path.Combine(sdkRoot, m_Rules.TypeScriptModule));
        Record(parser);
        Record(typescript);
        var start = new ProcessStartInfo("node")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(parser);
        start.ArgumentList.Add(typescript);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(JsonSerializer.Serialize(web));
        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException("工作台 TypeScript 源码分析失败：" + error.GetAwaiter().GetResult());
        ParsedWeb parsed = JsonSerializer.Deserialize<ParsedWeb>(output.GetAwaiter().GetResult(), s_Json)!;
        Record(parsed.Node);
        foreach (WebUse use in parsed.Routes)
        {
            string path = use.Text.Split('?')[0].TrimEnd('/');
            bool registered = use.Prefix
                ? routes.Any(route => StaticPrefix(route).StartsWith(path, StringComparison.Ordinal) ||
                    route.Contains('{') && path.StartsWith(StaticPrefix(route), StringComparison.Ordinal))
                : groups.Contains(path) || routes.Any(route => Regex.IsMatch(path, RoutePattern(route)));
            if (!registered)
                catalog.Violations.Add(new(m_Rules.RouteRule, use.File, use.Line, m_Rules.WebRoot,
                    $"网页路由{(use.Prefix ? "模板前缀" : "字面量")} '{use.Text}' 未由工作台 Host 注册。"));
        }
        foreach (WebUse use in parsed.Fields)
            if (!fields.Contains(use.Text))
                catalog.Violations.Add(new(m_Rules.FieldRule, use.File, use.Line, m_Rules.WebRoot,
                    $"作者字段 '{use.Text}' 未在 BTSMTL 包声明；请消费文档适配的语义角色。"));
    }

    void Routes(Source source, HashSet<string> routes, HashSet<string> groups)
    {
        SyntaxNode root = SourceGuard.ParseAllBranches(source.Text, source.File);
        foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member || !s_Maps.Contains(member.Name.Identifier.ValueText))
                continue;
            string? path = Literal(call.ArgumentList.Arguments.First().Expression);
            if (path is null)
            {
                catalog.Violations.Add(new(m_Rules.RouteRule, source.File, call.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    m_Rules.HostRoot, "路由注册路径必须能从正式字符串声明解析。"));
                continue;
            }
            string full = (Receiver(member.Expression, call) + path).TrimEnd('/');
            if (member.Name.Identifier.ValueText == "MapGroup") groups.Add(full);
            else routes.Add(full);
        }

        string Receiver(ExpressionSyntax expression, SyntaxNode context)
        {
            if (expression is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax access &&
                access.Name.Identifier.ValueText == "MapGroup")
                return Receiver(access.Expression, context) + Literal(invocation.ArgumentList.Arguments[0].Expression);
            if (expression is IdentifierNameSyntax name)
            {
                SyntaxNode? scope = context.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax);
                VariableDeclaratorSyntax? declaration = scope?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault(variable => variable.Identifier.ValueText == name.Identifier.ValueText &&
                        variable.Initializer?.Value is InvocationExpressionSyntax group && group.Expression is MemberAccessExpressionSyntax method &&
                        method.Name.Identifier.ValueText == "MapGroup");
                if (declaration?.Initializer?.Value is ExpressionSyntax initializer)
                    return Receiver(initializer, declaration);
            }
            return "";
        }
    }

    static string? Literal(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        BinaryExpressionSyntax add when add.IsKind(SyntaxKind.AddExpression) && Literal(add.Left) is string left && Literal(add.Right) is string right => left + right,
        _ => null
    };

    static string StaticPrefix(string route) => route.Split('{')[0];
    static string RoutePattern(string route) => "^" + string.Concat(Regex.Split(route, "(\\{[^}]+\\})")
        .Select(part => part.StartsWith('{') ? part.StartsWith("{*") ? ".+" : "[^/]+" : Regex.Escape(part))) + "$";

    IEnumerable<Source> Sources(string relative, params string[] extensions)
    {
        foreach (string file in Files(Path.GetFullPath(Path.Combine(sdkRoot, relative))))
            if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                byte[] bytes = File.ReadAllBytes(file);
                catalog.RecordInput(file, bytes);
                SourceCount++;
                yield return new(file, Encoding.UTF8.GetString(bytes));
            }
    }

    IEnumerable<string> Files(string directory) => Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal)
        .Concat(Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal)
            .Where(child => !m_Rules.ExcludedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)).SelectMany(Files));

    void Record(string path) => catalog.RecordInput(path, File.ReadAllBytes(path));
    sealed record Source(string File, string Text);
    sealed record WebUse(string File, int Line, string Text, bool Prefix);
    sealed record ParsedWeb(string Node, WebUse[] Routes, WebUse[] Fields);
}
