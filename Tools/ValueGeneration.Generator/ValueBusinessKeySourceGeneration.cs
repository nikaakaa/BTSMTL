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
            readonly Dictionary<ISymbol, AttributeData[]> m_Declarations =
                new Dictionary<ISymbol, AttributeData[]>(SymbolEqualityComparer.Default);
            readonly Dictionary<SyntaxTree, string> m_Revisions = new Dictionary<SyntaxTree, string>();
            readonly SortedSet<string> m_Sources = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_Constants = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_ConstantUses = new SortedSet<string>(StringComparer.Ordinal);

            internal BusinessKeySources(GeneratorExecutionContext context) => m_Context = context;

            internal void Collect(SyntaxTree tree)
            {
                SemanticModel model = m_Context.Compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot(m_Context.CancellationToken).DescendantNodes())
                {
                    switch (node)
                    {
                        case VariableDeclaratorSyntax variable:
                            ISymbol symbol = model.GetDeclaredSymbol(variable, m_Context.CancellationToken);
                            if (symbol is IFieldSymbol field && field.IsConst && field.Type.SpecialType == SpecialType.System_String ||
                                symbol is ILocalSymbol local && local.IsConst && local.Type.SpecialType == SpecialType.System_String)
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
                            ISymbol arrowOwner = model.GetEnclosingSymbol(arrow.Expression.SpanStart, m_Context.CancellationToken);
                            if (arrowOwner is IMethodSymbol getter && getter.MethodKind == MethodKind.PropertyGet)
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
                                AddValue(AssignedSymbol(compound.Target), compound.Value, "business_key_compound_assignment");
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

            AttributeData[] Declarations(ISymbol symbol)
            {
                if (symbol == null) return Array.Empty<AttributeData>();
                symbol = symbol.OriginalDefinition;
                if (m_Declarations.TryGetValue(symbol, out AttributeData[] declarations)) return declarations;
                declarations = symbol.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == BusinessKeyAttribute).ToArray();
                m_Declarations.Add(symbol, declarations);
                ITypeSymbol type = symbol is IParameterSymbol parameter ? parameter.Type :
                    symbol is IFieldSymbol field ? field.Type : symbol is IPropertySymbol property ? property.Type : null;
                foreach (AttributeData declaration in declarations)
                {
                    string reason = null;
                    if (type?.SpecialType != SpecialType.System_String &&
                        !(type is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_String))
                        reason = "业务键语义只声明原 string 或 string[] 值";
                    else if (string.IsNullOrWhiteSpace((string)declaration.ConstructorArguments[0].Value) ||
                        string.IsNullOrWhiteSpace((string)declaration.ConstructorArguments[1].Value))
                        reason = "业务键语义必须声明正式 domain 和 keyKind";
                    else if ((int)declaration.ConstructorArguments[2].Value != 0 && (int)declaration.ConstructorArguments[2].Value != 1)
                        reason = "业务键语义 role 必须为 Declaration 或 Reference";
                    if (reason != null)
                        m_Context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration,
                            declaration.ApplicationSyntaxReference?.GetSyntax(m_Context.CancellationToken).GetLocation(),
                            BusinessKeySymbol(symbol), reason));
                }
                return declarations;
            }

            void AddArguments(IEnumerable<IArgumentOperation> arguments)
            {
                foreach (IArgumentOperation argument in arguments)
                    AddValue(argument.Parameter, argument.Value,
                        argument.ArgumentKind == ArgumentKind.DefaultValue ? "business_key_implicit_default" : null);
            }

            void AddValue(ISymbol symbol, IOperation value, string failure = null)
            {
                AttributeData[] declarations = Declarations(symbol);
                if (declarations.Length == 0) return;
                while (value is IConversionOperation conversion && conversion.IsImplicit)
                    value = conversion.Operand;
                if (value is IArrayCreationOperation array && array.Initializer != null)
                {
                    foreach (IOperation element in array.Initializer.ElementValues)
                        AddValue(symbol, element, failure);
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
                foreach (AttributeData declaration in declarations)
                    m_Sources.Add(Attribute("BtsmtlBusinessKeySourceAttribute",
                        Literal((string)declaration.ConstructorArguments[0].Value),
                        Literal((string)declaration.ConstructorArguments[1].Value),
                        BusinessKeyRuntime + "BtsmtlBusinessKeyRole." + ((int)declaration.ConstructorArguments[2].Value == 0 ? "Declaration" : "Reference"),
                        Literal(symbol.ContainingAssembly.Identity.ToString()), Literal(BusinessKeySymbol(symbol)),
                        Literal(RootSymbol(owner)), Literal(BusinessKeySymbol(owner)), Literal(filePath),
                        Literal(Revision(expression.SyntaxTree)), Number(expression.SpanStart), Number(expression.Span.Length),
                        BusinessKeyRuntime + "BtsmtlBusinessKeyResolution." + (failure == null ? "Constant" : "Unresolved"),
                        Literal(key), Literal(failure)));
                new BusinessKeyConstantUses(this, expression).Visit(value);
            }

            void AddConstant(ISymbol symbol, ExpressionSyntax expression)
            {
                string value = symbol is IFieldSymbol field ? field.ConstantValue as string : ((ILocalSymbol)symbol).ConstantValue as string;
                m_Constants.Add(Attribute("BtsmtlBusinessKeyConstantAttribute", Literal(BusinessKeySymbol(symbol)),
                    Literal(RootSymbol(symbol)), Literal(FilePath(expression.SyntaxTree)), Literal(Revision(expression.SyntaxTree)),
                    Number(expression.SpanStart), Number(expression.Span.Length), Literal(value)));
            }

            void AddConstantUse(SyntaxNode expression, ISymbol constant)
            {
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
                code.AppendLine(Attribute("BtsmtlBusinessKeyMetadataAttribute", "1", Literal(m_Context.Compilation.Assembly.Identity.ToString()),
                    Number(m_Context.Compilation.SyntaxTrees.Count()),
                    Number(m_Declarations.Count(item => SymbolEqualityComparer.Default.Equals(item.Key.ContainingAssembly,
                        m_Context.Compilation.Assembly) && item.Value.Length != 0)),
                    Number(m_Sources.Count), Number(m_Constants.Count), Number(m_ConstantUses.Count)));
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
