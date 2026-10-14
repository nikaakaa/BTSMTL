using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    [Generator]
    public sealed partial class ValueGenerator : ISourceGenerator
    {
        const string UnionCaseAttribute = "ThirdPersonSimulation.ValueUnionCaseAttribute";

        const string PayloadAttribute = "ThirdPersonSimulation.ValuePayloadAttribute";

        const string UnionTemplateAttribute = "ThirdPersonSimulation.ValueUnionTemplateAttribute";

        const string UnionAttribute = "ThirdPersonSimulation.ValueUnionAttribute";

        const string ValueOperationsAttribute = "ThirdPersonSimulation.ValueOperationsAttribute";

        const string ValueResourceAttribute = "ThirdPersonSimulation.ValueResourceAttribute";

        const string ValueStoragePurposeAttribute = "ThirdPersonSimulation.ValueStorageAttribute";

        const string ValidationAttribute = "ThirdPersonSimulation.ValueValidationAttribute";

        const string TextAttribute = "ThirdPersonSimulation.ValueTextAttribute";

        const string BufferAttribute = "ThirdPersonSimulation.ValueBufferAttribute";

        const string OmittedUnionValueAttribute = "ThirdPersonSimulation.ValueUnionOmittedAttribute";

        const string BorrowedSequenceAttribute = "ThirdPersonSimulation.ValueBorrowedSequenceAttribute";

        const string BorrowedObjectAttribute = "ThirdPersonSimulation.ValueBorrowedObjectAttribute";

        const string TextRulesAttribute = "ThirdPersonSimulation.ValueTextRulesAttribute";

        const string ReferenceAccessAttribute = "ThirdPersonSimulation.ValueReferenceAccessAttribute";

        const string StorageOwnerAttribute = "ThirdPersonSimulation.ValueStorageOwnerAttribute";

        const string StorageAttribute = "ThirdPersonSimulation.ValueStorageMemberAttribute";

        const string Runtime = "global::ThirdPersonSimulation.";

        static readonly DiagnosticDescriptor InvalidValueDeclaration = new DiagnosticDescriptor(
            "VALUE001", "正式数据无法生成", "{0}: {1}", "ValueGeneration",
            DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new Receiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var receiver = (Receiver)context.SyntaxReceiver;
            var templates = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var unions = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueRecords = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueStorageRecords = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueUpdates = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var storageOwners = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var snapshotSources = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (BaseTypeDeclarationSyntax declaration in receiver.Types)
            {
                var type = (INamedTypeSymbol)context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration);
                if (HasAttribute(type, UnionTemplateAttribute))
                    templates[Identity(type)] = type;
                if (!HasAttribute(type, UnionTemplateAttribute) &&
                    (HasAttribute(type, UnionAttribute) || Cases(StorageMembers(type)).Length != 0))
                    unions[Identity(type)] = type;
                if (HasAttribute(type, StorageOwnerAttribute))
                    storageOwners[Identity(type)] = type;
                if (HasAttribute(type, SnapshotAttribute))
                    snapshotSources[Identity(type)] = type;
                if (HasAttribute(type, ValueOperationsAttribute) && !HasAttribute(type, UnionTemplateAttribute))
                    valueRecords[Identity(type)] = type;
                if (HasAttribute(type, ValueStoragePurposeAttribute))
                    valueStorageRecords[Identity(type)] = type;
                if (type.GetMembers().OfType<IMethodSymbol>().Any(method =>
                    HasAttribute(method, "ThirdPersonSimulation.ValueUpdateAttribute")))
                    valueUpdates[Identity(type)] = type;
            }
            foreach (INamedTypeSymbol template in templates.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                if (!ValidateTemplate(context, template)) return;
            foreach (INamedTypeSymbol template in templates.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                Member[] cases = Cases(UnionMembers(template));
                if (cases.Length == 0 || template.TypeKind != TypeKind.Struct && template.TypeKind != TypeKind.Enum)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, template.Locations.FirstOrDefault(),
                        Identity(template), "联合模板必须在正式 struct 成员或 enum 分支声明闭合载荷"));
                    continue;
                }
                if (template.TypeKind == TypeKind.Enum) continue;
                var code = new StringBuilder();
                code.Append("namespace ").Append(template.ContainingNamespace.ToDisplayString()).AppendLine(" {");
                EmitKind(code, template, cases);
                code.AppendLine("}");
                context.AddSource(Identity(template).Replace('.', '_').Replace('<', '_').Replace('>', '_') + ".Kind.g.cs",
                    SourceText.From(code.ToString(), Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in unions.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                Member[] cases = Cases(StorageMembers(type));
                if (cases.Length == 0 || HasAttribute(type, UnionAttribute))
                    continue;
                var code = new StringBuilder();
                code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
                EmitKind(code, type, cases);
                code.AppendLine("}");
                context.AddSource(Identity(type).Replace('.', '_') + ".Kind.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in unions.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Values.g.cs",
                    SourceText.From(EmitValueUnion(type), Encoding.UTF8));
            foreach (INamedTypeSymbol type in valueRecords.Values.Where(type => !unions.ContainsKey(Identity(type)))
                .OrderBy(type => Identity(type), StringComparer.Ordinal))
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Values.g.cs",
                    SourceText.From(EmitValueRecord(type), Encoding.UTF8));
            foreach (INamedTypeSymbol type in valueRecords.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
                EmitValueChanges(context, type);
            foreach (INamedTypeSymbol type in valueStorageRecords.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
            {
                string storage = EmitValueStorageRecord(context, type);
                if (storage == null) continue;
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Storage.g.cs",
                    SourceText.From(storage, Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in valueUpdates.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
                EmitValueUpdates(context, type);
            foreach (INamedTypeSymbol owner in storageOwners.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                EmitStorageOwner(context, owner);
            foreach (INamedTypeSymbol owner in snapshotSources.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
                EmitValueSnapshot(context, owner);
        }

        static void EmitStorageOwner(GeneratorExecutionContext context, INamedTypeSymbol owner)
        {
            IFieldSymbol[] fields = owner.GetMembers().OfType<IFieldSymbol>()
                .Where(field => HasAttribute(field, StorageAttribute)).ToArray();
            string reason = null;
            if (owner.ContainingType != null || fields.Length != 1 || fields[0].IsStatic ||
                !(fields[0].Type is INamedTypeSymbol data))
                reason = "状态 owner 必须由唯一真实字段持有正式数据，设施不进入该数据记录";
            else if (owner.DeclaringSyntaxReferences.Any(syntax =>
                !(syntax.GetSyntax() is TypeDeclarationSyntax declaration) || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                reason = "状态 owner 必须声明 partial，访问直接进入原类型";
            else if (StorageMembers((INamedTypeSymbol)fields[0].Type).Any(member =>
                owner.GetMembers(member.Symbol.Name).Length != 0))
                reason = "状态成员由唯一正式数据生成访问，owner 不能重复声明同名成员";
            if (reason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, owner.Locations.FirstOrDefault(), Identity(owner), reason));
                return;
            }
            IFieldSymbol storage = fields[0];
            Member[] members = StorageMembers((INamedTypeSymbol)storage.Type);
            bool ownsResources = storage.Type.GetAttributes().Any(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute &&
                attribute.NamedArguments.Any(argument => argument.Key == "OwnsMemberResources" && (bool)argument.Value.Value));
            var code = new StringBuilder();
            code.Append("namespace ").Append(owner.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            code.Append(owner.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(owner.IsReadOnly ? " readonly" : "").Append(" partial ")
                .Append(owner.TypeKind == TypeKind.Struct ? "struct " : "class ").Append(Escape(owner.Name));
            if (owner.Arity != 0)
                code.Append('<').Append(string.Join(", ", owner.TypeParameters.Select(parameter => Escape(parameter.Name)))).Append('>');
            foreach (ITypeParameterSymbol parameter in owner.TypeParameters)
            {
                var constraints = new List<string>();
                if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
                else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
                else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
                constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
                if (parameter.HasConstructorConstraint) constraints.Add("new()");
                if (constraints.Count != 0)
                    code.Append(" where ").Append(Escape(parameter.Name)).Append(" : ").Append(string.Join(", ", constraints));
            }
            code.AppendLine(" {");
            foreach (Member member in members)
            {
                string target = Escape(storage.Name) + "." + Escape(member.Symbol.Name);
                if (HasAttribute(member.Symbol, ReferenceAccessAttribute))
                {
                    code.Append("public ref readonly ").Append(TypeName(member.Type)).Append(" ").Append(Escape(member.Symbol.Name))
                        .Append(" => ref ").Append(target).AppendLine(";");
                    code.Append("public void Set").Append(member.Symbol.Name).Append("(in ").Append(TypeName(member.Type)).AppendLine(" value) {");
                    if (ownsResources)
                    {
                        HoldValueResources(code, "Retain", member.Type, "value");
                        HoldValueResources(code, "Release", member.Type, target);
                    }
                    code.Append(target).AppendLine(" = value; }");
                    continue;
                }
                code.Append(member.Symbol.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ")
                    .Append(TypeName(member.Type)).Append(" ").Append(Escape(member.Symbol.Name)).Append(" { get => ")
                    .Append(target).Append(";");
                if (!owner.IsReadOnly && !IsPreparedCollection(member.Type) && !(storage.IsReadOnly && storage.Type.IsValueType) &&
                    !(member.Symbol is IFieldSymbol field && field.IsReadOnly) &&
                    !(member.Symbol is IPropertySymbol property && property.SetMethod == null))
                {
                    if (ownsResources && HasValueResources(member.Type, new HashSet<string>()))
                    {
                        code.AppendLine(" set {");
                        HoldValueResources(code, "Retain", member.Type, "value");
                        HoldValueResources(code, "Release", member.Type, target);
                        code.Append(target).AppendLine(" = value; }");
                    }
                    else code.Append(" set => ").Append(target).Append(" = value;");
                }
                code.AppendLine(" }");
            }
            code.AppendLine("} }");
            context.AddSource(Identity(owner).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Storage.g.cs",
                SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static void EmitTypeDeclaration(StringBuilder code, INamedTypeSymbol type, string baseType = null)
        {
            code.Append(type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(type.IsReadOnly ? " readonly" : "").Append(" partial ")
                .Append(type.TypeKind == TypeKind.Struct ? "struct " : "class ").Append(Escape(type.Name));
            if (type.Arity != 0)
                code.Append('<').Append(string.Join(", ", type.TypeParameters.Select(p => Escape(p.Name)))).Append('>');
            if (baseType != null)
                code.Append(" : ").Append(baseType);
            foreach (ITypeParameterSymbol parameter in type.TypeParameters)
            {
                var constraints = new List<string>();
                if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
                else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
                else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
                constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
                if (parameter.HasConstructorConstraint) constraints.Add("new()");
                if (constraints.Count != 0)
                    code.Append(" where ").Append(Escape(parameter.Name)).Append(" : ").Append(string.Join(", ", constraints));
            }
            code.AppendLine(" {");
        }

        static string ValueEquals(ITypeSymbol type, string left, string right) =>
            type.TypeKind == TypeKind.Enum ? "(" + left + " == " + right + ")" :
            type.IsReferenceType ? "global::System.Object.Equals(" + left + ", " + right + ")" :
            left + ".Equals(" + right + ")";

        static Member[] ValueMembers(INamedTypeSymbol type)
        {
            AttributeData union = type.GetAttributes().SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute);
            Member[] members = StorageMembers(type);
            if (union == null) return members.Any(member => member.IsCase)
                ? members.Where(member => member.Symbol.Name != "Kind").ToArray() : members;
            Member[] generated = UnionMembers((INamedTypeSymbol)union.ConstructorArguments[0].Value)
                .Where(m => !HasAttribute(m.Type, OmittedUnionValueAttribute)).ToArray();
            return members.Where(m => m.Symbol.Name != "Kind" && !generated.Any(g => g.Symbol.Name == m.Symbol.Name))
                .Concat(generated).ToArray();
        }

        static bool HasValueResources(ITypeSymbol type, HashSet<string> visited)
        {
            if (HasAttribute(type, TextAttribute)) return true;
            if (type.GetAttributes().Any(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute &&
                attribute.NamedArguments.Any(argument => argument.Key == "Resources" && argument.Value.Value is false)))
                return false;
            if (type is ITypeParameterSymbol parameter)
                return parameter.ConstraintTypes.Any(constraint => Identity(constraint) == "ThirdPersonSimulation.IValueResources");
            if (type.TypeKind == TypeKind.Enum || BorrowedObjectStorage(type) != null || !visited.Add(Identity(type)))
                return false;
            if (MapTypes(type) is ITypeSymbol[] map)
                return map.Any(item => HasValueResources(item, new HashSet<string>(visited)));
            if (NullableElement(type) is ITypeSymbol nullable)
                return HasValueResources(nullable, visited);
            if (type.SpecialType != SpecialType.None) return false;
            if (BorrowedStorage(type) != null && BorrowedSemantic(type, "HoldStorage", true))
                return true;
            if (SequenceElement(type) is ITypeSymbol element)
                return HasValueResources(element, visited);
            return type is INamedTypeSymbol record && ValueMembers(record)
                .Any(member => HasAttribute(member.Symbol, ValueResourceAttribute) ||
                    HasValueResources(member.Type, new HashSet<string>(visited)));
        }

        static void HoldValueMember(StringBuilder code, string operation, Member member, string owner, int depth = 0)
        {
            string value = owner + "." + Escape(member.Symbol.Name);
            AttributeData resource = member.Symbol.GetAttributes().SingleOrDefault(attribute =>
                Identity(attribute.AttributeClass) == ValueResourceAttribute);
            if (resource == null)
                HoldValueResources(code, operation, member.Type, value, depth);
            else
            {
                bool elements = resource.NamedArguments.Any(argument => argument.Key == "Elements" && (bool)argument.Value.Value);
                if (elements)
                    HoldValueResourceElements(code, operation, member.Type, value, resource, depth);
                else
                    EmitDeclaredValueResource(code, operation, member.Type, value, resource);
            }
        }

        static void HoldValueResources(StringBuilder code, string operation, ITypeSymbol type, string value, int depth = 0)
        {
            if (!HasValueResources(type, new HashSet<string>())) return;
            if (HasAttribute(type, TextAttribute))
            {
                code.Append(value).Append('.').Append(operation).AppendLine("();");
                return;
            }
            if (type is ITypeParameterSymbol)
            {
                code.Append(value).Append('.').Append(operation).AppendLine("();");
                return;
            }
            if (type is INamedTypeSymbol owner && (HasAttribute(owner, ValueOperationsAttribute) ||
                HasAttribute(owner, UnionAttribute) || Cases(ValueMembers(owner)).Length != 0))
            {
                string method = owner.GetMembers(operation).OfType<IMethodSymbol>()
                    .Any(candidate => !candidate.IsStatic && candidate.Parameters.Length == 0)
                    ? operation : owner.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute)
                        .SelectMany(attribute => attribute.NamedArguments).Where(argument => argument.Key == operation + "Method")
                        .Select(argument => (string)argument.Value.Value).DefaultIfEmpty(operation).Single();
                code.Append(value).Append('.').Append(Escape(method)).AppendLine("();");
                return;
            }
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                string index = "__valueIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(value)
                    .Append(".Count; ").Append(index).AppendLine("++) {");
                HoldValueResources(code, operation, map[0], value + ".Keys[" + index + "]", depth + 1);
                HoldValueResources(code, operation, map[1], value + ".Values[" + index + "]", depth + 1);
                code.AppendLine("}");
                return;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
            {
                code.Append("if (").Append(value).AppendLine(".HasValue) {");
                HoldValueResources(code, operation, nullable, value + ".Value", depth);
                code.AppendLine("}");
                return;
            }
            if (BorrowedStorage(type) != null && BorrowedSemantic(type, "HoldStorage", true))
            {
                code.Append(value).Append('.').Append(operation).AppendLine("StateStorage();");
                return;
            }
            if (SequenceElement(type) is ITypeSymbol element)
            {
                string index = "__valueIndex" + depth;
                code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                    .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
                HoldValueResources(code, operation, element, SequenceValue(type, value, index), depth + 1);
                code.AppendLine("}");
                return;
            }
            var record = (INamedTypeSymbol)type;
            Member[] members = ValueMembers(record);
            foreach (Member member in members.Where(m => !m.IsCase))
                HoldValueMember(code, operation, member, value, depth);
            Member[] cases = Cases(members);
            if (cases.Length == 0) return;
            INamedTypeSymbol definition = (INamedTypeSymbol)record.GetAttributes()
                .SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute)?.ConstructorArguments[0].Value;
            INamedTypeSymbol kindOwner = definition ?? record;
            string kindType = kindOwner.TypeKind == TypeKind.Enum ? TypeName(kindOwner) :
                "global::" + kindOwner.ContainingNamespace.ToDisplayString() + "." + Escape(kindOwner.Name) + "Kind";
            code.Append("switch (").Append(value).AppendLine(".Kind) {");
            foreach (Member member in cases.Where(m => HasValueResources(m.Type, new HashSet<string>())))
            {
                code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name)).AppendLine(":");
                HoldValueResources(code, operation, member.Type, value + "." + Escape(member.Symbol.Name), depth);
                code.AppendLine("break;");
            }
            code.AppendLine("}");
        }

        static string EmitValueUnion(INamedTypeSymbol type)
        {
            AttributeData union = type.GetAttributes().SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute);
            INamedTypeSymbol definition = union == null ? null : (INamedTypeSymbol)union.ConstructorArguments[0].Value;
            Member[] members = StorageMembers(type);
            if (definition != null)
            {
                Member[] generated = UnionMembers(definition).Where(m => !HasAttribute(m.Type, OmittedUnionValueAttribute)).ToArray();
                foreach (Member member in generated)
                    member.Generated = true;
                members = members.Concat(generated).OrderBy(m => m.Symbol.Name, StringComparer.Ordinal).ToArray();
            }
            Member[] cases = Cases(members);
            Member[] common = members.Where(m => !m.IsCase).ToArray();
            INamedTypeSymbol kindOwner = definition ?? type;
            string kindType = kindOwner.TypeKind == TypeKind.Enum ? TypeName(kindOwner) :
                "global::" + kindOwner.ContainingNamespace.ToDisplayString() + "." + Escape(kindOwner.Name) + "Kind";
            IMethodSymbol[] validations = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => HasAttribute(m, ValidationAttribute)).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
            string fullType = TypeName(type);
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            code.AppendLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]");
            EmitTypeDeclaration(code, type);
            foreach (Member member in members.Where(m => m.Generated))
                code.Append("public ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name)).AppendLine(" { get; }");
            code.Append("public ").Append(kindType).AppendLine(" Kind { get; }");
            foreach (List<Member> branches in CaseGroups(cases))
            {
                code.Append("private ").Append(Escape(type.Name)).Append('(').Append(kindType).Append(" kind");
                foreach (Member member in common)
                    code.Append(", in ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.Append(", in ").Append(TypeName(branches[0].Type)).AppendLine(" value) {");
                if (type.TypeKind == TypeKind.Struct) code.AppendLine("this = default;");
                code.AppendLine("Kind = kind;");
                foreach (Member member in common)
                    code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                        .Append(Normalize(member, Parameter(member.Symbol.Name), "false")).AppendLine(";");
                if (branches.Count == 1)
                    code.Append("this.").Append(Escape(branches[0].Symbol.Name)).Append(" = ")
                        .Append(Normalize(branches[0], "value", "false")).AppendLine(";");
                else
                {
                    code.AppendLine("switch (kind) {");
                    foreach (Member member in branches)
                        code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                            .Append(Normalize(member, "value", "false")).AppendLine("; break;");
                    code.AppendLine("}");
                }
                foreach (IMethodSymbol validation in validations)
                    code.Append(Escape(validation.Name)).AppendLine("();");
                code.AppendLine("}");
            }
            foreach (Member branch in cases)
            {
                code.Append("public static ").Append(fullType).Append(" From").Append(branch.Symbol.Name).Append('(')
                    .Append(string.Join(", ", common.Select(m => TypeName(m.Type) + " " + Parameter(m.Symbol.Name))));
                if (common.Length != 0) code.Append(", ");
                code.Append(TypeName(branch.Type)).AppendLine(" value) {");
                code.Append("return new ").Append(fullType).Append('(').Append(kindType).Append('.').Append(Escape(branch.Symbol.Name));
                foreach (Member member in common)
                    code.Append(", ").Append(Parameter(member.Symbol.Name));
                code.AppendLine(", value); }");
            }
            if (type.TypeKind == TypeKind.Struct)
            {
                code.Append("public static ").Append(fullType).Append(" Default(").Append(kindType).Append(" kind");
                foreach (Member member in common)
                    code.Append(", ").Append(TypeName(member.Type)).Append(' ').Append(Parameter(member.Symbol.Name));
                code.AppendLine(") { switch (kind) {");
                foreach (Member member in cases)
                {
                    code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name)).Append(": return From")
                        .Append(member.Symbol.Name).Append('(');
                    foreach (Member value in common)
                        code.Append(Parameter(value.Symbol.Name)).Append(", ");
                    string defaultMember = CaseDefaultMember(member);
                    code.Append(defaultMember == null ? "default(" + TypeName(member.Type) + ")" :
                        TypeName(member.Type) + "." + Escape(defaultMember)).AppendLine(");");
                }
                code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(kind)); } }");
            }
            if (HasAttribute(type, ValueOperationsAttribute) ||
                definition != null && HasAttribute(definition, ValueOperationsAttribute))
                EmitValueOperations(code, type, cases, common, kindType);
            code.AppendLine("} }");
            return code.ToString();
        }

        static string EmitValueRecord(INamedTypeSymbol type)
        {
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = type.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners)
                EmitTypeDeclaration(code, owner);
            EmitTypeDeclaration(code, type);
            EmitValueOperations(code, type, Array.Empty<Member>(), StorageMembers(type), null);
            foreach (INamedTypeSymbol owner in owners)
                code.AppendLine("}");
            code.AppendLine("} }");
            return code.ToString();
        }

        static string EmitValueStorageRecord(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, type);
            AttributeData purpose = type.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == ValueStoragePurposeAttribute);
            bool construct = purpose.NamedArguments.Any(argument => argument.Key == "Construct" && (bool)argument.Value.Value);
            if (purpose.NamedArguments.Any(argument => argument.Key == "UseConstructor" && (bool)argument.Value.Value))
            {
                if (!EmitConstructorValueStorage(context, code, type, construct)) return null;
            }
            else
                EmitValueStorage(code, type, StorageMembers(type), construct);
            code.AppendLine("} }");
            return code.ToString();
        }

        static bool EmitConstructorValueStorage(GeneratorExecutionContext context, StringBuilder code,
            INamedTypeSymbol type, bool construct)
        {
            Member[] members = StorageMembers(type);
            var constructors = type.InstanceConstructors.Where(candidate => !candidate.IsImplicitlyDeclared)
                .Select(candidate => new { Constructor = candidate,
                    Inputs = ValueStorageConstructorInputs(context.Compilation, type, candidate, members) })
                .Where(candidate => candidate.Inputs != null).ToArray();
            if (construct || constructors.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(), Identity(type),
                    "UseConstructor 须唯一对应原构造函数与原存储成员，且不能同时声明 Construct"));
                return false;
            }
            IMethodSymbol constructor = constructors[0].Constructor;
            Member[] inputs = constructors[0].Inputs;
            code.Append("public void ReadValueStorage(");
            for (int index = 0; index < inputs.Length; index++)
            {
                if (index != 0) code.Append(", ");
                Member member = inputs[index];
                if (member.Payload) code.Append("[global::ThirdPersonSimulation.ValuePayload] ");
                code.Append("out ").Append(TypeName(constructor.Parameters[index].Type)).Append(' ')
                    .Append(Escape(member.Symbol.Name));
            }
            code.AppendLine(") {");
            for (int index = 0; index < inputs.Length; index++)
            {
                Member member = inputs[index];
                string value = "this." + Escape(member.Symbol.Name);
                code.Append(Escape(member.Symbol.Name)).Append(" = ")
                    .Append(ValueStorageSequenceToArray(member.Type, constructor.Parameters[index].Type) ?
                        "global::System.Linq.Enumerable.ToArray(" + value + ")" : value).AppendLine(";");
            }
            code.AppendLine("}");
            code.Append("public static ").Append(TypeName(type)).Append(" FromValueStorage(")
                .Append(string.Join(", ", inputs.Select((member, index) =>
                    "in " + TypeName(constructor.Parameters[index].Type) + " " + Escape(member.Symbol.Name))))
                .Append(") => new ").Append(TypeName(type)).Append('(')
                .Append(string.Join(", ", inputs.Select(member => Escape(member.Symbol.Name)))).AppendLine(");");
            return true;
        }

        static Member[] ValueStorageConstructorInputs(Compilation compilation, INamedTypeSymbol type,
            IMethodSymbol constructor, Member[] storage)
        {
            var inputs = new Member[constructor.Parameters.Length];
            for (int index = 0; index < inputs.Length; index++)
            {
                IParameterSymbol parameter = constructor.Parameters[index];
                Member[] matches = storage.Where(member => UpdateNameMatches(parameter.Name, member.Symbol.Name) &&
                    ValueStorageCanPass(compilation, member.Type, parameter.Type)).ToArray();
                if (matches.Length == 0)
                    matches = type.GetMembers().OfType<IPropertySymbol>().Where(property => !property.IsStatic &&
                        !property.IsIndexer && property.GetMethod != null &&
                        UpdateNameMatches(parameter.Name, property.Name) &&
                        ValueStorageCanPass(compilation, property.Type, parameter.Type))
                        .Select(property => new Member { Symbol = property, Type = property.Type,
                            Payload = HasAttribute(property, PayloadAttribute) }).ToArray();
                if (matches.Length != 1) return null;
                inputs[index] = matches[0];
            }
            return inputs;
        }

        static bool ValueStorageCanPass(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
        {
            if (SymbolEqualityComparer.Default.Equals(source, target) || ValueStorageSequenceToArray(source, target))
                return true;
            Conversion conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
            return conversion.IsImplicit && conversion.IsReference;
        }

        static bool ValueStorageSequenceToArray(ITypeSymbol source, ITypeSymbol target) =>
            target is IArrayTypeSymbol array && array.Rank == 1 && source is INamedTypeSymbol sequence &&
            sequence.AllInterfaces.Concat(new[] { sequence }).Any(contract =>
                Identity(contract.OriginalDefinition) == "System.Collections.Generic.IEnumerable<T>" &&
                SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], array.ElementType));

        static void EmitValueStorage(StringBuilder code, INamedTypeSymbol type, Member[] members, bool construct)
        {
            code.Append("public void ReadValueStorage(")
                .Append(string.Join(", ", members.Select(member =>
                    (member.Payload ? "[global::ThirdPersonSimulation.ValuePayload] " : "") +
                    "out " + TypeName(member.Type) + " " + Escape(member.Symbol.Name))))
                .AppendLine(") {");
            foreach (Member member in members)
                code.Append(Escape(member.Symbol.Name)).Append(" = this.").Append(Escape(member.Symbol.Name)).AppendLine(";");
            code.AppendLine("}");
            Member[] constructorMembers = construct
                ? members.OrderBy(member => member.Symbol.Locations[0].SourceSpan.Start).ToArray() : members;
            code.Append(construct ? "public " : "private ").Append(Escape(type.Name)).Append('(');
            if (!construct) code.Append("global::ThirdPersonSimulation.ValueConstruction construction");
            for (int index = 0; index < constructorMembers.Length; index++)
            {
                if (index != 0 || !construct) code.Append(", ");
                Member member = constructorMembers[index];
                code.Append(construct ? "" : "in ").Append(TypeName(member.Type)).Append(' ')
                    .Append(construct ? Parameter(member.Symbol.Name) : Escape(member.Symbol.Name));
            }
            code.AppendLine(") {");
            if (type.IsValueType) code.AppendLine("this = default;");
            foreach (Member member in members)
                code.Append("this.").Append(Escape(member.Symbol.Name)).Append(" = ")
                    .Append(Normalize(member, construct ? Parameter(member.Symbol.Name) : Escape(member.Symbol.Name),
                        construct ? "false" : "true")).AppendLine(";");
            foreach (IMethodSymbol validation in type.GetMembers().OfType<IMethodSymbol>()
                .Where(method => HasAttribute(method, ValidationAttribute)).OrderBy(method => method.Name, StringComparer.Ordinal))
                code.Append(Escape(validation.Name)).AppendLine("();");
            code.AppendLine("}");
            code.Append("public static ").Append(TypeName(type)).Append(" FromValueStorage(")
                .Append(string.Join(", ", members.Select(member => "in " + TypeName(member.Type) + " " + Escape(member.Symbol.Name))))
                .Append(") => new ").Append(TypeName(type)).Append('(');
            if (!construct) code.Append("default(global::ThirdPersonSimulation.ValueConstruction)");
            for (int index = 0; index < constructorMembers.Length; index++)
            {
                if (index != 0 || !construct) code.Append(", ");
                code.Append(Escape(constructorMembers[index].Symbol.Name));
            }
            code.AppendLine(");");
        }

        static bool UpdateNameMatches(string parameter, string member) => string.Equals(StorageName(parameter),
            StorageName(member), StringComparison.OrdinalIgnoreCase);

        static string StorageName(string name) => name.StartsWith("m_", StringComparison.Ordinal) ? name.Substring(2) :
            name.StartsWith("value_", StringComparison.Ordinal) ? name.Substring(6) : name;

        static void EmitValueUpdates(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            Member[] members = StorageMembers(type);
            IMethodSymbol constructor = type.InstanceConstructors.SingleOrDefault(candidate =>
                !candidate.IsImplicitlyDeclared && candidate.Parameters.Length != 0 &&
                candidate.Parameters.All(parameter => members.Any(member => UpdateNameMatches(parameter.Name, member.Symbol.Name))));
            if (constructor == null && !HasAttribute(type, ValueStoragePurposeAttribute))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations[0], Identity(type),
                    "更新必须由原始成员对应的正式构造函数创建"));
                return;
            }
            Member[] updateMembers = constructor == null ? members : constructor.Parameters.Select(parameter =>
                members.Single(member => UpdateNameMatches(parameter.Name, member.Symbol.Name))).ToArray();
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, type);
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(method =>
                HasAttribute(method, "ThirdPersonSimulation.ValueUpdateAttribute")))
            {
                IParameterSymbol unbound = method.Parameters.FirstOrDefault(parameter => !updateMembers.Any(member =>
                    UpdateNameMatches(parameter.Name, member.Symbol.Name)));
                if (unbound != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, unbound.Locations[0], Identity(unbound),
                        "更新参数没有对应的原始存储成员"));
                    return;
                }
                code.Append("public partial ").Append(TypeName(type)).Append(' ').Append(Escape(method.Name)).Append('(')
                    .Append(string.Join(", ", method.Parameters.Select(parameter => TypeName(parameter.Type) + " " + Escape(parameter.Name))))
                    .Append(constructor == null ? ") => FromValueStorage(" : ") => new " + TypeName(type) + "(");
                for (int index = 0; index < updateMembers.Length; index++)
                {
                    if (index != 0) code.Append(", ");
                    Member member = updateMembers[index];
                    IParameterSymbol parameter = method.Parameters.SingleOrDefault(parameter => UpdateNameMatches(parameter.Name, member.Symbol.Name));
                    code.Append(parameter == null ? "this." + Escape(member.Symbol.Name) : Escape(parameter.Name));
                }
                code.AppendLine(");");
            }
            code.AppendLine("} }");
            context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Updates.g.cs",
                SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static void EmitValueOperations(StringBuilder code, INamedTypeSymbol type,
            Member[] cases, Member[] common, string kindType)
        {
            string fullType = TypeName(type);
            AttributeData operations = type.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute);
            if (type.AllInterfaces.Any(i => i.OriginalDefinition.Name == "IEquatable" &&
                i.ContainingNamespace.ToDisplayString() == "System" &&
                SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)) &&
                !type.GetMembers("Equals").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type)))
            {
                code.Append("public bool Equals(").Append(fullType).AppendLine(" other) {");
                if (type.IsReferenceType) code.AppendLine("if (other == null) return false;");
                if (kindType != null) code.AppendLine("if (Kind != other.Kind) return false;");
                bool explicitEquality = operations != null && operations.NamedArguments.Any(argument =>
                    argument.Key == "ExplicitEquality" && argument.Value.Value is true);
                IEnumerable<Member> equalityMembers = common.Concat(type.GetMembers().OfType<IPropertySymbol>()
                    .Where(property => !IsAutoProperty(property) &&
                        HasAttribute(property, "ThirdPersonSimulation.ValueEqualityMemberAttribute"))
                    .OrderBy(Identity, StringComparer.Ordinal)
                    .Select(property => new Member { Symbol = property, Type = property.Type }));
                foreach (Member member in equalityMembers.Where(member =>
                    !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueEqualityIgnoreAttribute") &&
                    (!explicitEquality || HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueEqualityMemberAttribute"))))
                {
                    string left = "this." + Escape(member.Symbol.Name);
                    string right = "other." + Escape(member.Symbol.Name);
                    if (SequenceElement(member.Type) is ITypeSymbol element)
                    {
                        code.Append("if (").Append(SequenceCount(member.Type, left)).Append(" != ")
                            .Append(SequenceCount(member.Type, right)).AppendLine(") return false;");
                        code.Append("for (int __equalIndex = 0; __equalIndex < ")
                            .Append(SequenceCount(member.Type, left)).AppendLine("; __equalIndex++) {");
                        code.Append("if (!").Append(ValueEquals(element, SequenceValue(member.Type, left, "__equalIndex"),
                            SequenceValue(member.Type, right, "__equalIndex"))).AppendLine(") return false; }");
                    }
                    else
                        code.Append("if (!").Append(ValueEquals(member.Type, left, right)).AppendLine(") return false;");
                }
                if (kindType == null) code.AppendLine("return true; }");
                else
                {
                    code.AppendLine("switch (Kind) {");
                    foreach (Member member in cases)
                        code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": return ").Append(ValueEquals(member.Type, "this." + Escape(member.Symbol.Name),
                                "other." + Escape(member.Symbol.Name))).AppendLine(";");
                    code.Append("case (").Append(kindType).AppendLine(")0: return true;");
                    code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(Kind)); } }");
                }
            }
            if (operations != null && operations.NamedArguments.Any(argument => argument.Key == "Resources" && argument.Value.Value is false))
                return;
            if (operations != null && operations.NamedArguments.Any(argument => argument.Key == "ExplicitResources" && argument.Value.Value is true))
                common = common.Where(member => HasAttribute(member.Symbol, ValueResourceAttribute)).ToArray();
            foreach (string operation in new[] { "Retain", "Release" })
            {
                string method = operations == null ? operation : operations.NamedArguments
                    .Where(argument => argument.Key == operation + "Method").Select(argument => (string)argument.Value.Value)
                    .DefaultIfEmpty(operation).Single();
                IMethodSymbol declaration = type.GetMembers(method).OfType<IMethodSymbol>()
                    .SingleOrDefault(m => !m.IsStatic && m.Parameters.Length == 0);
                bool partialDefinition = declaration != null && declaration.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is MethodDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    syntax.Body == null && syntax.ExpressionBody == null);
                if (declaration != null && !partialDefinition)
                    continue;
                code.Append(declaration != null ? declaration.DeclaredAccessibility.ToString().ToLowerInvariant() + " " :
                    method == operation ? "public " : "private ")
                    .Append(declaration != null ? "partial " : "")
                    .Append(type.IsValueType && !type.IsReadOnly ? "readonly void " : "void ")
                    .Append(Escape(method)).AppendLine("() {");
                foreach (Member member in common)
                    HoldValueMember(code, operation, member, "this");
                Member[] resourceCases = cases.Where(m => HasValueResources(m.Type, new HashSet<string>())).ToArray();
                if (resourceCases.Length != 0) code.AppendLine("switch (Kind) {");
                foreach (Member member in resourceCases)
                {
                    code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name)).AppendLine(":");
                    HoldValueResources(code, operation, member.Type, "this." + Escape(member.Symbol.Name));
                    code.AppendLine("break;");
                }
                if (resourceCases.Length != 0) code.AppendLine("}");
                code.AppendLine("}");
            }
        }

        static string Normalize(Member member, string value, string encoded = "construction.IsEncoded")
        {
            if (HasAttribute(member.Symbol, TextRulesAttribute))
                return Runtime + "ValueTextNormalization.Apply(" + value + ", " +
                    (TextRule(member, "Required") ? "true" : "false") + ", " +
                    (TextRule(member, "Trim") ? "true" : "false") + ", " +
                    (TextRule(member, "NullToEmpty") ? "true" : "false") + ", " + encoded + ", " +
                    SymbolDisplay.FormatLiteral(member.Symbol.Name, true) + ")";
            if (CaseSemantic(member, "NormalizeZero"))
                return value + " == 0 ? 0 : " + value;
            if (CaseSemantic(member, "NormalizeNull"))
                return value + ".IsNull ? " + (CaseDefaultMember(member) is string defaultMember ?
                    TypeName(member.Type) + "." + Escape(defaultMember) : ValueDefault(member.Type)) + " : " + value;
            return value;
        }

        static bool TextRule(Member member, string name) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == TextRulesAttribute).SelectMany(a => a.NamedArguments)
            .Any(a => a.Key == name && a.Value.Value is true);

        static bool CaseSemantic(Member member, string name) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == UnionCaseAttribute).SelectMany(a => a.NamedArguments)
            .Any(a => a.Key == name && a.Value.Value is true);

        static string CaseDefaultMember(Member member) => member.Symbol.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == UnionCaseAttribute).SelectMany(a => a.NamedArguments)
            .Where(a => a.Key == "DefaultMember").Select(a => (string)a.Value.Value).SingleOrDefault();

        static string Primitive(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && type.TypeKind == TypeKind.Enum)
                return Primitive(named.EnumUnderlyingType);
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte: return "Byte";
                case SpecialType.System_SByte: return "SByte";
                case SpecialType.System_Boolean: return "Boolean";
                case SpecialType.System_Int16: return "Int16";
                case SpecialType.System_UInt16: return "UInt16";
                case SpecialType.System_Char: return "Char";
                case SpecialType.System_Int32: return "Int32";
                case SpecialType.System_UInt32: return "UInt32";
                case SpecialType.System_Int64: return "Int64";
                case SpecialType.System_UInt64: return "UInt64";
                case SpecialType.System_Single: return "SingleBits";
                case SpecialType.System_Double: return "DoubleBits";
                default: return null;
            }
        }

        static ITypeSymbol[] MapTypes(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.Collections.Generic.SortedList<TKey, TValue>"
                ? named.TypeArguments.ToArray() : null;

        static bool IsPreparedCollection(ITypeSymbol type) => BufferElement(type) != null || MapTypes(type) != null ||
            type is INamedTypeSymbol named && Identity(named.OriginalDefinition) == "System.Collections.Generic.List<T>";

        static ITypeSymbol NullableElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] : null;

        static ITypeSymbol BufferElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            HasAttribute(named.OriginalDefinition, BufferAttribute) ? named.AllInterfaces.Single(contract =>
                Identity(contract.OriginalDefinition) == "System.Collections.Generic.IReadOnlyList<T>").TypeArguments[0] : null;

        static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
                return array.ElementType;
            if (type is INamedTypeSymbol named)
            {
                ITypeSymbol bufferElement = BufferElement(type);
                if (bufferElement != null)
                    return bufferElement;
                if (HasAttribute(named.OriginalDefinition, BorrowedSequenceAttribute))
                    return ((INamedTypeSymbol)named.GetMembers("Span").OfType<IPropertySymbol>().Single().Type).TypeArguments[0];
                switch (Identity(named.OriginalDefinition))
                {
                    case "System.ReadOnlyMemory<T>":
                    case "System.Collections.Generic.List<T>":
                    case "System.Collections.Generic.IReadOnlyList<T>":
                    case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                        return named.TypeArguments[0];
                }
            }
            return null;
        }

        static INamedTypeSymbol BorrowedStorage(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named))
                return null;
            AttributeData shape = named.OriginalDefinition.GetAttributes()
                .SingleOrDefault(a => Identity(a.AttributeClass) == BorrowedSequenceAttribute);
            if (shape == null)
                return null;
            INamedTypeSymbol storage = (INamedTypeSymbol)shape.ConstructorArguments[0].Value;
            return storage.IsGenericType ? storage.OriginalDefinition.Construct(SequenceElement(type)) : storage;
        }

        static bool BorrowedSemantic(ITypeSymbol type, string name, bool defaultValue)
        {
            AttributeData shape = ((INamedTypeSymbol)type).OriginalDefinition.GetAttributes()
                .Single(a => Identity(a.AttributeClass) == BorrowedSequenceAttribute);
            return shape.NamedArguments.Where(a => a.Key == name).Select(a => (bool)a.Value.Value)
                .DefaultIfEmpty(defaultValue).Single();
        }

        static string SequenceCount(ITypeSymbol type, string value) =>
            value + (type is IArrayTypeSymbol || IsReadOnlyMemory(type) ? ".Length" : ".Count");

        static string SequenceValue(ITypeSymbol type, string value, string index) =>
            IsReadOnlyMemory(type) || BorrowedStorage(type) != null ? value + ".Span[" + index + "]" :
            BufferElement(type) == null ? value + "[" + index + "]" : value + ".Element(" + index + ")";

        static bool IsReadOnlyMemory(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.ReadOnlyMemory<T>";

        static string Identity(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        static string Escape(string name) => "@" + name;

        static string Parameter(string name) => Escape(char.ToLowerInvariant(name[0]) + name.Substring(1));

        static bool HasAttribute(ISymbol symbol, string attribute) => symbol.GetAttributes().Any(a => Identity(a.AttributeClass) == attribute);

        static INamedTypeSymbol BorrowedObjectStorage(ITypeSymbol type) => (INamedTypeSymbol)type.GetAttributes()
            .Where(a => Identity(a.AttributeClass) == BorrowedObjectAttribute)
            .Select(a => a.ConstructorArguments[0].Value).SingleOrDefault();

        sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<BaseTypeDeclarationSyntax> Types = new List<BaseTypeDeclarationSyntax>();
            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is BaseTypeDeclarationSyntax type && (type.AttributeLists.Count > 0 ||
                    type is TypeDeclarationSyntax declaration && declaration.Members.OfType<MethodDeclarationSyntax>()
                        .Any(method => method.AttributeLists.Count > 0)))
                    Types.Add(type);
            }
        }

        sealed class Member
        {
            public ISymbol Symbol;
            public ITypeSymbol Type;
            public bool IsCase;
            public bool Payload;
            public bool Generated;
        }

        static Member[] StorageMembers(INamedTypeSymbol type) => type.GetMembers()
            .Where(s => !s.IsStatic && !s.IsImplicitlyDeclared)
            .Where(s => s is IFieldSymbol field &&
                !HasAttribute(field, "System.Runtime.CompilerServices.CompilerGeneratedAttribute") ||
                s is IPropertySymbol property && IsAutoProperty(property))
            .OrderBy(Identity, StringComparer.Ordinal)
            .Select(s => new Member { Symbol = s, Type = s is IFieldSymbol f ? f.Type : ((IPropertySymbol)s).Type,
                IsCase = HasAttribute(s, UnionCaseAttribute), Payload = HasAttribute(s, PayloadAttribute) }).ToArray();

        static bool ValidateTemplate(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum) return true;
            var codes = new HashSet<int>();
            bool valid = true;
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(member => member.HasConstantValue))
            {
                AttributeData branch = field.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == UnionCaseAttribute);
                string reason = null;
                if (branch == null || branch.ConstructorArguments.Length != 1 ||
                    !(branch.ConstructorArguments[0].Value is ITypeSymbol))
                    reason = "每个正式 enum 分支必须在原成员声明唯一载荷类型";
                decimal code = Convert.ToDecimal(field.ConstantValue, CultureInfo.InvariantCulture);
                if (code < int.MinValue || code > int.MaxValue || !codes.Add((int)code))
                    reason = "正式联合分支必须使用唯一 Int32 判别值";
                if (reason == null) continue;
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, field.Locations.FirstOrDefault(), Identity(field), reason));
                valid = false;
            }
            return valid;
        }

        static Member[] UnionMembers(INamedTypeSymbol type) => type.TypeKind == TypeKind.Enum ?
            type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue && HasAttribute(field, UnionCaseAttribute))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => new Member
                {
                    Symbol = field,
                    Type = (ITypeSymbol)field.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == UnionCaseAttribute)
                        .ConstructorArguments.Single().Value,
                    IsCase = true
                }).ToArray() : StorageMembers(type);

        static IEnumerable<List<Member>> CaseGroups(IEnumerable<Member> cases)
        {
            var groups = new SortedDictionary<string, List<Member>>(StringComparer.Ordinal);
            foreach (Member member in cases)
            {
                string identity = Identity(member.Type);
                if (!groups.TryGetValue(identity, out List<Member> group))
                    groups.Add(identity, group = new List<Member>());
                group.Add(member);
            }
            return groups.Values;
        }

        static Member[] Cases(IEnumerable<Member> members) => members.Where(m => m.IsCase)
            .OrderBy(m => CaseOrder(m.Symbol.OriginalDefinition is IFieldSymbol field ? field.Type :
                ((IPropertySymbol)m.Symbol.OriginalDefinition).Type), StringComparer.Ordinal)
            .ThenBy(m => m.Symbol.Name, StringComparer.Ordinal).ToArray();

        static string CaseOrder(ITypeSymbol type) => Primitive(type) != null ? "0:" + Identity(type) :
            type is ITypeParameterSymbol parameter ? "1:" + parameter.Ordinal.ToString("D8", CultureInfo.InvariantCulture) :
            "2:" + Identity(type);

        static void EmitKind(StringBuilder code, INamedTypeSymbol type, Member[] cases)
        {
            code.Append(type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal")
                .Append(" enum ").Append(Escape(type.Name)).Append("Kind : ")
                .AppendLine(cases.Length <= byte.MaxValue ? "byte {" : "int {");
            for (int i = 0; i < cases.Length; i++)
                code.Append(Escape(cases[i].Symbol.Name)).Append(" = ").Append(i + 1).AppendLine(",");
            code.AppendLine("}");
        }

        static bool IsAutoProperty(IPropertySymbol property) => property.DeclaringSyntaxReferences.Length == 0
            ? property.GetMethod != null && HasAttribute(property.GetMethod, "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            : property.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is PropertyDeclarationSyntax declaration && declaration.AccessorList != null &&
                declaration.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));
    }
}
