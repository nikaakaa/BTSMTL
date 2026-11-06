using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string AuthoringNamespace = "BTSMTL.Authoring.Graph.";
        const string AuthoringFieldAttribute = AuthoringNamespace + "GraphAuthoringFieldAttribute";
        const string AuthoringRuntime = "global::" + AuthoringNamespace;

        sealed class AuthoringField
        {
            public ISymbol Symbol;
            public ITypeSymbol Type;
            public AttributeData Metadata;
            public string Id;
            public bool ReadOnly;
            public string[] Path;
            // 特性未写字段身份时由成员名首字母小写得出，其余构造参数从首位开始。
            public bool ExplicitId => Metadata.ConstructorArguments.Length != 0 &&
                Metadata.ConstructorArguments[0].Type?.SpecialType == SpecialType.System_String;
            public IEnumerable<TypedConstant> Arguments => Metadata.ConstructorArguments.Skip(ExplicitId ? 1 : 0);
            public string Name => string.Join("_", Path);
            // 辅助特性与字段间引用写 nameof(成员)，按成员路径找到字段后再取正式字段身份。
            public string MemberPath => string.Join(".", Path);
            public string Access => string.Join(".", Path.Select(Escape));
        }

        sealed class AuthoringArgument
        {
            public ITypeSymbol Type;
            public string ParameterName;
            public AuthoringField Field;
            public AuthoringArgument[] Children = Array.Empty<AuthoringArgument>();
            public bool ToArray;
            public IEnumerable<AuthoringField> Fields => Field != null
                ? new[] { Field } : Children.SelectMany(child => child.Fields);
            public string Expression => Field != null
                ? ToArray ? "global::System.Linq.Enumerable.ToArray(" + AuthoringCandidate(Field) + ")" : AuthoringCandidate(Field)
                : "new " + TypeName(Type) + "(" + string.Join(", ", Children.Select(child => Escape(child.ParameterName) + ": " + child.Expression)) + ")";
            public string Description => "new " + AuthoringRuntime + "GraphAuthoringArgument(typeof(" + TypeName(Type) + "), " + Quote(ParameterName) + ", " +
                (Field != null ? Quote(Field.Id) : "null") +
                (Children.Length == 0 ? "" : ", " + string.Join(", ", Children.Select(child => child.Description))) + ")";
        }

        static string Quote(string text) => SymbolDisplay.FormatLiteral(text, true);

        static AuthoringField[] AuthoringFields(INamedTypeSymbol type) =>
            AuthoringFields(type, Array.Empty<string>(), string.Empty).ToArray();

        static IEnumerable<AuthoringField> AuthoringFields(INamedTypeSymbol type, string[] path, string prefix)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                string[] memberPath = path.Concat(new[] { member.Name }).ToArray();
                ITypeSymbol memberType = member is IPropertySymbol property ? property.Type :
                    member is IFieldSymbol declaredField ? declaredField.Type : null;
                foreach (AttributeData metadata in AuthoringAttributes(member, "GraphAuthoringField"))
                    yield return new AuthoringField
                {
                    Symbol = member,
                    Type = memberType,
                    Metadata = metadata,
                    Id = prefix + (metadata.ConstructorArguments.Length != 0 &&
                        metadata.ConstructorArguments[0].Type?.SpecialType == SpecialType.System_String
                            ? (string)metadata.ConstructorArguments[0].Value
                            : char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1)),
                    ReadOnly = metadata.NamedArguments.Any(argument => argument.Key == "ReadOnly" && (bool)argument.Value.Value),
                    Path = memberPath
                };
                AttributeData inline = Attribute(member, AuthoringNamespace + "GraphAuthoringInlineAttribute");
                if (inline != null && memberType is INamedTypeSymbol embedded)
                    foreach (AuthoringField field in AuthoringFields(embedded, memberPath,
                        prefix + (string)inline.ConstructorArguments[0].Value + "."))
                        yield return field;
            }
        }

        static string AuthoringMemberName(AttributeData metadata) => metadata.NamedArguments
            .Where(argument => argument.Key == "MemberName").Select(argument => (string)argument.Value.Value).SingleOrDefault();

        static AttributeData Attribute(ISymbol symbol, string name) => symbol.GetAttributes()
            .SingleOrDefault(attribute => Identity(attribute.AttributeClass) == name);

        static IEnumerable<AttributeData> AuthoringAttributes(ISymbol symbol, string suffix)
        {
            string identity = AuthoringNamespace + suffix + "Attribute";
            IEnumerable<AttributeData> declarations = symbol.GetAttributes().Where(attribute =>
                Identity(attribute.AttributeClass) == identity);
            if (symbol is INamedTypeSymbol)
                return declarations;
            return declarations.Concat(symbol.ContainingType.GetAttributes().Where(attribute =>
                Identity(attribute.AttributeClass) == identity && AuthoringMemberName(attribute) == symbol.Name));
        }

        static ISymbol AuthoringAuxiliary(INamedTypeSymbol type, string suffix, AuthoringField field) =>
            type.GetMembers().SingleOrDefault(member => AuthoringAttributes(member, suffix)
                .Any(attribute => (string)attribute.ConstructorArguments[0].Value == field.MemberPath));

        static bool AuthoringWritable(AuthoringField field) => !field.ReadOnly &&
            (field.Symbol is IFieldSymbol member ? !member.IsReadOnly :
                ((IPropertySymbol)field.Symbol).SetMethod?.DeclaredAccessibility == Accessibility.Public);

        static bool AuthoringTypeHasFields(INamedTypeSymbol type) => type != null &&
            (HasAttribute(type, AuthoringFieldAttribute) ||
                HasAttribute(type, AuthoringNamespace + "GraphAuthoringChoicesAttribute") ||
                type.GetMembers().Any(member => HasAttribute(member, AuthoringFieldAttribute) ||
                    HasAttribute(member, AuthoringNamespace + "GraphAuthoringInlineAttribute")));

        static ITypeSymbol AuthoringResultType(ISymbol member) => member is IPropertySymbol property
            ? property.Type : ((IMethodSymbol)member).ReturnType;

        static bool PublicAuthoringRead(ISymbol member) => member.DeclaredAccessibility == Accessibility.Public &&
            (member is IPropertySymbol property ? !property.IsIndexer && property.GetMethod?.DeclaredAccessibility == Accessibility.Public :
                member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary &&
                !method.IsGenericMethod && method.Parameters.Length == 0 && !method.ReturnsVoid);

        static void EmitAuthoringFields(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            if (context.Compilation.GetTypeByMetadataName(AuthoringNamespace + "IGraphAuthoringFieldSource") == null)
                return;
            void Error(ISymbol symbol, string reason) => context.ReportDiagnostic(Diagnostic.Create(
                InvalidValueDeclaration, symbol.Locations.FirstOrDefault(), Identity(symbol), reason));
            if (type.ContainingType != null || type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct ||
                type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Error(type, "作者字段必须声明在顶层 partial class 或 partial struct 的公开实例成员上");
                return;
            }
            bool valid = true;
            foreach (AttributeData declaration in AuthoringAttributes(type, "GraphAuthoringField")
                .Concat(AuthoringAttributes(type, "GraphAuthoringChoices")))
            {
                string memberName = AuthoringMemberName(declaration);
                if (string.IsNullOrWhiteSpace(memberName) || type.GetMembers(memberName).Length != 1)
                {
                    Error(type, "类级 " + declaration.AttributeClass.Name + " 的 MemberName 必须唯一指向当前类声明的真实成员：" + memberName);
                    valid = false;
                }
            }
            if (!valid) return;
            AuthoringField[] fields = AuthoringFields(type);
            foreach (AuthoringField field in fields)
            {
                if (field.Type == null || field.Symbol.DeclaredAccessibility != Accessibility.Public || field.Symbol.IsStatic ||
                    field.Symbol is IPropertySymbol property && (property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public))
                {
                    Error(field.Symbol, "作者字段读取必须是公开实例字段或公开 getter");
                    valid = false;
                    continue;
                }
                string memberName = AuthoringMemberName(field.Metadata);
                if (memberName != null && memberName != field.Symbol.Name)
                {
                    Error(field.Symbol, "成员级作者字段的 MemberName 不能改绑其它成员：" + memberName);
                    valid = false;
                }
                if (fields.Count(other => other.MemberPath == field.MemberPath) != 1)
                {
                    Error(field.Symbol, "同一真实成员只能声明一个作者字段：" + field.MemberPath);
                    valid = false;
                }
                else if (fields.Count(other => other.Id == field.Id) != 1)
                {
                    Error(field.Symbol, "作者字段身份必须唯一：" + field.Id);
                    valid = false;
                }
                if (!ValidateAuthoringSubtypes(context.Compilation, type, field, Error))
                    valid = false;
            }
            for (INamedTypeSymbol ancestor = type.BaseType; ancestor != null; ancestor = ancestor.BaseType)
                foreach (AuthoringField inheritedField in AuthoringFields(ancestor))
                    if (fields.Any(field => field.Id == inheritedField.Id))
                    {
                        Error(type, "作者字段身份不能与继承字段重复：" + inheritedField.Id);
                        valid = false;
                    }
            string[] auxiliaryKinds = { "GraphAuthoringChoices", "GraphAuthoringDefault", "GraphAuthoringEvaluate",
                "GraphAuthoringResourceOwner", "GraphAuthoringBindResource", "GraphAuthoringResourceRule" };
            foreach (string kind in auxiliaryKinds)
            {
                var auxiliaryMembers = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
                foreach (ISymbol member in type.GetMembers())
                    foreach (AttributeData annotation in AuthoringAttributes(member, kind))
                    {
                        string key = (string)annotation.ConstructorArguments[0].Value;
                        if (!fields.Any(field => field.MemberPath == key))
                        {
                            Error(member, "作者字段辅助成员引用了未声明的字段成员：" + key);
                            valid = false;
                            continue;
                        }
                        string memberName = AuthoringMemberName(annotation);
                        if (memberName != null && memberName != member.Name)
                        {
                            Error(member, "成员级 " + kind + " 的 MemberName 不能改绑其它成员：" + memberName);
                            valid = false;
                        }
                        if (auxiliaryMembers.TryGetValue(key, out ISymbol previous))
                        {
                            Error(previous, "同一作者字段只能声明一个" + kind + "辅助成员：" + key);
                            valid = false;
                        }
                        else
                            auxiliaryMembers.Add(key, member);
                    }
            }
            if (!valid) return;
            foreach (AuthoringField field in fields)
            {
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field);
                if (defaults != null && (!PublicAuthoringRead(defaults) ||
                    !SymbolEqualityComparer.Default.Equals(AuthoringResultType(defaults), field.Type)))
                {
                    Error(defaults, "作者默认值必须是公开无参读取成员，并返回正式字段的准确类型");
                    valid = false;
                }
                ISymbol choices = AuthoringAuxiliary(type, "GraphAuthoringChoices", field);
                if (choices != null && (!PublicAuthoringRead(choices) ||
                    !(AuthoringResultType(choices) is INamedTypeSymbol choiceType) ||
                    !choiceType.AllInterfaces.Concat(new[] { choiceType }).Any(candidate => candidate.MetadataName == "IReadOnlyList`1" &&
                        candidate.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")))
                {
                    Error(choices, "作者候选必须是公开无参读取成员，并返回 IReadOnlyList<T>");
                    valid = false;
                }
                ISymbol owner = AuthoringAuxiliary(type, "GraphAuthoringResourceOwner", field);
                if (owner != null && (!(owner is IPropertySymbol ownerProperty) || owner.IsStatic ||
                    !PublicAuthoringRead(owner) || ownerProperty.SetMethod?.DeclaredAccessibility != Accessibility.Public ||
                    !ownerProperty.Type.IsReferenceType))
                {
                    Error(owner, "资源 owner 必须是公开可读写的平台资源引用属性");
                    valid = false;
                }
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field);
                if (field.Metadata.NamedArguments.Any(argument => argument.Key == "ResourceDomain" &&
                    argument.Value.Value is string domain && domain.Length != 0) && (owner == null || binder == null))
                {
                    Error(field.Symbol, "资源字段必须同时声明正式资源 owner 和绑定操作");
                    valid = false;
                }
                if (binder != null && (!(binder is IMethodSymbol method) || method.IsStatic || method.IsGenericMethod ||
                    method.DeclaredAccessibility != Accessibility.Public || method.Parameters.Length != 1 ||
                    !method.Parameters[0].Type.IsReferenceType ||
                    !SymbolEqualityComparer.Default.Equals(method.ReturnType, field.Type)))
                {
                    Error(binder, "资源绑定必须是公开实例方法，接收平台资源引用并返回正式字段的准确类型");
                    valid = false;
                }
                ISymbol resourceRule = AuthoringAuxiliary(type, "GraphAuthoringResourceRule", field);
                if (resourceRule != null)
                {
                    if (!(resourceRule is IMethodSymbol rule) || !rule.IsStatic || rule.IsGenericMethod ||
                        rule.DeclaredAccessibility != Accessibility.Public || rule.Parameters.Length != 2 ||
                        rule.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
                        !SymbolEqualityComparer.Default.Equals(rule.Parameters[0].Type, type) ||
                        Identity(rule.ReturnType) != AuthoringNamespace + "GraphAuthoringResourceAttribute")
                    {
                        Error(resourceRule, "资源规则必须是公开静态方法，按值接收当前作者类型和已解析声明，并返回 GraphAuthoringResourceAttribute");
                        valid = false;
                    }
                    AttributeData annotation = AuthoringAttributes(resourceRule, "GraphAuthoringResourceRule").Single();
                    string contextMember = (string)annotation.ConstructorArguments[1].Value;
                    AuthoringField contextField = fields.SingleOrDefault(candidate => candidate.MemberPath == contextMember);
                    if (contextField == null || contextField.Type.SpecialType != SpecialType.System_String ||
                        !HasAttribute(contextField.Symbol, AuthoringNamespace + "GraphAuthoringResourceAttribute"))
                    {
                        Error(resourceRule, "资源规则上下文必须指向当前类型已声明静态资源领域的字符串字段成员：" + contextMember);
                        valid = false;
                    }
                    if (HasAttribute(field.Symbol, AuthoringNamespace + "GraphAuthoringResourceAttribute"))
                    {
                        Error(field.Symbol, "同一资源字段不能同时声明静态资源领域和资源规则");
                        valid = false;
                    }
                }
            }
            if (!valid) return;
            var operations = new List<Tuple<IMethodSymbol, AuthoringArgument[]>>();
            var configured = new HashSet<string>(StringComparer.Ordinal);
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>()
                .Where(method => HasAttribute(method, AuthoringNamespace + "GraphAuthoringConfigureAttribute")))
            {
                if (method.IsStatic || method.DeclaredAccessibility != Accessibility.Public ||
                    !method.ReturnsVoid && method.MethodKind != MethodKind.Constructor ||
                    method.IsGenericMethod || method.Parameters.Any(parameter => parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref))
                {
                    Error(method, "作者配置必须是公开实例 void 方法或构造函数，参数只能按值或 in 传递");
                    valid = false;
                    continue;
                }
                AuthoringArgument[] arguments = method.Parameters.Select(parameter => ResolveAuthoringArgument(
                    context.Compilation, parameter, fields, new HashSet<string>(StringComparer.Ordinal))).ToArray();
                if (arguments.Any(argument => argument == null))
                {
                    Error(method, "配置参数必须唯一匹配正式字段身份，或由字段完整构造公开参数类型");
                    valid = false;
                    continue;
                }
                foreach (AuthoringField field in arguments.SelectMany(argument => argument.Fields))
                    if (field.ReadOnly || !configured.Add(field.Id))
                    {
                        Error(method, "可写字段必须只由一个正式配置操作持有：" + field.Id);
                        valid = false;
                    }
                operations.Add(Tuple.Create(method, arguments));
            }
            AuthoringField[] assigned = fields.Where(field => !field.ReadOnly && !configured.Contains(field.Id) && AuthoringWritable(field)).ToArray();
            foreach (AuthoringField field in fields.Where(field => !field.ReadOnly && !configured.Contains(field.Id) && !AuthoringWritable(field)))
            {
                Error(field.Symbol, "只读 getter 的可写作者字段必须属于标注 GraphAuthoringConfigure 的正式配置操作");
                valid = false;
            }
            foreach (AuthoringField field in fields)
                foreach (KeyValuePair<string, TypedConstant> argument in field.Metadata.NamedArguments)
                    if (AuthoringFieldReference(argument) && !fields.Any(candidate => candidate.MemberPath == (string)argument.Value.Value))
                    {
                        Error(field.Symbol, argument.Key + " 引用了未声明的字段成员：" + argument.Value.Value);
                        valid = false;
                    }
            if (!valid) return;

            INamedTypeSymbol inherited = type.BaseType;
            while (inherited != null && !AuthoringTypeHasFields(inherited)) inherited = inherited.BaseType;
            var code = new StringBuilder("#if UNITY_EDITOR || BTSMTL_AUTHORING\n");
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, type, AuthoringRuntime + "IGraphAuthoringFieldSource");
            code.Append("public ").Append(inherited != null ? "new " : "").Append("static ")
                .Append(AuthoringRuntime).AppendLine("GraphAuthoringTypeContract AuthoringContract { get; } = new(");
            code.Append("new ").Append(AuthoringRuntime).AppendLine("GraphAuthoringMember[] {");
            foreach (AuthoringField field in fields)
            {
                string read = "((" + TypeName(type) + ")target)." + field.Access;
                string evaluate = AuthoringCandidate(field);
                ISymbol evaluator = AuthoringAuxiliary(type, "GraphAuthoringEvaluate", field);
                if (evaluator is IMethodSymbol evaluatorMethod)
                {
                    if (!evaluatorMethod.IsStatic || evaluatorMethod.DeclaredAccessibility != Accessibility.Public ||
                        !SymbolEqualityComparer.Default.Equals(evaluatorMethod.ReturnType, field.Type) ||
                        evaluatorMethod.Parameters.Any(parameter => !fields.Any(candidate => candidate.Id == parameter.Name &&
                            SymbolEqualityComparer.Default.Equals(candidate.Type, parameter.Type))))
                    {
                        Error(evaluator, "候选读取操作必须是公开静态方法，返回字段类型且参数对应正式字段身份及类型");
                        return;
                    }
                    evaluate = TypeName(type) + "." + Escape(evaluator.Name) + "(" + string.Join(", ",
                        evaluatorMethod.Parameters.Select(parameter => AuthoringCandidate(fields.Single(candidate => candidate.Id == parameter.Name)))) + ")";
                }
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field);
                string defaultValue = defaults == null ? read : AuthoringAuxiliaryRead(type, defaults, "target");
                ISymbol choices = AuthoringAuxiliary(type, "GraphAuthoringChoices", field);
                string choice = choices == null ? "null" : "new global::System.Func<object, " + TypeName(
                    choices is IPropertySymbol choiceProperty ? choiceProperty.Type : ((IMethodSymbol)choices).ReturnType) + ">(" +
                    "target => " + AuthoringAuxiliaryRead(type, choices, "target") + ")";
                ISymbol owner = AuthoringAuxiliary(type, "GraphAuthoringResourceOwner", field);
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field);
                code.Append("new ").Append(AuthoringRuntime).Append("GraphAuthoringMember(")
                    .Append(AuthoringFieldMetadata(field, fields)).Append(", typeof(").Append(TypeName(field.Type)).Append("), ")
                    .Append(AuthoringCollectionElement(field.Type) is ITypeSymbol element ? "typeof(" + TypeName(element) + "), " : "null, ")
                    .Append("new string[] { ").Append(string.Join(", ",
                        AuthoringSerializedMemberPath(context.Compilation, type, field).Select(Quote))).Append(" }, ")
                    .Append(AuthoringSubtypeDescriptors(field)).Append(", ")
                    .Append("target => ").Append(read).Append(", new global::System.Func<object, ").Append(TypeName(field.Type))
                    .Append(">(target => ").Append(read).Append("), new global::System.Func<object, ").Append(TypeName(field.Type))
                    .Append(">(candidate => ").Append(evaluate).Append("), candidate => ").Append(evaluate)
                    .Append(", ").Append(field.ReadOnly ? "null" : "(candidate, value) => ((AuthoringCandidate)candidate).Set_" +
                        field.Name + "(" + AuthoringConvert(field, "value") + ")")
                    .Append(", candidate => ").Append(field.ReadOnly ? "false" : "((AuthoringCandidate)candidate).Changed_" + field.Name)
                    .Append(", value => ").Append(AuthoringExternalValue(field, "value"))
                    .Append(", target => ").Append(defaultValue).Append(", ").Append(choice).Append(", ")
                    .Append(owner == null ? "null" : "target => " + AuthoringAuxiliaryRead(type, owner, "target")).Append(", ")
                    .Append(owner == null ? "null" : "(target, asset) => ((" + TypeName(type) + ")target)." + Escape(owner.Name) +
                        " = (" + TypeName(((IPropertySymbol)owner).Type) + ")asset").Append(", ")
                    .Append(binder == null ? "null" : "(target, asset) => ((" + TypeName(type) + ")target)." + Escape(binder.Name) + "((" + TypeName(((IMethodSymbol)binder).Parameters[0].Type) + ")asset)")
                    .Append(", ").Append(binder == null ? "null" : "(candidate, asset) => ((AuthoringCandidate)candidate).SelectResource_" + field.Name + "((" + TypeName(((IMethodSymbol)binder).Parameters[0].Type) + ")asset)")
                    .Append(", ").Append(binder == null ? "null" : "candidate => ((AuthoringCandidate)candidate).ResourceChanged_" + field.Name)
                    .Append(", ").Append(binder == null ? "null" : "candidate => ((AuthoringCandidate)candidate).Resource_" + field.Name);
                foreach (AttributeData annotation in field.Symbol.GetAttributes().Where(attribute =>
                    HasAttribute(attribute.AttributeClass, AuthoringNamespace + "GraphAuthoringAnnotationAttribute")))
                    code.Append(", ").Append(AuthoringAttributeExpression(annotation));
                if (AuthoringAuxiliary(type, "GraphAuthoringResourceRule", field) is IMethodSymbol resourceRule)
                {
                    AttributeData annotation = AuthoringAttributes(resourceRule, "GraphAuthoringResourceRule").Single();
                    AuthoringField contextField = fields.Single(candidate => candidate.MemberPath ==
                        (string)annotation.ConstructorArguments[1].Value);
                    code.Append(", new ").Append(AuthoringRuntime).Append("GraphAuthoringResourceRuleAttribute(")
                        .Append(Quote(contextField.Id)).Append(", typeof(").Append(TypeName(resourceRule.Parameters[1].Type))
                        .Append("), (owner, declaration) => ").Append(TypeName(type)).Append(".").Append(Escape(resourceRule.Name))
                        .Append("((").Append(TypeName(type)).Append(")owner, (").Append(TypeName(resourceRule.Parameters[1].Type))
                        .Append(")declaration))");
                }
                code.AppendLine("),");
            }
            code.Append("}, new ").Append(AuthoringRuntime).AppendLine("GraphAuthoringConfiguration[] {");
            foreach (Tuple<IMethodSymbol, AuthoringArgument[]> operation in operations)
                EmitAuthoringConfiguration(code, operation.Item1.MethodKind == MethodKind.Constructor ? ".ctor" : operation.Item1.Name,
                    operation.Item2);
            if (assigned.Length != 0)
            {
                AuthoringArgument[] arguments = assigned.Select(field => new AuthoringArgument
                {
                    Type = field.Type, Field = field,
                    ParameterName = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1)
                }).ToArray();
                EmitAuthoringConfiguration(code, "ConfigureAuthoringFields", arguments);
            }
            code.Append("}, (target, complete) => new AuthoringCandidate((").Append(TypeName(type)).Append(")target, complete)")
                .Append(inherited != null ? ", " + TypeName(inherited) + ".AuthoringContract" : "").AppendLine(");");
            code.Append(AuthoringRuntime).Append("GraphAuthoringTypeContract ").Append(AuthoringRuntime)
                .AppendLine("IGraphAuthoringFieldSource.AuthoringFields => AuthoringContract;");
            code.Append("public ").Append(inherited != null ? "new " : "").AppendLine("static class FieldIds {");
            foreach (AuthoringField field in fields)
                code.Append("public const string ").Append(Escape(field.Name)).Append(" = ").Append(Quote(field.Id)).AppendLine(";");
            code.AppendLine("}");
            if (assigned.Length != 0)
            {
                code.Append("public void ConfigureAuthoringFields(").Append(string.Join(", ", assigned.Select(field =>
                    TypeName(field.Type) + " " + Parameter(field.Name)))).AppendLine(") {");
                foreach (AuthoringField field in assigned)
                    code.Append("this.").Append(field.Access).Append(" = ").Append(Parameter(field.Name)).AppendLine(";");
                code.AppendLine("}");
            }
            EmitAuthoringCandidate(code, type, fields, operations, assigned, inherited);
            code.AppendLine("} }\n#endif");
            context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Authoring.g.cs",
                SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static string AuthoringCandidate(AuthoringField field) => "((AuthoringCandidate)candidate)." + Escape(field.Name);

        static void EmitAuthoringCandidate(StringBuilder code, INamedTypeSymbol type, AuthoringField[] fields,
            List<Tuple<IMethodSymbol, AuthoringArgument[]>> operations, AuthoringField[] assigned, INamedTypeSymbol inherited)
        {
            code.Append("public ").Append(inherited != null ? "new " : "").Append("class AuthoringCandidate : ")
                .Append(inherited != null ? TypeName(inherited) + ".AuthoringCandidate" : AuthoringRuntime + "GraphAuthoringCandidate")
                .AppendLine(" {");
            foreach (AuthoringField field in fields)
            {
                code.Append("public ").Append(TypeName(field.Type)).Append(' ').Append(Escape(field.Name)).AppendLine(";");
                if (field.ReadOnly) continue;
                code.Append("public bool Changed_").Append(field.Name).AppendLine(";");
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field);
                if (binder != null)
                {
                    code.Append("public bool ResourceChanged_").Append(field.Name).AppendLine(";");
                    string resourceType = TypeName(((IMethodSymbol)binder).Parameters[0].Type);
                    code.Append("public ").Append(resourceType).Append(" Resource_").Append(field.Name).AppendLine(";");
                    code.Append("public void SelectResource_").Append(field.Name).Append('(').Append(resourceType).AppendLine(" asset) {")
                        .Append("Resource_").Append(field.Name).AppendLine(" = asset;")
                        .Append("ResourceChanged_").Append(field.Name).AppendLine(" = true;")
                        .Append("Changed_").Append(field.Name).AppendLine(" = true; }");
                }
                code.Append("public void Set_").Append(field.Name).Append('(').Append(TypeName(field.Type)).AppendLine(" value) {")
                    .Append(Escape(field.Name)).AppendLine(" = value;")
                    .Append("Changed_").Append(field.Name).AppendLine(" = true;");
                if (binder != null)
                    code.Append("ResourceChanged_").Append(field.Name).AppendLine(" = false;");
                code.AppendLine("}");
            }
            code.Append("public AuthoringCandidate(").Append(TypeName(type)).Append(" target, bool complete)")
                .Append(inherited != null ? " : base(target, complete)" : "").AppendLine(" {");
            foreach (AuthoringField field in fields)
            {
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field);
                string read = "target." + field.Access;
                code.Append(Escape(field.Name)).Append(" = ")
                    .Append(defaults == null ? read : "complete ? " + AuthoringAuxiliaryRead(type, defaults, "target") + " : " + read)
                    .AppendLine(";");
                if (!field.ReadOnly)
                    code.Append("Changed_").Append(field.Name).AppendLine(" = complete;");
            }
            code.AppendLine("}");
            code.Append("public override bool HasChanges => ").Append(string.Join(" || ",
                fields.Where(field => !field.ReadOnly).Select(field => "Changed_" + field.Name)
                    .Concat(inherited != null ? new[] { "base.HasChanges" } : new[] { "false" }))).AppendLine(";");
            AuthoringField[] resources = fields.Where(field => AuthoringAuxiliary(type, "GraphAuthoringBindResource", field) != null).ToArray();
            if (resources.Length != 0)
            {
                code.AppendLine("public override void ResolveResources(object target) {");
                if (inherited != null) code.AppendLine("base.ResolveResources(target);");
                foreach (AuthoringField field in resources)
                    code.Append("if (ResourceChanged_").Append(field.Name).Append(") Set_").Append(field.Name)
                        .Append("(((").Append(TypeName(type)).Append(")target).")
                        .Append(Escape(AuthoringAuxiliary(type, "GraphAuthoringBindResource", field).Name))
                        .Append("(Resource_").Append(field.Name).AppendLine("));");
                code.AppendLine("}");
            }
            code.AppendLine("public override object Apply(object target) {")
                .AppendLine("var candidate = this;");
            if (inherited != null) code.AppendLine("target = base.Apply(target);");
            if (type.IsValueType)
                code.Append("var value = (").Append(TypeName(type)).AppendLine(")target;");
            foreach (Tuple<IMethodSymbol, AuthoringArgument[]> operation in operations)
            {
                code.Append("if (").Append(string.Join(" || ", operation.Item2.SelectMany(argument => argument.Fields)
                    .Select(field => "Changed_" + field.Name))).Append(") ");
                if (operation.Item1.MethodKind == MethodKind.Constructor)
                    code.Append("return new ").Append(TypeName(type));
                else
                    code.Append(type.IsValueType ? "value." : "((" + TypeName(type) + ")target).")
                        .Append(Escape(operation.Item1.Name));
                code.Append('(').Append(string.Join(", ", operation.Item2.Select(argument => argument.Expression))).AppendLine(");");
            }
            if (assigned.Length != 0)
                code.Append("if (").Append(string.Join(" || ", assigned.Select(field => "Changed_" + field.Name)))
                    .Append(") ").Append(type.IsValueType ? "value" : "((" + TypeName(type) + ")target)")
                    .Append(".ConfigureAuthoringFields(")
                    .Append(string.Join(", ", assigned.Select(AuthoringCandidate))).AppendLine(");");
            code.AppendLine(type.IsValueType ? "return value; } }" : "return target; } }");
        }

        // 带点的 VisibleWhenField 是进入字段值的成员路径，由对应领域读取，不换算。
        static bool AuthoringFieldReference(KeyValuePair<string, TypedConstant> argument) =>
            (argument.Key == "PickerOwnerField" || argument.Key == "VisibleWhenField") &&
            argument.Value.Value is string member && member.Length != 0 && member.IndexOf('.') < 0;

        static string AuthoringFieldMetadata(AuthoringField field, AuthoringField[] fields) => "new " + TypeName(field.Metadata.AttributeClass) +
            "(" + Quote(field.Id) + (field.Arguments.Any() ? ", " + string.Join(", ",
                field.Arguments.Select(AuthoringConstant)) : "") + ")" +
            (field.Metadata.NamedArguments.Length == 0 ? "" : " { " + string.Join(", ", field.Metadata.NamedArguments.Select(argument =>
                Escape(argument.Key) + " = " + (AuthoringFieldReference(argument)
                    ? Quote(fields.Single(candidate => candidate.MemberPath == (string)argument.Value.Value).Id)
                    : AuthoringConstant(argument.Value)))) + " }");

        static bool AuthoringFieldKind(AuthoringField field, string name)
        {
            TypedConstant kind = field.Arguments.First();
            return kind.Kind == TypedConstantKind.Enum && kind.Type.GetMembers(name).OfType<IFieldSymbol>()
                .Any(member => Equals(member.ConstantValue, kind.Value));
        }

        static bool AuthoringIdentityField(AuthoringField field) => AuthoringFieldKind(field, "IdentityReference");

        static ITypeSymbol AuthoringCollectionElement(ITypeSymbol type) => IsReadOnlyMemory(type) ? null : SequenceElement(type);

        static ITypeSymbol[] AuthoringSubtypes(AuthoringField field)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in field.Metadata.NamedArguments)
                if (argument.Key == "Subtypes")
                    return argument.Value.IsNull ? Array.Empty<ITypeSymbol>() :
                        argument.Value.Values.Select(value => (ITypeSymbol)value.Value).ToArray();
            return Array.Empty<ITypeSymbol>();
        }

        static bool ValidateAuthoringSubtypes(Compilation compilation, INamedTypeSymbol owner, AuthoringField field,
            Action<ISymbol, string> error)
        {
            ITypeSymbol[] subtypes = AuthoringSubtypes(field);
            if (!AuthoringFieldKind(field, "Polymorphic"))
            {
                if (subtypes.Length == 0) return true;
                error(field.Symbol, "只有多态作者字段可以声明 Subtypes");
                return false;
            }
            ITypeSymbol valueType = AuthoringCollectionElement(field.Type) ?? field.Type;
            if (valueType.TypeKind != TypeKind.Class && valueType.TypeKind != TypeKind.Interface || subtypes.Length == 0)
            {
                error(field.Symbol, "多态作者字段或有序集合元素必须使用类或接口类型，并声明非空的 Subtypes 清单");
                return false;
            }
            bool valid = true;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (ITypeSymbol subtype in subtypes)
            {
                if (!(subtype is INamedTypeSymbol type) || type.TypeKind != TypeKind.Class || type.IsAbstract ||
                    type.IsUnboundGenericType || !compilation.IsSymbolAccessibleWithin(type, owner) ||
                    !type.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 &&
                        compilation.IsSymbolAccessibleWithin(constructor, owner)))
                {
                    error(field.Symbol, "多态子类型必须是当前声明可访问、非抽象且具有无参构造函数的类");
                    valid = false;
                    continue;
                }
                Conversion conversion = ((CSharpCompilation)compilation).ClassifyConversion(type, valueType);
                if (!conversion.IsIdentity && !(conversion.IsImplicit && conversion.IsReference))
                {
                    error(field.Symbol, "多态子类型必须继承或实现字段或集合元素类型：" + Identity(type));
                    valid = false;
                }
                AttributeData declaration = Attribute(type, AuthoringNamespace + "GraphAuthoringSubtypeAttribute");
                if (declaration == null || string.IsNullOrWhiteSpace((string)declaration.ConstructorArguments[0].Value) ||
                    string.IsNullOrWhiteSpace((string)declaration.ConstructorArguments[1].Value))
                {
                    error(field.Symbol, "多态子类型必须通过 GraphAuthoringSubtype 声明稳定身份和用途说明：" + Identity(type));
                    valid = false;
                }
                else if (!identities.Add((string)declaration.ConstructorArguments[0].Value))
                {
                    error(field.Symbol, "同一字段的多态子类型身份必须唯一：" + declaration.ConstructorArguments[0].Value);
                    valid = false;
                }
            }
            return valid;
        }

        static string AuthoringSubtypeDescriptors(AuthoringField field)
        {
            ITypeSymbol[] subtypes = AuthoringSubtypes(field);
            string descriptor = AuthoringRuntime + "GraphAuthoringSubtypeDescriptor";
            if (subtypes.Length == 0) return "global::System.Array.Empty<" + descriptor + ">()";
            return "new " + descriptor + "[] { " + string.Join(", ", subtypes.Select(type =>
            {
                AttributeData declaration = Attribute(type, AuthoringNamespace + "GraphAuthoringSubtypeAttribute");
                return "new " + descriptor + "(" + string.Join(", ", declaration.ConstructorArguments.Select(AuthoringConstant)) +
                    ", typeof(" + TypeName(type) + "), () => new " + TypeName(type) + "())";
            })) + " }";
        }

        static string AuthoringConvert(AuthoringField field, string value)
        {
            ITypeSymbol type = field.Type;
            string target = TypeName(type);
            if (type.TypeKind == TypeKind.Enum)
                return value + " is " + target + " typed ? typed : (" + target + ")global::System.Enum.Parse(typeof(" + target + "), (string)" + value + ", false)";
            string conversion = type.SpecialType == SpecialType.System_Byte ? "ToByte" :
                type.SpecialType == SpecialType.System_SByte ? "ToSByte" :
                type.SpecialType == SpecialType.System_Int16 ? "ToInt16" :
                type.SpecialType == SpecialType.System_UInt16 ? "ToUInt16" :
                type.SpecialType == SpecialType.System_Int32 ? "ToInt32" :
                type.SpecialType == SpecialType.System_UInt32 ? "ToUInt32" :
                type.SpecialType == SpecialType.System_Int64 ? "ToInt64" :
                type.SpecialType == SpecialType.System_UInt64 ? "ToUInt64" :
                type.SpecialType == SpecialType.System_Single ? "ToSingle" :
                type.SpecialType == SpecialType.System_Double ? "ToDouble" : null;
            if (conversion != null)
                return "global::System.Convert." + conversion + "(" + value + ", global::System.Globalization.CultureInfo.InvariantCulture)";
            if (AuthoringIdentityField(field) && type.SpecialType != SpecialType.System_String &&
                AuthoringIdentityConstruction(type, "(string)" + value, new HashSet<string>()) is string identity)
                return value + " is " + target + " typed ? typed : " + identity;
            return "(" + target + ")" + value;
        }

        static string AuthoringIdentityConstruction(ITypeSymbol type, string text, HashSet<string> constructing)
        {
            if (type.SpecialType == SpecialType.System_String) return text;
            if (!(type is INamedTypeSymbol named) || !constructing.Add(Identity(type))) return null;
            var expressions = new List<string>();
            foreach (IMethodSymbol constructor in named.InstanceConstructors.Where(method =>
                method.DeclaredAccessibility == Accessibility.Public && method.Parameters.Length == 1))
            {
                string argument = AuthoringIdentityConstruction(constructor.Parameters[0].Type, text, constructing);
                if (argument != null) expressions.Add("new " + TypeName(type) + "(" + argument + ")");
            }
            constructing.Remove(Identity(type));
            return expressions.Count == 1 ? expressions[0] : null;
        }

        static string AuthoringExternalValue(AuthoringField field, string value)
        {
            string typed = "((" + TypeName(field.Type) + ")" + value + ")";
            if (AuthoringIdentityField(field) && field.Type.SpecialType != SpecialType.System_String)
            {
                string text = AuthoringIdentityRead(field.Type, typed, new HashSet<string>());
                if (text != null)
                    return field.Type.IsReferenceType ? value + " == null ? string.Empty : " + text : text;
            }
            if (field.Type is INamedTypeSymbol collection && collection.AllInterfaces.Concat(new[] { collection })
                .Any(candidate => candidate.MetadataName == "IReadOnlyList`1"))
                return "global::System.Linq.Enumerable.ToArray(" + typed + ")";
            return value;
        }

        static string AuthoringIdentityRead(ITypeSymbol type, string value, HashSet<string> visiting)
        {
            if (type.SpecialType == SpecialType.System_String) return value + " ?? string.Empty";
            if (!(type is INamedTypeSymbol named) || !visiting.Add(Identity(type))) return null;
            var reads = new List<string>();
            foreach (IPropertySymbol member in named.GetMembers().OfType<IPropertySymbol>().Where(property =>
                property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic && !property.IsIndexer &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public))
            {
                string read = AuthoringIdentityRead(member.Type, value + "." + Escape(member.Name), visiting);
                if (read != null) reads.Add(read);
            }
            visiting.Remove(Identity(type));
            return reads.Count == 1 ? reads[0] : null;
        }

        static string AuthoringAuxiliaryRead(INamedTypeSymbol type, ISymbol symbol, string target) =>
            (symbol.IsStatic ? TypeName(type) : "((" + TypeName(type) + ")" + target + ")") + "." +
            Escape(symbol.Name) + (symbol is IMethodSymbol ? "()" : "");

        static void EmitAuthoringConfiguration(StringBuilder code, string method, AuthoringArgument[] arguments)
        {
            code.Append("new ").Append(AuthoringRuntime).Append("GraphAuthoringConfiguration(").Append(Quote(method))
                .Append(", new ").Append(AuthoringRuntime).Append("GraphAuthoringArgument[] { ")
                .Append(string.Join(", ", arguments.Select(argument => argument.Description))).Append(" }, new string[] { ")
                .Append(string.Join(", ", arguments.SelectMany(argument => argument.Fields).Select(field => Quote(field.Id))))
                .AppendLine(" }),");
        }

        static AuthoringArgument ResolveAuthoringArgument(Compilation compilation, IParameterSymbol parameter,
            AuthoringField[] fields, HashSet<string> constructing)
        {
            AuthoringField direct = fields.SingleOrDefault(field => field.Id == parameter.Name || Parameter(field.Name) == Escape(parameter.Name));
            if (direct != null)
            {
                var leaf = new AuthoringArgument { Type = direct.Type, Field = direct, ParameterName = parameter.Name };
                if (((CSharpCompilation)compilation).ClassifyConversion(direct.Type, parameter.Type).IsImplicit)
                    return leaf;
                if (parameter.Type is IArrayTypeSymbol array && direct.Type is INamedTypeSymbol collection &&
                    collection.AllInterfaces.Concat(new[] { collection }).Any(candidate =>
                        candidate.MetadataName == "IReadOnlyList`1" &&
                        SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], array.ElementType)))
                {
                    leaf.ToArray = true;
                    return leaf;
                }
                if (parameter.Type is INamedTypeSymbol wrapper && wrapper.InstanceConstructors.Any(constructor =>
                    constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, direct.Type)))
                {
                    IMethodSymbol constructor = wrapper.InstanceConstructors.Single(candidate => candidate.DeclaredAccessibility == Accessibility.Public &&
                        candidate.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, direct.Type));
                    leaf.ParameterName = constructor.Parameters[0].Name;
                    return new AuthoringArgument { Type = parameter.Type, ParameterName = parameter.Name, Children = new[] { leaf } };
                }
                return null;
            }
            if (!(parameter.Type is INamedTypeSymbol type) || !constructing.Add(Identity(type))) return null;
            var candidates = new List<AuthoringArgument>();
            foreach (IMethodSymbol constructor in type.InstanceConstructors.Where(constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length > 0))
            {
                AuthoringArgument[] arguments = constructor.Parameters.Select(child =>
                    ResolveAuthoringArgument(compilation, child, fields, constructing)).ToArray();
                if (arguments.All(argument => argument != null))
                    candidates.Add(new AuthoringArgument { Type = type, ParameterName = parameter.Name, Children = arguments });
            }
            constructing.Remove(Identity(type));
            return candidates.Count == 1 ? candidates[0] : null;
        }

        static string AuthoringAttributeExpression(AttributeData attribute) => "new " + TypeName(attribute.AttributeClass) +
            "(" + string.Join(", ", attribute.ConstructorArguments.Select(AuthoringConstant)) + ")" +
            (attribute.NamedArguments.Length == 0 ? "" : " { " + string.Join(", ", attribute.NamedArguments.Select(argument =>
                Escape(argument.Key) + " = " + AuthoringConstant(argument.Value))) + " }");

        static string AuthoringConstant(TypedConstant constant)
        {
            if (constant.IsNull) return "null";
            if (constant.Kind == TypedConstantKind.Type) return "typeof(" + TypeName((ITypeSymbol)constant.Value) + ")";
            if (constant.Kind == TypedConstantKind.Array) return "new " + TypeName(((IArrayTypeSymbol)constant.Type).ElementType) +
                "[] { " + string.Join(", ", constant.Values.Select(AuthoringConstant)) + " }";
            if (constant.Kind == TypedConstantKind.Enum) return "(" + TypeName(constant.Type) + ")" +
                Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
            if (constant.Value is string text) return Quote(text);
            if (constant.Value is bool boolean) return boolean ? "true" : "false";
            return Convert.ToString(constant.Value, CultureInfo.InvariantCulture) + (constant.Value is float ? "f" : constant.Value is double ? "d" : "");
        }
    }
}
