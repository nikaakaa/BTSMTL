using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string BusinessKeyNamespace = "BTSMTL.Authoring.";
        const string BusinessKeyAttribute = BusinessKeyNamespace + "BtsmtlBusinessKeyAttribute";
        const string BusinessKeyKindAttribute = BusinessKeyNamespace + "BtsmtlBusinessKeyKindAttribute";
        const string BusinessKeyExternalAttribute = BusinessKeyNamespace + "BtsmtlBusinessKeyExternalParameterAttribute";
        const string BusinessKeyRuntime = "global::" + BusinessKeyNamespace;

        static void EmitBusinessKeySources(GeneratorExecutionContext context)
        {
            if (context.Compilation.GetTypeByMetadataName(BusinessKeyAttribute) == null) return;
            var sources = new BusinessKeySources(context);
            foreach (SyntaxTree tree in context.Compilation.SyntaxTrees)
                sources.Collect(tree);
            context.AddSource("BtsmtlBusinessKeySources.g.cs", SourceText.From(sources.Emit(), Encoding.UTF8));
        }

        sealed class BusinessKeySources
        {
            readonly GeneratorExecutionContext m_Context;
            readonly Dictionary<ISymbol, BusinessKeyDeclaration[]> m_Declarations =
                new Dictionary<ISymbol, BusinessKeyDeclaration[]>(SymbolEqualityComparer.Default);
            readonly Dictionary<string, List<BusinessKeyDeclaration>> m_ExternalDeclarations =
                new Dictionary<string, List<BusinessKeyDeclaration>>(StringComparer.Ordinal);
            readonly Dictionary<SyntaxTree, string> m_Revisions = new Dictionary<SyntaxTree, string>();
            readonly SortedSet<string> m_Files = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_Sources = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_Constants = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_ConstantUses = new SortedSet<string>(StringComparer.Ordinal);

            internal BusinessKeySources(GeneratorExecutionContext context)
            {
                m_Context = context;
                foreach (IAssemblySymbol assembly in new[] { context.Compilation.Assembly }
                    .Concat(context.Compilation.SourceModule.ReferencedAssemblySymbols))
                    foreach (AttributeData declaration in assembly.GetAttributes().Where(attribute =>
                        Identity(attribute.AttributeClass) == BusinessKeyExternalAttribute))
                    {
                        // 引用程序集的声明类型不在本编译引用范围时，本编译不可能调用该方法，声明不适用。
                        if (!SymbolEqualityComparer.Default.Equals(assembly, context.Compilation.Assembly) &&
                            declaration.ConstructorArguments[0].Value is ITypeSymbol { TypeKind: TypeKind.Error })
                            continue;
                        AddExternalDeclaration(declaration);
                    }
            }

            internal void Collect(SyntaxTree tree)
            {
                m_Files.Add(Attribute("BtsmtlBusinessKeySourceFileAttribute", Literal(FilePath(tree)), Literal(Revision(tree))));
                SemanticModel model = m_Context.Compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot(m_Context.CancellationToken).DescendantNodes())
                {
                    switch (node)
                    {
                        case VariableDeclaratorSyntax variable:
                            ISymbol symbol = model.GetDeclaredSymbol(variable, m_Context.CancellationToken);
                            if (symbol is IFieldSymbol field && field.IsConst && field.Type.SpecialType == SpecialType.System_String)
                                AddConstant(symbol, variable.Initializer.Value);
                            if (symbol is IFieldSymbol && variable.Initializer != null)
                                AddValue(symbol, model.GetOperation(variable.Initializer.Value, m_Context.CancellationToken));
                            else if (symbol is IFieldSymbol)
                                Declarations(symbol);
                            break;
                        case ParameterSyntax parameter:
                            ISymbol parameterSymbol = model.GetDeclaredSymbol(parameter, m_Context.CancellationToken);
                            Declarations(parameterSymbol);
                            if (parameter.Default != null)
                                AddValue(parameterSymbol, model.GetOperation(parameter.Default.Value, m_Context.CancellationToken));
                            break;
                        case PropertyDeclarationSyntax property:
                            ISymbol propertySymbol = model.GetDeclaredSymbol(property, m_Context.CancellationToken);
                            Declarations(propertySymbol);
                            if (property.Initializer != null)
                                AddValue(propertySymbol, model.GetOperation(property.Initializer.Value, m_Context.CancellationToken));
                            break;
                        case ArrowExpressionClauseSyntax arrow:
                            if (arrow.Parent is PropertyDeclarationSyntax arrowProperty)
                                AddValue(model.GetDeclaredSymbol(arrowProperty, m_Context.CancellationToken),
                                    model.GetOperation(arrow.Expression, m_Context.CancellationToken));
                            else if (model.GetEnclosingSymbol(arrow.Expression.SpanStart, m_Context.CancellationToken) is IMethodSymbol getter &&
                                getter.MethodKind == MethodKind.PropertyGet)
                                AddValue(getter.AssociatedSymbol, model.GetOperation(arrow.Expression, m_Context.CancellationToken));
                            break;
                        case ReturnStatementSyntax result when result.Expression != null:
                            ISymbol resultOwner = model.GetEnclosingSymbol(result.SpanStart, m_Context.CancellationToken);
                            if (resultOwner is IMethodSymbol resultGetter && resultGetter.MethodKind == MethodKind.PropertyGet)
                                AddValue(resultGetter.AssociatedSymbol, model.GetOperation(result.Expression, m_Context.CancellationToken));
                            break;
                        case AssignmentExpressionSyntax assignment:
                            IOperation assignmentOperation = model.GetOperation(assignment, m_Context.CancellationToken);
                            if (assignmentOperation is ISimpleAssignmentOperation simple)
                                AddValue(AssignedSymbol(simple.Target), simple.Value);
                            else if (assignmentOperation is ICompoundAssignmentOperation compound)
                                AddValue(AssignedSymbol(compound.Target), compound.Value, failure: "business_key_compound_assignment");
                            break;
                        case InvocationExpressionSyntax _:
                        case ConstructorInitializerSyntax _:
                            if (model.GetOperation(node, m_Context.CancellationToken) is IInvocationOperation invocation)
                                AddArguments(invocation.Arguments);
                            break;
                        case ObjectCreationExpressionSyntax _:
                        case ImplicitObjectCreationExpressionSyntax _:
                            if (model.GetOperation(node, m_Context.CancellationToken) is IObjectCreationOperation creation)
                                AddArguments(creation.Arguments);
                            break;
                        case ElementAccessExpressionSyntax element:
                            if (model.GetOperation(element, m_Context.CancellationToken) is IPropertyReferenceOperation indexer)
                                AddArguments(indexer.Arguments);
                            break;
                        case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                            if (model.GetOperation(initializer, m_Context.CancellationToken) is IObjectOrCollectionInitializerOperation collection)
                                foreach (IOperation item in collection.Initializers)
                                    if (item is IInvocationOperation add && add.IsImplicit)
                                        AddArguments(add.Arguments);
                            break;
                    }
                }
            }

            void AddExternalDeclaration(AttributeData attribute)
            {
                var type = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
                if (type == null)
                {
                    Report(attribute, BusinessKeyExternalAttribute, "外部业务键声明必须提供真实声明类型");
                    return;
                }
                string methodName = (string)attribute.ConstructorArguments[1].Value;
                string parameterName = (string)attribute.ConstructorArguments[2].Value;
                ITypeSymbol[] signature = attribute.ConstructorArguments[6].Values.Select(value => (ITypeSymbol)value.Value).ToArray();
                IMethodSymbol[] methods = type.GetMembers(methodName).OfType<IMethodSymbol>().Where(method =>
                    method.Parameters.Length == signature.Length && method.Parameters.Select((parameter, index) =>
                        SymbolEqualityComparer.Default.Equals(parameter.Type, signature[index])).All(equal => equal)).ToArray();
                IParameterSymbol parameter = methods.Length == 1
                    ? methods[0].Parameters.SingleOrDefault(candidate => candidate.Name == parameterName) : null;
                if (parameter == null)
                {
                    Report(attribute, Identity(type) + "." + methodName,
                        "外部业务键声明必须唯一绑定实际方法签名及参数");
                    return;
                }
                var declaration = new BusinessKeyDeclaration(attribute,
                    (string)attribute.ConstructorArguments[3].Value, (string)attribute.ConstructorArguments[4].Value,
                    (int)attribute.ConstructorArguments[5].Value);
                Validate(parameter, declaration);
                string key = BindingKey(parameter);
                if (!m_ExternalDeclarations.TryGetValue(key, out List<BusinessKeyDeclaration> declarations))
                    m_ExternalDeclarations.Add(key, declarations = new List<BusinessKeyDeclaration>());
                declarations.Add(declaration);
            }

            BusinessKeyDeclaration[] Declarations(ISymbol symbol)
            {
                if (symbol == null) return Array.Empty<BusinessKeyDeclaration>();
                symbol = symbol.OriginalDefinition;
                if (m_Declarations.TryGetValue(symbol, out BusinessKeyDeclaration[] declarations)) return declarations;
                var values = new List<BusinessKeyDeclaration>();
                foreach (AttributeData attribute in symbol.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == BusinessKeyAttribute))
                {
                    bool conditional = attribute.ConstructorArguments.Length == 2;
                    var declaration = new BusinessKeyDeclaration(attribute, (string)attribute.ConstructorArguments[0].Value,
                        conditional ? null : (string)attribute.ConstructorArguments[1].Value,
                        (int)attribute.ConstructorArguments[conditional ? 1 : 2].Value);
                    Validate(symbol, declaration);
                    values.Add(declaration);
                }
                if (m_ExternalDeclarations.TryGetValue(BindingKey(symbol), out List<BusinessKeyDeclaration> external))
                    values.AddRange(external);
                declarations = values.ToArray();
                m_Declarations.Add(symbol, declarations);
                return declarations;
            }

            void Validate(ISymbol symbol, BusinessKeyDeclaration declaration)
            {
                ITypeSymbol type = symbol is IParameterSymbol parameter ? parameter.Type :
                    symbol is IFieldSymbol field ? field.Type : symbol is IPropertySymbol property ? property.Type : null;
                string reason = null;
                if (type?.SpecialType != SpecialType.System_String &&
                    !(type is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_String))
                    reason = "业务键语义只声明原 string 或 string[] 值";
                else if (string.IsNullOrWhiteSpace(declaration.Domain) ||
                    (declaration.KeyKind == null) == (declaration.KindParameter == null) ||
                    declaration.KeyKind != null && string.IsNullOrWhiteSpace(declaration.KeyKind) ||
                    declaration.KindParameter != null && string.IsNullOrWhiteSpace(declaration.KindParameter))
                    reason = "业务键语义必须声明正式 domain，并选择固定 keyKind 或真实枚举参数 KindParameter";
                else if (declaration.Role != 0 && declaration.Role != 1)
                    reason = "业务键语义 role 必须为 Declaration 或 Reference";
                else if (declaration.KindType != null && declaration.KindParameter == null)
                    reason = "KindType 只用于同次调用的 KindParameter 语义";
                else if (declaration.KindParameter != null)
                {
                    IParameterSymbol selector = symbol is IParameterSymbol keyParameter && keyParameter.ContainingSymbol is IMethodSymbol method
                        ? method.Parameters.SingleOrDefault(candidate => candidate.Name == declaration.KindParameter) : null;
                    ITypeSymbol kindType = declaration.KindType ?? selector?.Type;
                    if (selector == null || kindType?.TypeKind != TypeKind.Enum ||
                        !(SymbolEqualityComparer.Default.Equals(selector.Type, kindType) || IsBusinessKeyIntegral(selector.Type.SpecialType)))
                        reason = "KindParameter 必须绑定同一方法的正式枚举参数，或由 KindType 显式声明该整数参数的正式枚举";
                }
                if (reason != null) Report(declaration.Attribute, BusinessKeySymbol(symbol), reason);
            }

            void AddArguments(IEnumerable<IArgumentOperation> arguments)
            {
                IArgumentOperation[] bindings = arguments.ToArray();
                foreach (IArgumentOperation argument in bindings)
                    AddValue(argument.Parameter, argument.Value, bindings,
                        argument.ArgumentKind == ArgumentKind.DefaultValue ? "business_key_implicit_default" : null);
            }

            void AddValue(ISymbol symbol, IOperation value, IArgumentOperation[] bindings = null, string failure = null)
            {
                BusinessKeyDeclaration[] declarations = Declarations(symbol);
                if (declarations.Length == 0) return;
                while (value is IConversionOperation conversion && conversion.IsImplicit)
                    value = conversion.Operand;
                if (value is IArrayCreationOperation array && array.Initializer != null)
                {
                    foreach (IOperation element in array.Initializer.ElementValues)
                        AddValue(symbol, element, bindings, failure);
                    return;
                }
                SyntaxNode expression = value.Syntax;
                SemanticModel model = m_Context.Compilation.GetSemanticModel(expression.SyntaxTree);
                ISymbol owner = model.GetEnclosingSymbol(expression.SpanStart, m_Context.CancellationToken);
                string filePath = FilePath(expression.SyntaxTree);
                Optional<object> constant = value.ConstantValue;
                string key = constant.HasValue ? constant.Value as string : null;
                failure = failure ?? (!Path.IsPathRooted(filePath) ? "business_key_source_path_unresolved" :
                    key == null ? "business_key_nonconstant_expression" : null);
                foreach (BusinessKeyDeclaration declaration in declarations)
                {
                    string kind = ResolveKind(declaration, bindings, out string kindFailure);
                    string sourceFailure = failure ?? kindFailure;
                    m_Sources.Add(Attribute("BtsmtlBusinessKeySourceAttribute",
                        Literal(declaration.Domain), Literal(kind),
                        BusinessKeyRuntime + "BtsmtlBusinessKeyRole." + (declaration.Role == 0 ? "Declaration" : "Reference"),
                        Literal(symbol.ContainingAssembly.Identity.ToString()), Literal(BusinessKeySymbol(symbol)),
                        Literal(RootSymbol(owner)), Literal(BusinessKeySymbol(owner)), Literal(filePath),
                        Literal(Revision(expression.SyntaxTree)), Number(expression.SpanStart), Number(expression.Span.Length),
                        BusinessKeyRuntime + "BtsmtlBusinessKeyResolution." + (sourceFailure == null ? "Constant" : "Unresolved"),
                        Literal(key), Literal(sourceFailure)));
                }
                new BusinessKeyConstantUses(this, expression).Visit(value);
            }

            static string ResolveKind(BusinessKeyDeclaration declaration, IArgumentOperation[] bindings, out string failure)
            {
                failure = null;
                if (declaration.KindParameter == null) return declaration.KeyKind;
                if (bindings == null)
                {
                    failure = "business_key_kind_call_unavailable";
                    return null;
                }
                IArgumentOperation selector = bindings.SingleOrDefault(argument => argument.Parameter.Name == declaration.KindParameter);
                if (selector == null || !selector.Value.ConstantValue.HasValue)
                {
                    failure = "business_key_kind_nonconstant";
                    return null;
                }
                var kindType = (INamedTypeSymbol)(declaration.KindType ?? selector.Parameter.Type);
                decimal kindValue = Convert.ToDecimal(selector.Value.ConstantValue.Value, CultureInfo.InvariantCulture);
                string[] kinds = kindType.GetMembers().OfType<IFieldSymbol>()
                    .Where(field => field.HasConstantValue && Convert.ToDecimal(field.ConstantValue, CultureInfo.InvariantCulture) == kindValue)
                    .SelectMany(field => field.GetAttributes()).Where(attribute => Identity(attribute.AttributeClass) == BusinessKeyKindAttribute &&
                        (string)attribute.ConstructorArguments[0].Value == declaration.Domain)
                    .Select(attribute => (string)attribute.ConstructorArguments[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                if (kinds.Length != 1 || string.IsNullOrWhiteSpace(kinds[0]))
                {
                    failure = kinds.Length > 1 ? "business_key_kind_ambiguous" : "business_key_kind_undeclared";
                    return null;
                }
                return kinds[0];
            }

            void Report(AttributeData attribute, string symbol, string reason) => m_Context.ReportDiagnostic(Diagnostic.Create(
                InvalidValueDeclaration, attribute.ApplicationSyntaxReference?.GetSyntax(m_Context.CancellationToken).GetLocation(), symbol, reason));

            static string BindingKey(ISymbol symbol) => symbol.ContainingAssembly.Identity + ":" + BusinessKeySymbol(symbol);

            static bool IsBusinessKeyIntegral(SpecialType type) => type == SpecialType.System_Byte || type == SpecialType.System_SByte ||
                type == SpecialType.System_Int16 || type == SpecialType.System_UInt16 || type == SpecialType.System_Int32 ||
                type == SpecialType.System_UInt32 || type == SpecialType.System_Int64 || type == SpecialType.System_UInt64;

            void AddConstant(ISymbol symbol, ExpressionSyntax expression)
            {
                string value = symbol is IFieldSymbol field ? field.ConstantValue as string : ((ILocalSymbol)symbol).ConstantValue as string;
                m_Constants.Add(Attribute("BtsmtlBusinessKeyConstantAttribute", Literal(BusinessKeySymbol(symbol)),
                    Literal(RootSymbol(symbol)), Literal(FilePath(expression.SyntaxTree)), Literal(Revision(expression.SyntaxTree)),
                    Number(expression.SpanStart), Number(expression.Span.Length), Literal(value)));
            }

            void AddConstantUse(SyntaxNode expression, ISymbol constant)
            {
                if (constant is ILocalSymbol local && local.Type.SpecialType == SpecialType.System_String)
                    AddConstant(local, ((VariableDeclaratorSyntax)local.DeclaringSyntaxReferences.Single()
                        .GetSyntax(m_Context.CancellationToken)).Initializer.Value);
                m_ConstantUses.Add(Attribute("BtsmtlBusinessKeyConstantUseAttribute", Literal(FilePath(expression.SyntaxTree)),
                    Number(expression.SpanStart), Number(expression.Span.Length), Literal(constant.ContainingAssembly.Identity.ToString()),
                    Literal(BusinessKeySymbol(constant))));
            }

            string FilePath(SyntaxTree tree)
            {
                string path = tree.FilePath;
                if (Path.IsPathRooted(path)) return path;
                return m_Context.Compilation.Options.SourceReferenceResolver?.NormalizePath(path, null) ?? path;
            }

            string Revision(SyntaxTree tree)
            {
                if (m_Revisions.TryGetValue(tree, out string revision)) return revision;
                string text = tree.GetText(m_Context.CancellationToken).ToString();
                var bytes = new byte[text.Length * 2];
                for (int i = 0; i < text.Length; i++)
                {
                    bytes[i * 2] = (byte)text[i];
                    bytes[i * 2 + 1] = (byte)(text[i] >> 8);
                }
                using (SHA256 hash = SHA256.Create())
                    revision = string.Concat(hash.ComputeHash(bytes).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                m_Revisions.Add(tree, revision);
                return revision;
            }

            internal string Emit()
            {
                var code = new StringBuilder();
                code.AppendLine(Attribute("BtsmtlBusinessKeyMetadataAttribute", "2", Literal(m_Context.Compilation.Assembly.Identity.ToString()),
                    Number(m_Files.Count),
                    Number(m_Declarations.Count(item => SymbolEqualityComparer.Default.Equals(item.Key.ContainingAssembly,
                        m_Context.Compilation.Assembly) && item.Value.Length != 0)),
                    Number(m_Sources.Count), Number(m_Constants.Count), Number(m_ConstantUses.Count)));
                foreach (string row in m_Files) code.AppendLine(row);
                foreach (string row in m_Sources) code.AppendLine(row);
                foreach (string row in m_Constants) code.AppendLine(row);
                foreach (string row in m_ConstantUses) code.AppendLine(row);
                return code.ToString();
            }

            static ISymbol AssignedSymbol(IOperation target) => target is IFieldReferenceOperation field ? field.Field :
                target is IPropertyReferenceOperation property ? property.Property : null;

            static string RootSymbol(ISymbol symbol)
            {
                INamedTypeSymbol root = symbol is INamedTypeSymbol type ? type : symbol.ContainingType;
                while (root.ContainingType != null) root = root.ContainingType;
                return BusinessKeySymbol(root);
            }

            static string BusinessKeySymbol(ISymbol symbol)
            {
                if (symbol is IParameterSymbol parameter)
                    return BusinessKeySymbol(parameter.ContainingSymbol) + "#parameter:" + Number(parameter.Ordinal);
                if (symbol is ILocalSymbol local)
                    return BusinessKeySymbol(local.ContainingSymbol) + "#local:" + local.Name + ":" +
                        Number(local.DeclaringSyntaxReferences.Single().Span.Start);
                return symbol.OriginalDefinition.GetDocumentationCommentId() ?? Identity(symbol);
            }

            static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
            static string Literal(string value) => value == null ? "null" : SymbolDisplay.FormatLiteral(value, true);
            static string Attribute(string name, params string[] arguments) =>
                "[assembly: " + BusinessKeyRuntime + name + "(" + string.Join(", ", arguments) + ")]";

            sealed class BusinessKeyDeclaration
            {
                internal readonly AttributeData Attribute;
                internal readonly string Domain;
                internal readonly string KeyKind;
                internal readonly int Role;
                internal readonly string KindParameter;
                internal readonly ITypeSymbol KindType;

                internal BusinessKeyDeclaration(AttributeData attribute, string domain, string keyKind, int role)
                {
                    Attribute = attribute;
                    Domain = domain;
                    KeyKind = keyKind;
                    Role = role;
                    KindParameter = attribute.NamedArguments.Where(argument => argument.Key == "KindParameter")
                        .Select(argument => (string)argument.Value.Value).SingleOrDefault();
                    KindType = attribute.NamedArguments.Where(argument => argument.Key == "KindType")
                        .Select(argument => (ITypeSymbol)argument.Value.Value).SingleOrDefault();
                }
            }

            sealed class BusinessKeyConstantUses : OperationWalker
            {
                readonly BusinessKeySources m_Owner;
                readonly SyntaxNode m_Expression;

                internal BusinessKeyConstantUses(BusinessKeySources owner, SyntaxNode expression)
                {
                    m_Owner = owner;
                    m_Expression = expression;
                }

                public override void VisitFieldReference(IFieldReferenceOperation operation)
                {
                    if (operation.Field.IsConst) m_Owner.AddConstantUse(m_Expression, operation.Field);
                    base.VisitFieldReference(operation);
                }

                public override void VisitLocalReference(ILocalReferenceOperation operation)
                {
                    if (operation.Local.IsConst) m_Owner.AddConstantUse(m_Expression, operation.Local);
                    base.VisitLocalReference(operation);
                }
            }
        }
    }
}
