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
            public string Name => string.Join("_", Path);
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
                AttributeData metadata = Attribute(member, AuthoringFieldAttribute);
                string[] memberPath = path.Concat(new[] { member.Name }).ToArray();
                ITypeSymbol memberType = member is IPropertySymbol property ? property.Type :
                    member is IFieldSymbol declaredField ? declaredField.Type : null;
                if (metadata != null)
                    yield return new AuthoringField
                {
                    Symbol = member,
                    Type = memberType,
                    Metadata = metadata,
                    Id = prefix + (string)metadata.ConstructorArguments[0].Value,
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

        static AttributeData Attribute(ISymbol symbol, string name) => symbol.GetAttributes()
            .SingleOrDefault(attribute => Identity(attribute.AttributeClass) == name);

        static IEnumerable<AttributeData> AuthoringAttributes(ISymbol symbol, string suffix) =>
            symbol.GetAttributes().Where(attribute =>
                Identity(attribute.AttributeClass) == AuthoringNamespace + suffix + "Attribute");

        static ISymbol AuthoringAuxiliary(INamedTypeSymbol type, string suffix, string id) =>
            type.GetMembers().SingleOrDefault(member => AuthoringAttributes(member, suffix)
                .Any(attribute => (string)attribute.ConstructorArguments[0].Value == id));

        static bool AuthoringWritable(AuthoringField field) => !field.ReadOnly &&
            (field.Symbol is IFieldSymbol member ? !member.IsReadOnly :
                ((IPropertySymbol)field.Symbol).SetMethod?.DeclaredAccessibility == Accessibility.Public);

        static bool AuthoringTypeHasFields(INamedTypeSymbol type) => type != null &&
            type.GetMembers().Any(member => HasAttribute(member, AuthoringFieldAttribute) ||
                HasAttribute(member, AuthoringNamespace + "GraphAuthoringInlineAttribute"));

        static ITypeSymbol AuthoringResultType(ISymbol member) => member is IPropertySymbol property
            ? property.Type : ((IMethodSymbol)member).ReturnType;

        static bool PublicAuthoringRead(ISymbol member) => member.DeclaredAccessibility == Accessibility.Public &&
            (member is IPropertySymbol property ? !property.IsIndexer && property.GetMethod?.DeclaredAccessibility == Accessibility.Public :
                member is IMethodSymbol method && !method.IsGenericMethod && method.Parameters.Length == 0 && !method.ReturnsVoid);

        static void EmitAuthoringFields(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            if (context.Compilation.GetTypeByMetadataName(AuthoringNamespace + "IGraphAuthoringFieldSource") == null)
                return;
            AuthoringField[] fields = AuthoringFields(type);
            void Error(ISymbol symbol, string reason) => context.ReportDiagnostic(Diagnostic.Create(
                InvalidValueDeclaration, symbol.Locations.FirstOrDefault(), Identity(symbol), reason));
            if (type.ContainingType != null || type.TypeKind != TypeKind.Class ||
                type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Error(type, "作者字段必须声明在顶层 partial class 的公开实例成员上");
                return;
            }
            bool valid = true;
            foreach (AuthoringField field in fields)
            {
                if (field.Symbol.DeclaredAccessibility != Accessibility.Public || field.Symbol.IsStatic ||
                    field.Symbol is IPropertySymbol property && (property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public))
                {
                    Error(field.Symbol, "作者字段读取必须是公开实例字段或公开 getter");
                    valid = false;
                }
                if (fields.Count(other => other.Id == field.Id) != 1)
                {
                    Error(field.Symbol, "作者字段身份必须唯一：" + field.Id);
                    valid = false;
                }
            }
            for (INamedTypeSymbol ancestor = type.BaseType; ancestor != null; ancestor = ancestor.BaseType)
                foreach (AuthoringField inheritedField in AuthoringFields(ancestor))
                    if (fields.Any(field => field.Id == inheritedField.Id))
                    {
                        Error(type, "作者字段身份不能与继承字段重复：" + inheritedField.Id);
                        valid = false;
                    }
            string[] auxiliaryKinds = { "GraphAuthoringChoices", "GraphAuthoringDefault", "GraphAuthoringEvaluate",
                "GraphAuthoringResourceOwner", "GraphAuthoringBindResource" };
            foreach (string kind in auxiliaryKinds)
            {
                var auxiliaryMembers = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
                foreach (ISymbol member in type.GetMembers())
                    foreach (AttributeData annotation in AuthoringAttributes(member, kind))
                    {
                        string key = (string)annotation.ConstructorArguments[0].Value;
                        if (key == null)
                            continue;
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
            ITypeSymbol unityObject = context.Compilation.GetTypeByMetadataName("UnityEngine.Object");
            foreach (AuthoringField field in fields)
            {
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field.Id);
                if (defaults != null && (!PublicAuthoringRead(defaults) ||
                    !SymbolEqualityComparer.Default.Equals(AuthoringResultType(defaults), field.Type)))
                {
                    Error(defaults, "作者默认值必须是公开无参读取成员，并返回正式字段的准确类型");
                    valid = false;
                }
                ISymbol choices = AuthoringAuxiliary(type, "GraphAuthoringChoices", field.Id);
                if (choices != null && (!PublicAuthoringRead(choices) ||
                    !(AuthoringResultType(choices) is INamedTypeSymbol choiceType) ||
                    !choiceType.AllInterfaces.Concat(new[] { choiceType }).Any(candidate => candidate.MetadataName == "IReadOnlyList`1" &&
                        candidate.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")))
                {
                    Error(choices, "作者候选必须是公开无参读取成员，并返回 IReadOnlyList<T>");
                    valid = false;
                }
                ISymbol owner = AuthoringAuxiliary(type, "GraphAuthoringResourceOwner", field.Id);
                if (owner != null && (!(owner is IPropertySymbol ownerProperty) || owner.IsStatic ||
                    !PublicAuthoringRead(owner) || ownerProperty.SetMethod?.DeclaredAccessibility != Accessibility.Public ||
                    !((CSharpCompilation)context.Compilation).ClassifyConversion(ownerProperty.Type, unityObject).IsImplicit))
                {
                    Error(owner, "资源 owner 必须是公开可读写的 Unity 资产属性");
                    valid = false;
                }
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field.Id);
                if (field.Metadata.NamedArguments.Any(argument => argument.Key == "ResourceDomain" &&
                    argument.Value.Value is string domain && domain.Length != 0) && (owner == null || binder == null))
                {
                    Error(field.Symbol, "资源字段必须同时声明正式资源 owner 和绑定操作");
                    valid = false;
                }
                if (binder != null && (!(binder is IMethodSymbol method) || method.IsStatic || method.IsGenericMethod ||
                    method.DeclaredAccessibility != Accessibility.Public || method.Parameters.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, unityObject) ||
                    !SymbolEqualityComparer.Default.Equals(method.ReturnType, field.Type)))
                {
                    Error(binder, "资源绑定必须是公开实例方法，接收 UnityEngine.Object 并返回正式字段的准确类型");
                    valid = false;
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
            foreach (ISymbol member in type.GetMembers())
                foreach (AttributeData annotation in member.GetAttributes().Where(attribute =>
                    Identity(attribute.AttributeClass).StartsWith(AuthoringNamespace, StringComparison.Ordinal) &&
                    attribute.AttributeClass.Name != "GraphAuthoringFieldAttribute" &&
                    attribute.AttributeClass.Name != "GraphAuthoringConfigureAttribute" &&
                    attribute.AttributeClass.Name != "GraphAuthoringInlineAttribute" &&
                    attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string))
                    if (!fields.Any(field => field.Id == (string)annotation.ConstructorArguments[0].Value))
                    {
                        Error(member, "作者字段辅助成员引用了未声明的字段身份：" + annotation.ConstructorArguments[0].Value);
                        valid = false;
                    }
            if (!valid) return;

            INamedTypeSymbol inherited = type.BaseType;
            while (inherited != null && !AuthoringTypeHasFields(inherited)) inherited = inherited.BaseType;
            var code = new StringBuilder("#if UNITY_EDITOR\n");
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, type, AuthoringRuntime + "IGraphAuthoringFieldSource");
            code.Append("public ").Append(inherited != null ? "new " : "").Append("static ")
                .Append(AuthoringRuntime).AppendLine("GraphAuthoringTypeContract AuthoringContract { get; } = new(");
            code.Append("new ").Append(AuthoringRuntime).AppendLine("GraphAuthoringMember[] {");
            foreach (AuthoringField field in fields)
            {
                string read = "((" + TypeName(type) + ")target)." + field.Access;
                string evaluate = AuthoringCandidate(field);
                ISymbol evaluator = AuthoringAuxiliary(type, "GraphAuthoringEvaluate", field.Id);
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
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field.Id);
                string defaultValue = defaults == null ? read : AuthoringAuxiliaryRead(type, defaults, "target");
                ISymbol choices = AuthoringAuxiliary(type, "GraphAuthoringChoices", field.Id);
                string choice = choices == null ? "null" : "new global::System.Func<object, " + TypeName(
                    choices is IPropertySymbol choiceProperty ? choiceProperty.Type : ((IMethodSymbol)choices).ReturnType) + ">(" +
                    "target => " + AuthoringAuxiliaryRead(type, choices, "target") + ")";
                ISymbol owner = AuthoringAuxiliary(type, "GraphAuthoringResourceOwner", field.Id);
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field.Id);
                code.Append("new ").Append(AuthoringRuntime).Append("GraphAuthoringMember(")
                    .Append(AuthoringFieldMetadata(field)).Append(", typeof(").Append(TypeName(field.Type)).Append("), ")
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
                    .Append(binder == null ? "null" : "(target, asset) => ((" + TypeName(type) + ")target)." + Escape(binder.Name) + "(asset)")
                    .Append(", ").Append(binder == null ? "null" : "(candidate, asset) => ((AuthoringCandidate)candidate).SelectResource_" + field.Name + "(asset)")
                    .Append(", ").Append(binder == null ? "null" : "candidate => ((AuthoringCandidate)candidate).ResourceChanged_" + field.Name)
                    .Append(", ").Append(binder == null ? "null" : "candidate => ((AuthoringCandidate)candidate).Resource_" + field.Name);
                foreach (AttributeData annotation in field.Symbol.GetAttributes().Where(attribute =>
                    HasAttribute(attribute.AttributeClass, AuthoringNamespace + "GraphAuthoringAnnotationAttribute")))
                    code.Append(", ").Append(AuthoringAttributeExpression(annotation));
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
                    ParameterName = Parameter(field.Name)
                }).ToArray();
                EmitAuthoringConfiguration(code, "ConfigureAuthoringFields", arguments);
            }
            code.Append("}, (target, complete) => new AuthoringCandidate((").Append(TypeName(type)).Append(")target, complete)")
                .Append(inherited != null ? ", " + TypeName(inherited) + ".AuthoringContract" : "").AppendLine(");");
            code.Append(AuthoringRuntime).Append("GraphAuthoringTypeContract ").Append(AuthoringRuntime)
                .AppendLine("IGraphAuthoringFieldSource.AuthoringFields => AuthoringContract;");
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
                ISymbol binder = AuthoringAuxiliary(type, "GraphAuthoringBindResource", field.Id);
                if (binder != null)
                {
                    code.Append("public bool ResourceChanged_").Append(field.Name).AppendLine(";");
                    code.Append("public global::UnityEngine.Object Resource_").Append(field.Name).AppendLine(";");
                    code.Append("public void SelectResource_").Append(field.Name).AppendLine("(global::UnityEngine.Object asset) {")
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
                ISymbol defaults = AuthoringAuxiliary(type, "GraphAuthoringDefault", field.Id);
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
            AuthoringField[] resources = fields.Where(field => AuthoringAuxiliary(type, "GraphAuthoringBindResource", field.Id) != null).ToArray();
            if (resources.Length != 0)
            {
                code.AppendLine("public override void ResolveResources(object target) {");
                if (inherited != null) code.AppendLine("base.ResolveResources(target);");
                foreach (AuthoringField field in resources)
                    code.Append("if (ResourceChanged_").Append(field.Name).Append(") Set_").Append(field.Name)
                        .Append("(((").Append(TypeName(type)).Append(")target).")
                        .Append(Escape(AuthoringAuxiliary(type, "GraphAuthoringBindResource", field.Id).Name))
                        .Append("(Resource_").Append(field.Name).AppendLine("));");
                code.AppendLine("}");
            }
            code.AppendLine("public override object Apply(object target) {")
                .AppendLine("var candidate = this;");
            if (inherited != null) code.AppendLine("target = base.Apply(target);");
            foreach (Tuple<IMethodSymbol, AuthoringArgument[]> operation in operations)
            {
                code.Append("if (").Append(string.Join(" || ", operation.Item2.SelectMany(argument => argument.Fields)
                    .Select(field => "Changed_" + field.Name))).Append(") ");
                if (operation.Item1.MethodKind == MethodKind.Constructor)
                    code.Append("return new ").Append(TypeName(type));
                else
                    code.Append("((").Append(TypeName(type)).Append(")target).").Append(Escape(operation.Item1.Name));
                code.Append('(').Append(string.Join(", ", operation.Item2.Select(argument => argument.Expression))).AppendLine(");");
            }
            if (assigned.Length != 0)
                code.Append("if (").Append(string.Join(" || ", assigned.Select(field => "Changed_" + field.Name)))
                    .Append(") ((").Append(TypeName(type)).Append(")target).ConfigureAuthoringFields(")
                    .Append(string.Join(", ", assigned.Select(AuthoringCandidate))).AppendLine(");");
            code.AppendLine("return target; } }");
        }

        static string AuthoringFieldMetadata(AuthoringField field) => "new " + TypeName(field.Metadata.AttributeClass) +
            "(" + Quote(field.Id) + (field.Metadata.ConstructorArguments.Length > 1 ? ", " + string.Join(", ",
                field.Metadata.ConstructorArguments.Skip(1).Select(AuthoringConstant)) : "") + ")" +
            (field.Metadata.NamedArguments.Length == 0 ? "" : " { " + string.Join(", ", field.Metadata.NamedArguments.Select(argument =>
                Escape(argument.Key) + " = " + AuthoringConstant(argument.Value))) + " }");

        static bool AuthoringIdentityField(AuthoringField field)
        {
            TypedConstant kind = field.Metadata.ConstructorArguments[1];
            return kind.Kind == TypedConstantKind.Enum && kind.Type.GetMembers("IdentityReference").OfType<IFieldSymbol>()
                .Any(member => Equals(member.ConstantValue, kind.Value));
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
            AuthoringField direct = fields.SingleOrDefault(field => field.Id == parameter.Name || Parameter(field.Name) == parameter.Name);
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
