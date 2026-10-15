using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;
using static ThirdPerson.ValueGeneration.ValueSnapshotModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        const string SnapshotAttributeName = "ThirdPersonSimulation.ValueSnapshotAttribute";
        const string SnapshotCopyContract = "ThirdPersonSimulation.IValueSnapshotCopy";
        const string SnapshotResourceAttribute = "ThirdPersonSimulation.ValueResourceAttribute";

        enum NativeSnapshotKind { Value, Source, Array, List, Dictionary, SortedList }

        sealed class NativeSnapshotShape
        {
            public NativeSnapshotKind Kind;
            public ITypeSymbol Type;
            public NativeSnapshotShape Element;
            public NativeSnapshotShape Key;
            public bool FixedKeys;
            public string Retain;
            public string Release;
            public bool Optional;
            public bool DynamicSource;
        }

        sealed class NativeSnapshotPlan
        {
            public INamedTypeSymbol Type;
            public string Name;
            public bool Original;
            public ISymbol[] Fields;
            public NativeSnapshotShape[] Shapes;
            public string Error;
        }

        static bool IsSnapshotCopy(ITypeSymbol type) => type is INamedTypeSymbol named &&
            (Identity(named) == SnapshotCopyContract || named.AllInterfaces.Any(contract => Identity(contract) == SnapshotCopyContract));

        static SortedDictionary<string, NativeSnapshotPlan> MemoryPackSnapshotCandidates(GeneratorExecutionContext context,
            HashSet<string> externalLeaves, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference)
        {
            var result = new SortedDictionary<string, NativeSnapshotPlan>(StringComparer.Ordinal);
            IEnumerable<INamedTypeSymbol> types = FormatterTypes(context.Compilation.Assembly.GlobalNamespace).Concat(
                context.Compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(assembly => FormatterTypes(assembly.GlobalNamespace)));
            var candidates = types.Where(type => type.TypeKind == TypeKind.Class && IsSnapshotCopy(type))
                .Select(type => new NativeSnapshotPlan { Type = type, Name = TypeName(type) });
            candidates = candidates.Concat(FormatterTypes(context.Compilation.Assembly.GlobalNamespace)
                .Where(type => type.TypeKind == TypeKind.Class && HasAttribute(type, SnapshotAttributeName))
                .Select(type => new NativeSnapshotPlan { Type = type, Name = SnapshotTypeName(type), Original = true }));
            foreach (NativeSnapshotPlan candidate in candidates)
            {
                candidate.Error = PrepareMemoryPackSnapshot(context, candidate, externalLeaves, implicitReference);
                result[candidate.Name] = candidate;
            }
            return result;
        }

        static bool CanEncodeSnapshot(NativeSnapshotPlan candidate, SortedDictionary<string, NativeSnapshotPlan> candidates,
            HashSet<string> externalLeaves, HashSet<string> visited)
        {
            if (candidate.Error != null) return false;
            if (!visited.Add(candidate.Name)) return true;
            return candidate.Shapes.All(Visit);

            bool Visit(NativeSnapshotShape shape)
            {
                if (shape.Kind == NativeSnapshotKind.Source && !shape.DynamicSource)
                {
                    string name = !IsSnapshotCopy(shape.Type) && HasAttribute(shape.Type, SnapshotAttributeName) ?
                        SnapshotTypeName((INamedTypeSymbol)shape.Type) : TypeName(shape.Type);
                    if (!externalLeaves.Contains(Identity(shape.Type)) &&
                        (!candidates.TryGetValue(name, out NativeSnapshotPlan nested) ||
                         !CanEncodeSnapshot(nested, candidates, externalLeaves, visited))) return false;
                }
                return (shape.Key == null || Visit(shape.Key)) && (shape.Element == null || Visit(shape.Element));
            }
        }

        static string PrepareMemoryPackSnapshot(GeneratorExecutionContext context, NativeSnapshotPlan candidate,
            HashSet<string> externalLeaves, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference)
        {
            ITypeSymbol text = context.Compilation.GetTypeByMetadataName("ThirdPersonSimulation.Serialization.MemoryPackContentContext")?
                .GetMembers("ReadText").OfType<IMethodSymbol>().SingleOrDefault()?.ReturnType;
            INamedTypeSymbol type = candidate.Type;
            ISymbol[] fields = type.GetMembers().Where(field => (field is IFieldSymbol || candidate.Original && field is IPropertySymbol) &&
                !field.IsStatic && HasAttribute(field, SnapshotAttributeName)).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            string reason = fields.Length == 0 ? "捕获载体没有原 ValueSnapshot 成员" :
                fields.Length > 249 ? "捕获值成员超过 MemoryPack 对象头上限" :
                !candidate.Original && !context.Compilation.IsSymbolAccessibleWithin(type, context.Compilation.Assembly) ? "捕获载体必须提供可访问的中立数据合同" :
                !candidate.Original && fields.Any(field => ((IFieldSymbol)field).IsReadOnly || field.DeclaredAccessibility != Accessibility.Public) ?
                    "捕获载体的 ValueSnapshot 字段必须是公开的原存储" : null;
            var shapes = new List<NativeSnapshotShape>();
            foreach (ISymbol field in fields)
            {
                if (reason != null) break;
                AttributeData snapshot = field.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == SnapshotAttributeName);
                AttributeData resource = field.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == SnapshotResourceAttribute);
                ITypeSymbol fieldType = field is IFieldSymbol stored ? stored.Type : ((IPropertySymbol)field).Type;
                if (!TryNativeSnapshotShape(fieldType, SnapshotOption(snapshot, "FixedKeys"), resource,
                    DeclaredText(field), text, implicitReference, externalLeaves, out NativeSnapshotShape shape, out reason, candidate.Original))
                    reason = field.Name + ": " + reason;
                else shapes.Add(shape);
            }
            candidate.Fields = fields;
            candidate.Shapes = shapes.ToArray();
            return reason;
        }

        static bool SnapshotOption(AttributeData attribute, string name) => attribute != null &&
            attribute.NamedArguments.Any(argument => argument.Key == name && argument.Value.Value is true);

        static bool TryNativeSnapshotShape(ITypeSymbol type, bool fixedKeys, AttributeData resource, bool declared,
            ITypeSymbol text, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, HashSet<string> externalLeaves,
            out NativeSnapshotShape shape, out string reason, bool original = false)
        {
            shape = new NativeSnapshotShape { Type = type, FixedKeys = fixedKeys };
            reason = null;
            if (IsSnapshotCopy(type) || original && IsSnapshotSource(type))
            {
                shape.Kind = NativeSnapshotKind.Source;
                shape.DynamicSource = Identity(type) == SnapshotCopyContract || original && !HasAttribute(type, SnapshotAttributeName);
                if (!type.IsReferenceType) reason = "嵌套捕获值必须是已准备的引用实例，不能装箱";
            }
            else if (type is IArrayTypeSymbol array)
            {
                shape.Kind = NativeSnapshotKind.Array;
                if (array.Rank != 1) reason = "捕获数组必须是一维或锯齿固定形状";
                else TryNativeSnapshotShape(array.ElementType, fixedKeys, SnapshotOption(resource, "Elements") ? resource : null,
                    declared, text, implicitReference, externalLeaves, out shape.Element, out reason, original);
            }
            else if (original && PreparedSnapshotElement(type) is ITypeSymbol element)
            {
                shape.Kind = NativeSnapshotKind.List;
                TryNativeSnapshotShape(element, fixedKeys, SnapshotOption(resource, "Elements") ? resource : null,
                    declared, text, implicitReference, externalLeaves, out shape.Element, out reason, true);
                if (reason == null && shape.Element.Kind == NativeSnapshotKind.Source)
                    shape.Kind = NativeSnapshotKind.Array;
                else if (reason == null && shape.Element.Kind != NativeSnapshotKind.Value)
                    reason = "捕获 roster 的元素必须是原值或嵌套状态源";
            }
            else if (type is INamedTypeSymbol collection && collection.IsGenericType &&
                (Identity(collection.OriginalDefinition) == "System.Collections.Generic.List<T>" ||
                 Identity(collection.OriginalDefinition) == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
                 Identity(collection.OriginalDefinition) == "System.Collections.Generic.SortedList<TKey, TValue>"))
            {
                bool list = collection.TypeArguments.Length == 1;
                shape.Kind = list ? NativeSnapshotKind.List : Identity(collection.OriginalDefinition) ==
                    "System.Collections.Generic.Dictionary<TKey, TValue>" ? NativeSnapshotKind.Dictionary : NativeSnapshotKind.SortedList;
                AttributeData elementResource = SnapshotOption(resource, "Elements") ? resource : null;
                if (!list && !TryNativeSnapshotShape(collection.TypeArguments[0], false, null, declared,
                    text, implicitReference, externalLeaves, out shape.Key, out reason, original)) return false;
                if (reason == null) TryNativeSnapshotShape(collection.TypeArguments[list ? 0 : 1], fixedKeys, elementResource,
                    declared, text, implicitReference, externalLeaves, out shape.Element, out reason, original);
                if (reason == null && shape.Key != null && shape.Key.Kind != NativeSnapshotKind.Value)
                    reason = "捕获集合的键必须是原值";
                if (reason == null && shape.Element.Kind != NativeSnapshotKind.Value &&
                    (shape.Kind != NativeSnapshotKind.Dictionary || !fixedKeys))
                    reason = "可变集合只能接收原值；嵌套捕获值需要固定 roster 或 FixedKeys";
            }
            else
            {
                reason = NativeSnapshotValueReason(type, declared, text, implicitReference, externalLeaves, new HashSet<string>());
                if (reason == null && (resource != null || SnapshotValueHasResources(type, implicitReference, new HashSet<string>())))
                {
                    shape.Retain = SnapshotResourceMethod(type, resource, "Retain");
                    shape.Release = SnapshotResourceMethod(type, resource, "Release");
                    shape.Optional = SnapshotOption(resource, "Optional");
                    if (shape.Retain == null || shape.Release == null)
                        reason = "拥有资源的原值必须提供可直接调用的 Retain/Release，不能通过接口装箱或手写镜像";
                }
            }
            return reason == null;
        }

        static string NativeSnapshotValueReason(ITypeSymbol type, bool declared, ITypeSymbol text,
            Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, HashSet<string> externalLeaves, HashSet<string> visited)
        {
            if (IsUnmanagedValue(type)) return null;
            if (type.SpecialType == SpecialType.System_String)
                return declared ? null : "prepared string 必须由原成员声明 ValueTextRules.Declared，不能新建 string";
            if (type.SpecialType != SpecialType.None && !type.IsReferenceType) return null;
            if (HasAttribute(type, TextAttribute)) return SymbolEqualityComparer.Default.Equals(type, text) ? null :
                "原文本缺少现有 prepared context 的存储合同";
            if (type.IsReferenceType) return "原引用不是已准备的捕获载体，不能在读取时创建对象";
            if (externalLeaves.Contains(Identity(type)) || !visited.Add(Identity(type))) return null;
            if (NullableElement(type) is ITypeSymbol nullable)
                return NativeSnapshotValueReason(nullable, declared, text, implicitReference, externalLeaves, visited);
            if (!(type is INamedTypeSymbol named)) return "捕获原值缺少可生成的正式成员合同";
            foreach (Member member in ValueMembers(named, implicitReference))
            {
                if (SequenceElement(member.Type) != null)
                    return "原值内部集合需要已准备存储，不能使用冷读取构造：" + member.Name;
                string reason = NativeSnapshotValueReason(member.Type, DeclaredText(member.Symbol), text,
                    implicitReference, externalLeaves, new HashSet<string>(visited));
                if (reason != null) return member.Name + ": " + reason;
            }
            return null;
        }

        static bool SnapshotValueHasResources(ITypeSymbol type, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference,
            HashSet<string> visited)
        {
            if (HasAttribute(type, TextAttribute)) return true;
            if (type.GetAttributes().Any(attribute => Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueOperationsAttribute" &&
                attribute.NamedArguments.Any(argument => argument.Key == "Resources" && argument.Value.Value is false))) return false;
            if (type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum || !visited.Add(Identity(type))) return false;
            if (type is INamedTypeSymbol named && named.AllInterfaces.Any(contract => Identity(contract) == "ThirdPersonSimulation.IValueResources"))
                return true;
            if (IsUnmanagedValue(type)) return false;
            if (NullableElement(type) is ITypeSymbol nullable) return SnapshotValueHasResources(nullable, implicitReference, visited);
            return type is INamedTypeSymbol value && ValueMembers(value, implicitReference).Any(member =>
                HasAttribute(member.Symbol, SnapshotResourceAttribute) ||
                SnapshotValueHasResources(member.Type, implicitReference, new HashSet<string>(visited)));
        }

        static string SnapshotResourceMethod(ITypeSymbol type, AttributeData resource, string operation)
        {
            string method = resource == null ? operation : (string)resource.ConstructorArguments[operation == "Retain" ? 0 : 1].Value;
            if (type.GetMembers(method).OfType<IMethodSymbol>().Any(candidate => !candidate.IsStatic && candidate.ReturnsVoid &&
                candidate.Parameters.Length == 0 && candidate.DeclaredAccessibility == Accessibility.Public)) return method;
            AttributeData generated = type.GetAttributes().SingleOrDefault(attribute =>
                Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueOperationsAttribute");
            if (generated != null && !generated.NamedArguments.Any(argument => argument.Key == "Resources" && argument.Value.Value is false))
                return generated.NamedArguments.Where(argument => argument.Key == operation + "Method")
                    .Select(argument => (string)argument.Value.Value).DefaultIfEmpty(operation).Single() == method ? method : null;
            return HasAttribute(type, UnionAttribute) && method == operation ? method : null;
        }

        static void EmitMemoryPackSnapshotFormatter(StringBuilder code, NativeSnapshotPlan plan, int ordinal, bool scopedReferences, string schemaHash)
        {
            string name = plan.Name;
            EmitMemoryPackFormatterHeader(code, "SnapshotFormatter" + ordinal, name, schemaHash);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            code.Append("writer.WriteObjectHeader(").Append(plan.Fields.Length).AppendLine(");");
            int next = 0;
            for (int index = 0; index < plan.Fields.Length; index++)
                EmitSnapshotNativeWrite(code, plan.Shapes[index], "value." + Escape(plan.Fields[index].Name), ref next);
            code.AppendLine("}");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.Append("if (!reader.TryReadObjectHeader(out byte members) || members != ").Append(plan.Fields.Length)
                .AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"捕获值成员数与当前格式不符。\");");
            next = 0;
            for (int index = 0; index < plan.Fields.Length; index++)
                EmitSnapshotNativeRead(code, plan.Shapes[index], "value." + Escape(plan.Fields[index].Name), ref next);
            code.AppendLine("} }");
        }

        static void EmitSnapshotNativeWrite(StringBuilder code, NativeSnapshotShape shape, string value, ref int next)
        {
            int id = next++;
            string index = "__index" + id;
            switch (shape.Kind)
            {
                case NativeSnapshotKind.Value:
                    EmitValueWrite(code, shape.Type, value);
                    break;
                case NativeSnapshotKind.Source:
                    code.Append("writer.WriteValue(");
                    if (shape.DynamicSource) code.Append(value).Append(".GetType(), ");
                    code.Append(value).AppendLine(");");
                    break;
                case NativeSnapshotKind.Array:
                case NativeSnapshotKind.List:
                    string count = value + (shape.Kind == NativeSnapshotKind.Array ? ".Length" : ".Count");
                    code.Append("writer.WriteCollectionHeader(").Append(count).AppendLine(");");
                    code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(count)
                        .Append("; ").Append(index).AppendLine("++) {");
                    EmitSnapshotNativeWrite(code, shape.Element, value + "[" + index + "]", ref next);
                    code.AppendLine("}");
                    break;
                case NativeSnapshotKind.Dictionary:
                case NativeSnapshotKind.SortedList:
                    string pair = "__pair" + id;
                    code.Append("writer.WriteCollectionHeader(").Append(value).AppendLine(".Count);");
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(value).AppendLine(") {");
                    EmitSnapshotNativeWrite(code, shape.Key, pair + ".Key", ref next);
                    EmitSnapshotNativeWrite(code, shape.Element, pair + ".Value", ref next);
                    code.AppendLine("}");
                    break;
            }
        }

        static void EmitSnapshotNativeRead(StringBuilder code, NativeSnapshotShape shape, string value, ref int next)
        {
            int id = next++;
            string incoming = "__incoming" + id;
            string index = "__index" + id;
            if (shape.Kind == NativeSnapshotKind.Value)
            {
                EmitSnapshotIncoming(code, shape, incoming);
                EmitSnapshotResource(code, shape, incoming, true);
                EmitSnapshotResource(code, shape, value, false);
                code.Append(value).Append(" = ").Append(incoming).AppendLine(";");
                return;
            }
            if (shape.Kind == NativeSnapshotKind.Source)
            {
                code.Append(shape.DynamicSource ? "object " : "var ")
                    .Append(incoming).Append(" = ").Append(value).AppendLine(";");
                code.Append("reader.ReadValue(");
                if (shape.DynamicSource) code.Append(incoming).Append(".GetType(), ");
                code.Append("ref ").Append(incoming).AppendLine(");");
                return;
            }
            string count = "__count" + id;
            code.Append("if (!reader.TryReadCollectionHeader(out int ").Append(count)
                .AppendLine(")) throw new global::MemoryPack.MemoryPackSerializationException(\"已准备捕获容器不可为 null。\");");
            string capacity = shape.Kind == NativeSnapshotKind.Array ? value + ".Length" :
                shape.Kind == NativeSnapshotKind.Dictionary ? value + (shape.FixedKeys ? ".Count" : ".EnsureCapacity(0)") : value + ".Capacity";
            bool fixedShape = shape.Kind == NativeSnapshotKind.Array || shape.Kind == NativeSnapshotKind.Dictionary && shape.FixedKeys;
            code.Append(fixedShape ? "if (" : "if ((uint)").Append(count).Append(fixedShape ? " != " : " > (uint)").Append(capacity)
                .AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"捕获容器与已准备形状或容量不符。\");");
            if (!fixedShape)
            {
                EmitSnapshotContainerRelease(code, shape, value, ref next);
                code.Append(value).AppendLine(".Clear();");
            }
            code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(count)
                .Append("; ").Append(index).AppendLine("++) {");
            if (shape.Kind == NativeSnapshotKind.Array)
                EmitSnapshotNativeRead(code, shape.Element, value + "[" + index + "]", ref next);
            else if (shape.Kind == NativeSnapshotKind.List)
            {
                EmitSnapshotIncoming(code, shape.Element, incoming);
                code.Append(value).Append(".Add(").Append(incoming).AppendLine(");");
                EmitSnapshotResource(code, shape.Element, incoming, true);
            }
            else
            {
                string key = "__key" + id;
                EmitSnapshotIncoming(code, shape.Key, key);
                if (fixedShape)
                    EmitSnapshotNativeRead(code, shape.Element, value + "[" + key + "]", ref next);
                else
                {
                    EmitSnapshotIncoming(code, shape.Element, incoming);
                    code.Append(value).Append(".Add(").Append(key).Append(", ").Append(incoming).AppendLine(");");
                    EmitSnapshotResource(code, shape.Key, key, true);
                    EmitSnapshotResource(code, shape.Element, incoming, true);
                }
            }
            code.AppendLine("}");
        }

        static void EmitSnapshotIncoming(StringBuilder code, NativeSnapshotShape shape, string incoming)
        {
            code.Append(TypeName(shape.Type)).Append(' ').Append(incoming).AppendLine(" = default;");
            if (shape.Type.SpecialType == SpecialType.System_String)
                code.Append(incoming).AppendLine(" = ((global::ThirdPersonSimulation.Serialization.MemoryPackContentContext)reader.Options.ServiceProvider).ReadDeclaredText(ref reader);");
            else if (HasAttribute(shape.Type, TextAttribute))
                code.Append(incoming).AppendLine(" = ((global::ThirdPersonSimulation.Serialization.MemoryPackContentContext)reader.Options.ServiceProvider).ReadText(ref reader);");
            else EmitValueRead(code, shape.Type, incoming);
        }

        static void EmitSnapshotResource(StringBuilder code, NativeSnapshotShape shape, string value, bool retain)
        {
            string method = retain ? shape.Retain : shape.Release;
            if (method == null) return;
            if (shape.Optional && shape.Type.IsReferenceType) code.Append("if (").Append(value).AppendLine(" != null) {");
            code.Append(value).Append('.').Append(Escape(method)).AppendLine("();");
            if (shape.Optional && shape.Type.IsReferenceType) code.AppendLine("}");
        }

        static void EmitSnapshotContainerRelease(StringBuilder code, NativeSnapshotShape shape, string value, ref int next)
        {
            if (shape.Element.Release == null && (shape.Key == null || shape.Key.Release == null)) return;
            string item = "__previous" + next++;
            code.Append("foreach (var ").Append(item).Append(" in ").Append(value).AppendLine(") {");
            if (shape.Key != null) EmitSnapshotResource(code, shape.Key, item + ".Key", false);
            EmitSnapshotResource(code, shape.Element, item + (shape.Key == null ? "" : ".Value"), false);
            code.AppendLine("}");
        }
    }
}
