using System;
using System.Collections.Generic;
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
        const string SnapshotAttribute = "ThirdPersonSimulation.ValueSnapshotAttribute";
        const string SnapshotSource = "ThirdPersonSimulation.IValueSnapshotSource";

        enum SnapshotKind { Value, Array, Sequence, Dictionary, SortedList, KeyedBuffer, Source }

        sealed class SnapshotShape
        {
            public SnapshotKind Kind;
            public ITypeSymbol Type;
            public SnapshotShape Element;
            public ITypeSymbol Key;
            public bool FixedKeys;
            public AttributeData Resource;

            public string StorageType => Kind == SnapshotKind.Source ? Runtime + "IValueSnapshotCopy" :
                Kind == SnapshotKind.Array ? Element.StorageType + "[]" :
                Kind == SnapshotKind.Sequence ? Element.Kind == SnapshotKind.Source ? Element.StorageType + "[]" :
                    "global::System.Collections.Generic.List<" + Element.StorageType + ">" :
                Kind == SnapshotKind.Dictionary ? "global::System.Collections.Generic.Dictionary<" + TypeName(Key) + ", " + Element.StorageType + ">" :
                TypeName(Type);
        }

        static void EmitValueSnapshot(GeneratorExecutionContext context, INamedTypeSymbol owner)
        {
            Member[] members = owner.GetMembers().Where(member => HasAttribute(member, SnapshotAttribute))
                .OrderBy(Identity, StringComparer.Ordinal)
                .Where(member => member is IFieldSymbol || member is IPropertySymbol)
                .Select(member => new Member { Symbol = member,
                    Type = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type }).ToArray();
            string reason = null;
            if (owner.TypeKind != TypeKind.Class || owner.IsStatic || owner.ContainingType != null ||
                owner.DeclaringSyntaxReferences.Any(reference =>
                    !(reference.GetSyntax() is TypeDeclarationSyntax declaration) ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                reason = "快照 owner 必须是命名空间内的 partial class";
            else if (members.Length == 0)
                reason = "快照 owner 必须在原状态成员标记 ValueSnapshot";
            var shapes = new List<SnapshotShape>();
            foreach (Member member in members)
            {
                if (reason != null) break;
                if (member.Symbol.IsStatic || member.Symbol is IPropertySymbol property &&
                    (property.IsIndexer || property.GetMethod == null))
                    reason = "快照成员必须是可读取的实例字段或属性";
                else if (!TrySnapshotShape(member.Type, SnapshotMemberOption(member, "FixedKeys"),
                    member.Symbol.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == ValueResourceAttribute),
                    out SnapshotShape shape, out reason))
                    reason = member.Symbol.Name + ": " + reason;
                else if (shape.Kind == SnapshotKind.Value &&
                    (member.Symbol is IFieldSymbol field && field.IsReadOnly ||
                     member.Symbol is IPropertySymbol valueProperty && valueProperty.SetMethod == null))
                    reason = member.Symbol.Name + ": 原值必须可赋值，固定容器和嵌套状态源可保持 readonly";
                else
                    shapes.Add(shape);
            }
            if (reason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, owner.Locations.FirstOrDefault(),
                    Identity(owner), reason));
                return;
            }

            var code = new StringBuilder();
            var equality = new SnapshotEquality();
            code.Append("namespace ").Append(owner.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, owner, "global::ThirdPersonSimulation.IValueSnapshotSource");
            code.AppendLine("sealed class __ValueSnapshot : global::ThirdPersonSimulation.IValueSnapshotCopy {");
            code.AppendLine("internal int References;");
            for (int index = 0; index < members.Length; index++)
                code.Append("internal ").Append(shapes[index].StorageType).Append(' ')
                    .Append(Escape(members[index].Symbol.Name)).AppendLine(";");
            code.Append("internal __ValueSnapshot(").Append(TypeName(owner)).AppendLine(" owner) {");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotInitialize(code, shapes[index], "owner." + Escape(members[index].Symbol.Name),
                    "this." + Escape(members[index].Symbol.Name), 0);
            code.AppendLine("}");
            code.AppendLine("public void Retain() { References++; }");
            code.AppendLine("public void Release() { if (--References == 0) ReleaseValues(); }");
            code.AppendLine("public void CopyFrom(global::ThirdPersonSimulation.IValueResources source) {");
            code.AppendLine("var state = (__ValueSnapshot)source;");
            code.AppendLine("if (References != 0) ReleaseValues();");
            code.AppendLine("References = 1;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotCopy(code, shapes[index], "state." + Escape(members[index].Symbol.Name),
                    "this." + Escape(members[index].Symbol.Name), true, 0, true);
            code.AppendLine("}");
            code.AppendLine("internal void ReleaseValues() {");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotResources(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                    true, 0);
            code.AppendLine("}");
            code.AppendLine("public override bool Equals(object other) {");
            code.AppendLine("if (!(other is __ValueSnapshot state)) return false;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotEquals(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                    "state." + Escape(members[index].Symbol.Name), equality, 0);
            code.AppendLine("return true; }");
            code.AppendLine("public override int GetHashCode() { unchecked { int hash = 17;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotHash(code, shapes[index], "this." + Escape(members[index].Symbol.Name), equality, 0);
            code.AppendLine("return hash; } }");
            equality.Emit(code);
            code.AppendLine("}");
            code.AppendLine("public global::ThirdPersonSimulation.IValueSnapshotCopy CreateValueSnapshot() {");
            code.AppendLine("var snapshot = new __ValueSnapshot(this);");
            code.AppendLine("CaptureValueSnapshot(snapshot);");
            code.AppendLine("return snapshot; }");
            code.AppendLine("public void CaptureValueSnapshot(global::ThirdPersonSimulation.IValueResources snapshot) {");
            code.AppendLine("var state = (__ValueSnapshot)snapshot;");
            code.AppendLine("if (state.References != 0) state.ReleaseValues();");
            code.AppendLine("state.References = 1;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotCopy(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                    "state." + Escape(members[index].Symbol.Name), true, 0);
            code.AppendLine("}");
            code.AppendLine("public void RestoreValueSnapshot(global::ThirdPersonSimulation.IValueResources snapshot) {");
            code.AppendLine("var state = (__ValueSnapshot)snapshot;");
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotResources(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                    false, 0);
            for (int index = 0; index < members.Length; index++)
                EmitSnapshotCopy(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                    "state." + Escape(members[index].Symbol.Name), false, 0);
            code.AppendLine("}");
            foreach (string method in members.Select(SnapshotRestoreMethod).Where(method => method != null)
                .Distinct(StringComparer.Ordinal).OrderBy(method => method, StringComparer.Ordinal))
            {
                IMethodSymbol declaration = owner.GetMembers(method).OfType<IMethodSymbol>().SingleOrDefault(candidate =>
                    candidate.Parameters.Length == 1 && Identity(candidate.Parameters[0].Type) == "ThirdPersonSimulation.IValueResources" &&
                    candidate.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is MethodDeclarationSyntax syntax &&
                        syntax.Modifiers.Any(SyntaxKind.PartialKeyword) && syntax.Body == null && syntax.ExpressionBody == null));
                if (declaration == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, owner.Locations.FirstOrDefault(),
                        Identity(owner), method + ": 分类恢复入口必须声明接收 IValueResources 的 partial void 方法"));
                    return;
                }
                string parameter = Escape(declaration.Parameters[0].Name);
                code.Append(declaration.DeclaredAccessibility.ToString().ToLowerInvariant()).Append(" partial void ")
                    .Append(Escape(method)).Append("(global::ThirdPersonSimulation.IValueResources ")
                    .Append(parameter).AppendLine(") {");
                code.Append("var __state = (__ValueSnapshot)").Append(parameter).AppendLine(";");
                for (int index = 0; index < members.Length; index++)
                    if (SnapshotRestoreMethod(members[index]) == method)
                        EmitSnapshotResources(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                            false, 0);
                for (int index = 0; index < members.Length; index++)
                    if (SnapshotRestoreMethod(members[index]) == method)
                        EmitSnapshotCopy(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                            "__state." + Escape(members[index].Symbol.Name), false, 0);
                code.AppendLine("}");
            }
            foreach (string purpose in new[] { "CopyMethod", "ClearMethod" })
            {
                string method = owner.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == SnapshotAttribute)
                    .SelectMany(attribute => attribute.NamedArguments).Where(argument => argument.Key == purpose)
                    .Select(argument => (string)argument.Value.Value).SingleOrDefault();
                if (method == null) continue;
                bool copy = purpose == "CopyMethod";
                IMethodSymbol declaration = owner.GetMembers(method).OfType<IMethodSymbol>().SingleOrDefault(candidate =>
                    !candidate.IsStatic && candidate.ReturnsVoid && candidate.Parameters.Length == (copy ? 1 : 0) &&
                    (!copy || SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, owner)));
                if (declaration != null && !declaration.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is MethodDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    syntax.Body == null && syntax.ExpressionBody == null))
                    continue;
                if (declaration == null || shapes.Any(shape => !SnapshotOwnerOperationSupported(shape, purpose)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, owner.Locations.FirstOrDefault(),
                        Identity(owner), method + ": 原值操作须声明匹配的 partial void；嵌套 owner 必须在原类型声明同一用途的方法"));
                    return;
                }
                code.Append(declaration.DeclaredAccessibility.ToString().ToLowerInvariant()).Append(" partial void ")
                    .Append(Escape(method)).Append('(');
                string parameter = copy ? Escape(declaration.Parameters[0].Name) : null;
                if (copy) code.Append(TypeName(owner)).Append(' ').Append(parameter);
                code.AppendLine(") {");
                if (copy)
                    for (int index = 0; index < members.Length; index++)
                        EmitSnapshotResources(code, shapes[index], parameter + "." + Escape(members[index].Symbol.Name),
                            false, 0, "Retain");
                for (int index = 0; index < members.Length; index++)
                    EmitSnapshotResources(code, shapes[index], "this." + Escape(members[index].Symbol.Name),
                        false, 0);
                for (int index = 0; index < members.Length; index++)
                    if (copy)
                        EmitSnapshotCopy(code, shapes[index], parameter + "." + Escape(members[index].Symbol.Name),
                            "this." + Escape(members[index].Symbol.Name), true, 0, false, true, false);
                    else
                        EmitSnapshotClear(code, shapes[index], "this." + Escape(members[index].Symbol.Name), 0);
                code.AppendLine("}");
            }
            code.AppendLine("} }");
            context.AddSource(Identity(owner).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') +
                ".Snapshot.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static string SnapshotRestoreMethod(Member member) => member.Symbol.GetAttributes()
            .Where(attribute => Identity(attribute.AttributeClass) == SnapshotAttribute).SelectMany(attribute => attribute.NamedArguments)
            .Where(argument => argument.Key == "RestoreMethod").Select(argument => (string)argument.Value.Value).SingleOrDefault();

        static bool SnapshotMemberOption(Member member, string name) => member.Symbol.GetAttributes()
            .Where(attribute => Identity(attribute.AttributeClass) == SnapshotAttribute).SelectMany(attribute => attribute.NamedArguments)
            .Any(argument => argument.Key == name && argument.Value.Value is true);

        static bool ResourceElements(AttributeData resource) => resource != null && resource.NamedArguments
            .Any(argument => argument.Key == "Elements" && argument.Value.Value is true);

        static bool TrySnapshotShape(ITypeSymbol type, bool fixedKeys, AttributeData resource,
            out SnapshotShape shape, out string reason)
        {
            reason = null;
            shape = new SnapshotShape { Type = type };
            if (HasAttribute(type, SnapshotAttribute) || type is INamedTypeSymbol source &&
                (Identity(source) == SnapshotSource || source.AllInterfaces.Any(contract => Identity(contract) == SnapshotSource)))
            {
                shape.Kind = SnapshotKind.Source;
                if (type.IsValueType) reason = "嵌套状态源必须为引用类型，不能在快照接口调用中装箱";
            }
            else if (type is IArrayTypeSymbol array)
            {
                shape.Kind = SnapshotKind.Array;
                if (array.Rank != 1)
                    reason = "快照数组必须是固定形状的一维或锯齿数组";
                else if (TrySnapshotShape(array.ElementType, fixedKeys, ResourceElements(resource) ? resource : null,
                    out SnapshotShape element, out reason))
                    shape.Element = element;
            }
            else if (type is INamedTypeSymbol dictionary &&
                Identity(dictionary.OriginalDefinition) == "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                shape.Kind = SnapshotKind.Dictionary;
                shape.Key = dictionary.TypeArguments[0];
                shape.FixedKeys = fixedKeys;
                if (!SnapshotValueIsCopyable(shape.Key, new HashSet<string>()))
                    reason = "预分配字典的键必须是原值";
                else if (TrySnapshotShape(dictionary.TypeArguments[1], fixedKeys, ResourceElements(resource) ? resource : null,
                    out SnapshotShape element, out reason))
                {
                    shape.Element = element;
                    if (element.Kind != SnapshotKind.Value && !fixedKeys)
                        reason = "含可变嵌套值的字典必须在原成员声明 FixedKeys，并在初始化准备完整键集合";
                }
            }
            else if (MapTypes(type) is ITypeSymbol[] map)
            {
                shape.Kind = SnapshotKind.SortedList;
                shape.Key = map[0];
                if (!SnapshotValueIsCopyable(shape.Key, new HashSet<string>()))
                    reason = "预分配有序集合的键必须是原值";
                else if (TrySnapshotShape(map[1], false, ResourceElements(resource) ? resource : null,
                    out SnapshotShape element, out reason))
                {
                    shape.Element = element;
                    if (element.Kind != SnapshotKind.Value)
                        reason = "预分配有序集合的值必须是原值，不能动态增加可变状态源";
                }
            }
            else if (PreparedSnapshotKeyedTypes(type) is ITypeSymbol[] keyed)
            {
                shape.Kind = SnapshotKind.KeyedBuffer;
                shape.Key = keyed[0];
                if (!SnapshotValueIsCopyable(shape.Key, new HashSet<string>()))
                    reason = "预分配键值缓冲的键必须是原值";
                else if (TrySnapshotShape(keyed[1], false, ResourceElements(resource) ? resource : null,
                    out SnapshotShape element, out reason))
                {
                    shape.Element = element;
                    if (element.Kind != SnapshotKind.Value)
                        reason = "预分配键值缓冲不能动态增加可变嵌套值；须先准备固定存储形状";
                    else if (SnapshotHasResources(element, false))
                    {
                        if (!ResourceElements(resource))
                            reason = "键值缓冲元素含资源，但原 owner 未用 ValueResource(Elements = true) 声明元素拥有关系；借用缓冲不能自动归还 live 资源";
                        else if (!SnapshotResourceCanCall(element.Type, resource))
                            reason = "键值缓冲元素必须提供原 ValueResource 声明的正式资源操作";
                    }
                }
            }
            else if (PreparedSnapshotElement(type) is ITypeSymbol sequenceElement)
            {
                shape.Kind = SnapshotKind.Sequence;
                if (TrySnapshotShape(sequenceElement, fixedKeys, ResourceElements(resource) ? resource : null,
                    out SnapshotShape element, out reason))
                {
                    if (element.Kind != SnapshotKind.Value && element.Kind != SnapshotKind.Source)
                        reason = "预分配集合元素必须是原值或固定 roster 中的嵌套状态源";
                    else
                        shape.Element = element;
                }
            }
            else
            {
                shape.Resource = resource;
                if (!SnapshotValueIsCopyable(type, new HashSet<string>(), resource))
                    reason = "成员缺少独立状态源或可复制原值语义；终端引用必须由原 ValueResource 声明正式持有方法";
            }
            return reason == null;
        }

        static ITypeSymbol[] PreparedSnapshotKeyedTypes(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named) || !named.IsReferenceType) return null;
            IMethodSymbol key = named.GetMembers("GetKey").OfType<IMethodSymbol>().SingleOrDefault(method =>
                SnapshotPublicInstance(method) && method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && !method.ReturnsVoid);
            IMethodSymbol value = named.GetMembers("GetValue").OfType<IMethodSymbol>().SingleOrDefault(method =>
                SnapshotPublicInstance(method) && method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && !method.ReturnsVoid);
            if (key == null || value == null || !SnapshotIntProperty(named, "Count") || !SnapshotIntProperty(named, "Capacity") ||
                !named.GetMembers("CreateEmpty").OfType<IMethodSymbol>().Any(method => SnapshotPublicInstance(method) &&
                    method.Parameters.Length == 0 && SymbolEqualityComparer.Default.Equals(method.ReturnType, named)) ||
                !named.GetMembers("CopyFrom").OfType<IMethodSymbol>().Any(method => SnapshotPublicInstance(method) &&
                    method.ReturnsVoid && method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, named)) ||
                !named.GetMembers("Clear").OfType<IMethodSymbol>().Any(method => SnapshotPublicInstance(method) &&
                    method.ReturnsVoid && method.Parameters.Length == 0))
                return null;
            return new[] { key.ReturnType, value.ReturnType };
        }

        static bool SnapshotPublicInstance(IMethodSymbol method) => !method.IsStatic &&
            method.DeclaredAccessibility == Accessibility.Public;

        static bool SnapshotIntProperty(INamedTypeSymbol type, string name) => type.GetMembers(name).OfType<IPropertySymbol>()
            .Any(property => !property.IsStatic && property.Type.SpecialType == SpecialType.System_Int32 &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public);

        static ITypeSymbol PreparedSnapshotElement(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named)) return null;
            INamedTypeSymbol sequence = named.AllInterfaces.FirstOrDefault(contract =>
                Identity(contract.OriginalDefinition) == "System.Collections.Generic.IReadOnlyList<T>");
            if (sequence == null || !named.GetMembers("Capacity").OfType<IPropertySymbol>()
                    .Any(property => property.Type.SpecialType == SpecialType.System_Int32 && property.GetMethod != null) ||
                !named.GetMembers("Add").OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, sequence.TypeArguments[0])) ||
                !named.GetMembers("Clear").OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 0))
                return null;
            return sequence.TypeArguments[0];
        }

        static bool SnapshotValueIsCopyable(ITypeSymbol type, HashSet<string> visited, AttributeData resource = null)
        {
            if (type is ITypeParameterSymbol) return false;
            if (type.SpecialType == SpecialType.System_String || HasAttribute(type, TextAttribute) ||
                BorrowedStorage(type) != null || BorrowedObjectStorage(type) != null)
                return true;
            if (NullableElement(type) is ITypeSymbol nullable)
                return SnapshotValueIsCopyable(nullable, visited);
            if (!type.IsValueType) return resource != null && SnapshotResourceCanCall(type, resource);
            if (!visited.Add(Identity(type)) || type.TypeKind == TypeKind.Enum || type.SpecialType != SpecialType.None)
                return true;
            return type is INamedTypeSymbol value && ValueMembers(value)
                .All(member => SnapshotValueIsCopyable(member.Type, new HashSet<string>(visited), member.Symbol.GetAttributes()
                    .SingleOrDefault(attribute => Identity(attribute.AttributeClass) == ValueResourceAttribute)));
        }

        static bool SnapshotResourceCanCall(ITypeSymbol type, AttributeData resource) =>
            new[] { "Retain", "Release" }.All(operation =>
            {
                string method = (string)resource.ConstructorArguments[operation == "Retain" ? 0 : 1].Value;
                if (SnapshotResourcePublicMethod(type, method) || method == operation && SnapshotResourceInterface(type)) return true;
                return type is INamedTypeSymbol generated && HasAttribute(generated, ValueOperationsAttribute) &&
                    !generated.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute)
                        .SelectMany(attribute => attribute.NamedArguments).Any(argument => argument.Key == "Resources" && argument.Value.Value is false) &&
                    generated.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute)
                        .SelectMany(attribute => attribute.NamedArguments).Where(argument => argument.Key == operation + "Method")
                        .Select(argument => (string)argument.Value.Value).DefaultIfEmpty(operation).Single() == method;
            });

        static bool SnapshotResourcePublicMethod(ITypeSymbol type, string method)
        {
            for (INamedTypeSymbol owner = type as INamedTypeSymbol; owner != null; owner = owner.BaseType)
                if (owner.GetMembers(method).OfType<IMethodSymbol>().Any(candidate => SnapshotPublicInstance(candidate) &&
                    candidate.Parameters.Length == 0 && candidate.ReturnsVoid)) return true;
            return false;
        }

        static bool SnapshotResourceInterface(ITypeSymbol type) => type.IsReferenceType && type is INamedTypeSymbol named &&
            (Identity(named) == "ThirdPersonSimulation.IValueResources" ||
             named.AllInterfaces.Any(contract => Identity(contract) == "ThirdPersonSimulation.IValueResources"));

        static void EmitSnapshotInitialize(StringBuilder code, SnapshotShape shape, string source, string target, int depth)
        {
            switch (shape.Kind)
            {
                case SnapshotKind.Source:
                    code.Append(target).Append(" = ").Append(source).AppendLine(".CreateValueSnapshot();");
                    break;
                case SnapshotKind.Array:
                    code.Append(target).Append(" = ").Append(NewSnapshotArray(shape.Element.StorageType, source + ".Length")).AppendLine(";");
                    if (shape.Element.Kind == SnapshotKind.Value) break;
                    SnapshotLoop(code, source + ".Length", depth);
                    EmitSnapshotInitialize(code, shape.Element, source + "[__snapshotIndex" + depth + "]",
                        target + "[__snapshotIndex" + depth + "]", depth + 1);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Sequence:
                    if (shape.Element.Kind == SnapshotKind.Source)
                    {
                        code.Append(target).Append(" = new ").Append(shape.Element.StorageType).Append('[')
                            .Append(source).AppendLine(".Count];");
                        SnapshotLoop(code, source + ".Count", depth);
                        EmitSnapshotInitialize(code, shape.Element, source + "[__snapshotIndex" + depth + "]",
                            target + "[__snapshotIndex" + depth + "]", depth + 1);
                        code.AppendLine("}");
                    }
                    else
                        code.Append(target).Append(" = new ").Append(shape.StorageType).Append('(')
                            .Append(source).AppendLine(".Capacity);");
                    break;
                case SnapshotKind.Dictionary:
                    code.Append(target).Append(" = new ").Append(shape.StorageType).Append('(')
                        .Append(source).Append(".EnsureCapacity(0), ").Append(source).AppendLine(".Comparer);");
                    if (!shape.FixedKeys) break;
                    string pair = "__snapshotPair" + depth;
                    string value = "__snapshotValue" + depth;
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(source).AppendLine(") {");
                    code.Append(shape.Element.StorageType).Append(' ').Append(value).AppendLine(" = default;");
                    EmitSnapshotInitialize(code, shape.Element, pair + ".Value", value, depth + 1);
                    code.Append(target).Append(".Add(").Append(pair).Append(".Key, ").Append(value).AppendLine("); }");
                    break;
                case SnapshotKind.SortedList:
                    code.Append(target).Append(" = new ").Append(shape.StorageType).Append('(')
                        .Append(source).Append(".Capacity, ").Append(source).AppendLine(".Comparer);");
                    code.Append("_ = ").Append(source).AppendLine(".Keys;");
                    code.Append("_ = ").Append(source).AppendLine(".Values;");
                    code.Append("_ = ").Append(target).AppendLine(".Keys;");
                    code.Append("_ = ").Append(target).AppendLine(".Values;");
                    break;
                case SnapshotKind.KeyedBuffer:
                    code.Append(target).Append(" = ").Append(source).AppendLine(".CreateEmpty();");
                    break;
            }
        }

        static void EmitSnapshotResources(StringBuilder code, SnapshotShape shape, string value,
            bool stored, int depth, string operation = "Release")
        {
            if (!SnapshotHasResources(shape, stored)) return;
            switch (shape.Kind)
            {
                case SnapshotKind.Value:
                    SnapshotHoldValue(code, operation, shape, value, depth);
                    break;
                case SnapshotKind.Source:
                    if (stored) code.Append(value).Append('.').Append(operation).AppendLine("();");
                    break;
                case SnapshotKind.Array:
                    SnapshotLoop(code, value + ".Length", depth);
                    EmitSnapshotResources(code, shape.Element, value + "[__snapshotIndex" + depth + "]",
                        stored, depth + 1, operation);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Sequence:
                    SnapshotLoop(code, value + (stored && shape.Element.Kind == SnapshotKind.Source ? ".Length" : ".Count"), depth);
                    EmitSnapshotResources(code, shape.Element, value + "[__snapshotIndex" + depth + "]",
                        stored, depth + 1, operation);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Dictionary:
                    string pair = "__snapshotPair" + depth;
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(value).AppendLine(") {");
                    if (!shape.FixedKeys || stored)
                        HoldValueResources(code, operation, shape.Key, pair + ".Key", depth + 1);
                    EmitSnapshotResources(code, shape.Element, pair + ".Value", stored, depth + 1, operation);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.SortedList:
                case SnapshotKind.KeyedBuffer:
                    SnapshotLoop(code, value + ".Count", depth);
                    string index = "__snapshotIndex" + depth;
                    HoldValueResources(code, operation, shape.Key, SnapshotKey(shape, value, index), depth + 1);
                    EmitSnapshotResources(code, shape.Element, SnapshotValue(shape, value, index), stored, depth + 1, operation);
                    code.AppendLine("}");
                    break;
            }
        }

        static bool SnapshotHasResources(SnapshotShape shape, bool stored) =>
            shape.Kind == SnapshotKind.Value ? shape.Resource != null ||
                HasValueResources(shape.Type, new HashSet<string>()) :
            shape.Kind == SnapshotKind.Source ? stored :
            shape.Key != null && (!shape.FixedKeys || stored) && HasValueResources(shape.Key, new HashSet<string>()) ||
                SnapshotHasResources(shape.Element, stored);

        static string SnapshotKey(SnapshotShape shape, string value, string index) => shape.Kind == SnapshotKind.KeyedBuffer
            ? value + ".GetKey(" + index + ")" : value + ".Keys[" + index + "]";

        static string SnapshotValue(SnapshotShape shape, string value, string index) => shape.Kind == SnapshotKind.KeyedBuffer
            ? value + ".GetValue(" + index + ")" : value + ".Values[" + index + "]";

        static string NewSnapshotArray(string element, string length)
        {
            string suffix = "";
            while (element.EndsWith("[]", StringComparison.Ordinal))
            {
                suffix += "[]";
                element = element.Substring(0, element.Length - 2);
            }
            return "new " + element + "[" + length + "]" + suffix;
        }

        static void EmitSnapshotCopy(StringBuilder code, SnapshotShape shape, string owner, string stored,
            bool capture, int depth, bool carrierCopy = false, bool ownerCopy = false, bool retain = true)
        {
            string source = capture ? owner : stored;
            string target = capture ? stored : owner;
            switch (shape.Kind)
            {
                case SnapshotKind.Value:
                    code.Append(target).Append(" = ").Append(source).AppendLine(";");
                    if (retain) SnapshotHoldValue(code, "Retain", shape, target, depth);
                    break;
                case SnapshotKind.Source:
                    if (ownerCopy)
                        code.Append(target).Append('.').Append(Escape(SnapshotOwnerMethod(shape.Type, "CopyMethod")))
                            .Append('(').Append(source).AppendLine(");");
                    else if (carrierCopy)
                        code.Append(target).Append(".CopyFrom(").Append(source).AppendLine(");");
                    else
                        code.Append(owner).Append(capture ? ".CaptureValueSnapshot(" : ".RestoreValueSnapshot(")
                            .Append(stored).AppendLine(");");
                    break;
                case SnapshotKind.Array:
                    SnapshotLoop(code, source + ".Length", depth);
                    EmitSnapshotCopy(code, shape.Element, owner + "[__snapshotIndex" + depth + "]",
                        stored + "[__snapshotIndex" + depth + "]", capture, depth + 1, carrierCopy, ownerCopy, retain);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Sequence:
                    if (shape.Element.Kind == SnapshotKind.Source)
                    {
                        SnapshotLoop(code, owner + (carrierCopy ? ".Length" : ".Count"), depth);
                        EmitSnapshotCopy(code, shape.Element, owner + "[__snapshotIndex" + depth + "]",
                            stored + "[__snapshotIndex" + depth + "]", capture, depth + 1, carrierCopy, ownerCopy, retain);
                        code.AppendLine("}");
                        break;
                    }
                    code.Append(target).AppendLine(".Clear();");
                    SnapshotLoop(code, source + ".Count", depth);
                    string element = "__snapshotValue" + depth;
                    code.Append("var ").Append(element).Append(" = ").Append(source)
                        .Append("[__snapshotIndex").Append(depth).AppendLine("];");
                    code.Append(target).Append(".Add(").Append(element).AppendLine(");");
                    if (retain) SnapshotHoldValue(code, "Retain", shape.Element, element, depth + 1);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Dictionary:
                    string pair = "__snapshotPair" + depth;
                    if (!shape.FixedKeys) code.Append(target).AppendLine(".Clear();");
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(source).AppendLine(") {");
                    if (shape.FixedKeys)
                        EmitSnapshotCopy(code, shape.Element, owner + "[" + pair + ".Key]",
                            stored + "[" + pair + ".Key]", capture, depth + 1, carrierCopy, ownerCopy, retain);
                    else
                    {
                        code.Append(target).Append(".Add(").Append(pair).Append(".Key, ").Append(pair).AppendLine(".Value);");
                        if (retain) SnapshotHoldValue(code, "Retain", shape.Element, pair + ".Value", depth + 1);
                    }
                    if (retain && (!shape.FixedKeys || capture && !ownerCopy || carrierCopy))
                        HoldValueResources(code, "Retain", shape.Key, pair + ".Key", depth + 1);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.SortedList:
                    code.Append(target).AppendLine(".Clear();");
                    SnapshotLoop(code, source + ".Count", depth);
                    string index = "__snapshotIndex" + depth;
                    code.Append(target).Append(".Add(").Append(SnapshotKey(shape, source, index)).Append(", ")
                        .Append(SnapshotValue(shape, source, index)).AppendLine(");");
                    if (retain)
                    {
                        HoldValueResources(code, "Retain", shape.Key, SnapshotKey(shape, source, index), depth + 1);
                        SnapshotHoldValue(code, "Retain", shape.Element, SnapshotValue(shape, source, index), depth + 1);
                    }
                    code.AppendLine("}");
                    break;
                case SnapshotKind.KeyedBuffer:
                    code.Append(target).Append(".CopyFrom(").Append(source).AppendLine(");");
                    if (retain) EmitSnapshotResources(code, shape, target, capture || carrierCopy, depth, "Retain");
                    break;
            }
        }

        static string SnapshotOwnerMethod(ITypeSymbol type, string purpose) => type.GetAttributes()
            .Where(attribute => Identity(attribute.AttributeClass) == SnapshotAttribute).SelectMany(attribute => attribute.NamedArguments)
            .Where(argument => argument.Key == purpose).Select(argument => (string)argument.Value.Value).SingleOrDefault();

        static bool SnapshotOwnerOperationSupported(SnapshotShape shape, string purpose) =>
            shape.Kind == SnapshotKind.Source ? SnapshotOwnerMethod(shape.Type, purpose) != null :
            shape.Kind == SnapshotKind.Dictionary && shape.FixedKeys && shape.Element.Kind == SnapshotKind.Value && purpose == "ClearMethod" ? false :
            shape.Element == null || SnapshotOwnerOperationSupported(shape.Element, purpose);

        static void EmitSnapshotClear(StringBuilder code, SnapshotShape shape, string value, int depth)
        {
            switch (shape.Kind)
            {
                case SnapshotKind.Value:
                    code.Append(value).Append(" = ").Append(ValueDefault(shape.Type)).AppendLine(";");
                    break;
                case SnapshotKind.Array:
                    if (shape.Element.Kind == SnapshotKind.Value)
                        code.Append("global::System.Array.Clear(").Append(value).Append(", 0, ")
                            .Append(value).AppendLine(".Length);");
                    else
                    {
                        SnapshotLoop(code, value + ".Length", depth);
                        EmitSnapshotClear(code, shape.Element, value + "[__snapshotIndex" + depth + "]", depth + 1);
                        code.AppendLine("}");
                    }
                    break;
                case SnapshotKind.Sequence:
                    if (shape.Element.Kind == SnapshotKind.Source)
                    {
                        SnapshotLoop(code, value + ".Count", depth);
                        EmitSnapshotClear(code, shape.Element, value + "[__snapshotIndex" + depth + "]", depth + 1);
                        code.AppendLine("}");
                        break;
                    }
                    code.Append(value).AppendLine(".Clear();");
                    break;
                case SnapshotKind.Dictionary:
                    if (shape.FixedKeys)
                    {
                        string pair = "__snapshotPair" + depth;
                        code.Append("foreach (var ").Append(pair).Append(" in ").Append(value).AppendLine(") {");
                        EmitSnapshotClear(code, shape.Element, value + "[" + pair + ".Key]", depth + 1);
                        code.AppendLine("}");
                    }
                    else
                        code.Append(value).AppendLine(".Clear();");
                    break;
                case SnapshotKind.SortedList:
                case SnapshotKind.KeyedBuffer:
                    code.Append(value).AppendLine(".Clear();");
                    break;
                case SnapshotKind.Source:
                    code.Append(value).Append('.').Append(Escape(SnapshotOwnerMethod(shape.Type, "ClearMethod"))).AppendLine("();");
                    break;
            }
        }

        static void SnapshotLoop(StringBuilder code, string count, int depth) =>
            code.Append("for (int __snapshotIndex").Append(depth).Append(" = 0; __snapshotIndex")
                .Append(depth).Append(" < ").Append(count).Append("; __snapshotIndex")
                .Append(depth).AppendLine("++) {");

        static void SnapshotHoldValue(StringBuilder code, string operation, SnapshotShape shape, string value, int depth)
        {
            if (shape.Resource == null)
                HoldValueResources(code, operation, shape.Type, value, depth);
            else
                EmitDeclaredValueResource(code, operation, shape.Type, value, shape.Resource);
        }

        static void EmitDeclaredValueResource(StringBuilder code, string operation, ITypeSymbol type, string value, AttributeData resource)
        {
            string method = (string)resource.ConstructorArguments[operation == "Retain" ? 0 : 1].Value;
            if (method == operation && SnapshotResourceInterface(type) && !SnapshotResourcePublicMethod(type, method))
                value = "((global::ThirdPersonSimulation.IValueResources)" + value + ")";
            code.Append(value).Append(resource.NamedArguments.Any(argument =>
                argument.Key == "Optional" && argument.Value.Value is true) ? "?." : ".")
                .Append(Escape(method)).AppendLine("();");
        }

        static void HoldValueResourceElements(StringBuilder code, string operation, ITypeSymbol type,
            string value, AttributeData resource, int depth)
        {
            if (type is INamedTypeSymbol dictionary &&
                Identity(dictionary.OriginalDefinition) == "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                string pair = "__resourcePair" + depth;
                code.Append("foreach (var ").Append(pair).Append(" in ").Append(value).AppendLine(") {");
                HoldValueResourceElements(code, operation, dictionary.TypeArguments[1], pair + ".Value", resource, depth + 1);
                code.AppendLine("}");
                return;
            }
            ITypeSymbol element = null;
            string item = null;
            string index = "__resourceIndex" + depth;
            if (MapTypes(type) is ITypeSymbol[] map)
            {
                element = map[1];
                item = value + ".Values[" + index + "]";
            }
            else if (PreparedSnapshotKeyedTypes(type) is ITypeSymbol[] keyed)
            {
                element = keyed[1];
                item = value + ".GetValue(" + index + ")";
            }
            else if (SequenceElement(type) is ITypeSymbol sequence)
            {
                element = sequence;
                item = SequenceValue(type, value, index);
            }
            if (element == null)
            {
                EmitDeclaredValueResource(code, operation, type, value, resource);
                return;
            }
            code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                .Append(SequenceCount(type, value)).Append("; ").Append(index).AppendLine("++) {");
            HoldValueResourceElements(code, operation, element, item, resource, depth + 1);
            code.AppendLine("}");
        }
    }
}
