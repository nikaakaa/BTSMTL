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
    [Generator]
    public sealed partial class ValueGenerator : ISourceGenerator
    {
        const string UnionCaseAttribute = "ThirdPersonSimulation.ValueUnionCaseAttribute";

        const string PayloadAttribute = "ThirdPersonSimulation.ValuePayloadAttribute";

        const string UnionTemplateAttribute = "ThirdPersonSimulation.ValueUnionTemplateAttribute";

        const string UnionAttribute = "ThirdPersonSimulation.ValueUnionAttribute";

        const string UnionAccessAttribute = "ThirdPersonSimulation.ValueUnionAccessAttribute";

        const string ValueOperationsAttribute = "ThirdPersonSimulation.ValueOperationsAttribute";

        const string ValueStoragePurposeAttribute = "ThirdPersonSimulation.ValueStorageAttribute";

        const string ValidationAttribute = "ThirdPersonSimulation.ValueValidationAttribute";

        const string TextRulesAttribute = "ThirdPersonSimulation.ValueTextRulesAttribute";

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
            var unionAccesses = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueRecords = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueStorageRecords = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var valueUpdates = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var stateLayouts = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var stateLayoutFactories = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            var valueHashes = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            foreach (BaseTypeDeclarationSyntax declaration in receiver.Types)
            {
                var type = (INamedTypeSymbol)context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration);
                if (HasAttribute(type, UnionTemplateAttribute))
                    templates[Identity(type)] = type;
                if (!HasAttribute(type, UnionTemplateAttribute) &&
                    (HasAttribute(type, UnionAttribute) || Cases(StorageMembers(type)).Length != 0))
                    unions[Identity(type)] = type;
                if (HasAttribute(type, UnionAccessAttribute))
                    unionAccesses[Identity(type)] = type;
                if (HasAttribute(type, StateLayoutAttribute))
                    stateLayouts[Identity(type)] = type;
                foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (HasAttribute(method, StateLayoutFactoryAttribute))
                        stateLayoutFactories[Identity(method)] = method;
                    if (HasAttribute(method, HashAttribute))
                        valueHashes[Identity(method)] = method;
                }
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
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Kind.g.cs",
                    SourceText.From(code.ToString(), Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in unions.Values.OrderBy(t => Identity(t), StringComparer.Ordinal))
            {
                if (!ValidateUnionLayout(type, GetValueUnionShape(type), out string reason) ||
                    !TryExplicitValueUnionLayout((CSharpCompilation)context.Compilation, type, out ValueUnionLayout layout, out reason))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(), Identity(type), reason));
                    continue;
                }
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Values.g.cs",
                    SourceText.From(EmitValueUnion(type, layout), Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in unionAccesses.Values.OrderBy(Identity, StringComparer.Ordinal))
                EmitValueUnionAccess(context, type);
            foreach (INamedTypeSymbol type in valueRecords.Values.Where(type => !unions.ContainsKey(Identity(type)))
                .OrderBy(type => Identity(type), StringComparer.Ordinal))
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Values.g.cs",
                    SourceText.From(EmitValueRecord(type), Encoding.UTF8));
            foreach (INamedTypeSymbol type in valueStorageRecords.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
            {
                string storage = EmitValueStorageRecord(context, type);
                if (storage == null) continue;
                context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Storage.g.cs",
                    SourceText.From(storage, Encoding.UTF8));
            }
            foreach (INamedTypeSymbol type in valueUpdates.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
                EmitValueUpdates(context, type);
            foreach (INamedTypeSymbol layout in stateLayouts.Values.OrderBy(type => Identity(type), StringComparer.Ordinal))
                EmitStateLayout(context, layout);
            if (stateLayoutFactories.Count != 0)
                EmitStateLayoutFactories(context, stateLayoutFactories.Values);
            if (valueHashes.Count != 0)
                EmitValueHashes(context, valueHashes.Values);
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
            type is ITypeParameterSymbol parameter && parameter.ConstraintTypes.Any(constraint =>
                constraint.SpecialType == SpecialType.System_Enum) ?
                "global::System.Collections.Generic.EqualityComparer<" + TypeName(type) + ">.Default.Equals(" + left + ", " + right + ")" :
            type.IsReferenceType ? "global::System.Object.Equals(" + left + ", " + right + ")" :
            left + ".Equals(" + right + ")";

        static string EmitValueUnion(INamedTypeSymbol type, ValueUnionLayout layout)
        {
            ValueUnionShape shape = GetValueUnionShape(type);
            INamedTypeSymbol definition = shape.Definition;
            Member[] members = shape.Members;
            Member[] cases = shape.Cases;
            Member[] common = shape.Common;
            string kindType = shape.KindType;
            IMethodSymbol[] validations = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => HasAttribute(m, ValidationAttribute)).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
            string fullType = TypeName(type);
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            if (layout != null)
                code.AppendLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit)]");
            else if (!HasAttribute(type, StructLayoutAttribute))
                code.AppendLine("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Auto)]");
            EmitTypeDeclaration(code, type);
            foreach (Member member in members.Where(m => m.Generated))
            {
                if (layout == null)
                    code.Append("public ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name)).AppendLine(" { get; }");
                else
                {
                    code.AppendLine("[global::System.Runtime.CompilerServices.CompilerGenerated]");
                    code.Append("[global::System.Runtime.InteropServices.FieldOffset(").Append(layout.PayloadOffset).AppendLine(")]");
                    code.Append("readonly ").Append(TypeName(member.Type)).Append(' ').Append(UnionPayloadField(member)).AppendLine(";");
                    code.Append("public ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name))
                        .Append(" => ").Append(UnionPayloadField(member)).AppendLine(";");
                }
            }
            if (shape.KindMember == null)
            {
                if (layout != null)
                    code.AppendLine("[field: global::System.Runtime.InteropServices.FieldOffset(0)]");
                code.Append("public ").Append(kindType).AppendLine(" Kind { get; }");
            }
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
                if (layout != null || branches.Count == 1)
                    code.Append("this.").Append(layout == null ? Escape(branches[0].Symbol.Name) : UnionPayloadField(branches[0])).Append(" = ")
                        .Append(layout == null ? Normalize(branches[0], "value", "false") : "value").AppendLine(";");
                else
                {
                    code.AppendLine("switch (kind) {");
                    foreach (Member member in branches)
                        code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": this.").Append(layout == null ? Escape(member.Symbol.Name) : UnionPayloadField(member)).Append(" = ")
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
                code.Append(", ").Append(layout == null ? "value" : Normalize(branch, "value", "false")).AppendLine("); }");
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
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = type.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners)
                EmitTypeDeclaration(code, owner);
            EmitTypeDeclaration(code, type);
            AttributeData purpose = type.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == ValueStoragePurposeAttribute);
            bool construct = purpose.NamedArguments.Any(argument => argument.Key == "Construct" && (bool)argument.Value.Value);
            bool useConstructor = purpose.NamedArguments.Any(argument => argument.Key == "UseConstructor" && (bool)argument.Value.Value);
            ValueUnionShape union = GetValueUnionShape(type);
            if (union != null)
            {
                if (construct || useConstructor)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(), Identity(type),
                        "联合存储由原 Kind 与 From 分支重建，不能同时声明 Construct 或 UseConstructor"));
                    return null;
                }
                EmitUnionValueStorage(code, type, union);
            }
            else if (useConstructor)
            {
                if (!EmitConstructorValueStorage(context, code, type, construct)) return null;
            }
            else
                EmitValueStorage(code, type, StorageMembers(type).Where(member =>
                    !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray(), construct);
            foreach (INamedTypeSymbol owner in owners)
                code.AppendLine("}");
            code.AppendLine("} }");
            return code.ToString();
        }

        static bool EmitConstructorValueStorage(GeneratorExecutionContext context, StringBuilder code,
            INamedTypeSymbol type, bool construct)
        {
            Member[] members = StorageMembers(type).Where(member =>
                !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray();
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
                var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
                string accessibility = string.Join(" ", declaration.Modifiers.Where(modifier =>
                    modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword) ||
                    modifier.IsKind(SyntaxKind.PrivateKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword)));
                code.Append(accessibility).Append(" partial ").Append(TypeName(type)).Append(' ').Append(Escape(method.Name)).Append('(')
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
            if (type.AllInterfaces.Any(i => i.OriginalDefinition.Name == "IEquatable" &&
                i.ContainingNamespace.ToDisplayString() == "System" &&
                SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type)) &&
                !type.GetMembers("Equals").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, type)))
            {
                code.Append("public bool Equals(").Append(fullType).AppendLine(" other) {");
                if (type.IsReferenceType) code.AppendLine("if (other == null) return false;");
                if (kindType != null) code.AppendLine("if (Kind != other.Kind) return false;");
                foreach (Member member in common)
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

        static string ValueDefault(ITypeSymbol type)
        {
            ISymbol member = type.GetMembers().SingleOrDefault(candidate => candidate.IsStatic &&
                HasAttribute(candidate, "ThirdPersonSimulation.ValueDefaultAttribute"));
            return member == null ? "default" : TypeName(type) + "." + Escape(member.Name);
        }

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

        static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
                return array.ElementType;
            if (type is INamedTypeSymbol named)
                switch (Identity(named.OriginalDefinition))
                {
                    case "System.ReadOnlyMemory<T>":
                    case "System.Collections.Generic.List<T>":
                    case "System.Collections.Generic.IReadOnlyList<T>":
                    case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                        return named.TypeArguments[0];
                }
            return null;
        }

        static string SequenceCount(ITypeSymbol type, string value) =>
            value + (type is IArrayTypeSymbol || IsReadOnlyMemory(type) ? ".Length" : ".Count");

        static string SequenceValue(ITypeSymbol type, string value, string index) =>
            IsReadOnlyMemory(type) ? value + ".Span[" + index + "]" : value + "[" + index + "]";

        static bool IsReadOnlyMemory(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.ReadOnlyMemory<T>";

        static string Identity(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        static string Escape(string name) => "@" + name;

        static string Parameter(string name) => Escape(char.ToLowerInvariant(name[0]) + name.Substring(1));

        static bool HasAttribute(ISymbol symbol, string attribute) => symbol.GetAttributes().Any(a => Identity(a.AttributeClass) == attribute);

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

        static Member[] StorageMembers(INamedTypeSymbol type)
        {
            ISymbol[] members = type.GetMembers().Where(symbol => !symbol.IsStatic && !symbol.IsImplicitlyDeclared).ToArray();
            return members.Where(symbol => symbol is IFieldSymbol field && !HasAttribute(field, "System.Runtime.CompilerServices.CompilerGeneratedAttribute") ||
                symbol is IPropertySymbol property && IsAutoProperty(property))
                .OrderBy(Identity, StringComparer.Ordinal)
                .Select(symbol => new Member { Symbol = symbol,
                    Type = symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)symbol).Type,
                    IsCase = HasAttribute(symbol, UnionCaseAttribute), Payload = HasAttribute(symbol, PayloadAttribute) }).ToArray();
        }

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
                }).ToArray() : StorageMembers(type).Where(member => member.Symbol.Name != "Kind").ToArray();

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
                .Append(" enum ").Append(Escape(ValueUnionKindName(type))).Append(" : ")
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
