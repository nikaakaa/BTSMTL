// 字段与类型的 Serializable 元数据标志正是 Unity 序列化规则的输入，此处读取而非用于 BinaryFormatter。
#pragma warning disable SYSLIB0050
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BTSMTL.WebWorkbench;

// 播放器构建不带 TypeTree，场景与资产按打包时的字段布局反序列化。从构建登记的根脚本类型出发，
// 按 Unity 序列化规则收集会被序列化的字段及其可达的 [Serializable] 类型，新旧 DLL 布局必须完全一致。
static class PlayerSerializedLayout
{
    static readonly HashSet<string> s_UnityObjects = new(StringComparer.Ordinal)
    {
        "UnityEngine.Object", "UnityEngine.Component", "UnityEngine.Behaviour", "UnityEngine.MonoBehaviour",
        "UnityEngine.ScriptableObject"
    };

    public static IEnumerable<string> Compare(IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> next, IReadOnlyList<string> roots)
    {
        Dictionary<string, string> before = new Catalog(current).Layouts(roots);
        Dictionary<string, string> after = new Catalog(next).Layouts(roots);
        foreach (string type in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(type, out string? old);
            after.TryGetValue(type, out string? updated);
            if (old == updated)
                continue;
            yield return old == null ? $"序列化类型 {type} 新进入场景/资产可达范围：{updated}，需要整包重建。"
                : updated == null ? $"序列化类型 {type} 已删除或不再可序列化（原布局 {old}），需要整包重建。"
                : $"序列化类型 {type} 字段布局变化，需要整包重建：原 {old}；新 {updated}";
        }
    }

    sealed class Catalog
    {
        readonly Dictionary<string, (MetadataReader Reader, TypeDefinitionHandle Handle, string Assembly)> m_Types = new(StringComparer.Ordinal);
        readonly List<PEReader> m_Readers = new();

        public Catalog(IReadOnlyDictionary<string, string> assemblies)
        {
            foreach ((string name, string path) in assemblies)
            {
                var reader = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
                m_Readers.Add(reader);
                MetadataReader metadata = reader.GetMetadataReader();
                foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                    m_Types[name + "|" + FullName(metadata, handle)] = (metadata, handle, name);
            }
        }

        public Dictionary<string, string> Layouts(IEnumerable<string> roots)
        {
            var layouts = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<string>(roots.Where(m_Types.ContainsKey));
            while (pending.Count > 0)
            {
                string type = pending.Dequeue();
                if (layouts.ContainsKey(type) || !m_Types.TryGetValue(type, out var entry))
                    continue;
                layouts[type] = Layout(entry.Reader, entry.Handle, entry.Assembly, pending);
            }
            return layouts;
        }

        string Layout(MetadataReader reader, TypeDefinitionHandle handle, string assembly, Queue<string> pending)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            var provider = new TypeNames(reader, assembly);
            string baseType = definition.BaseType.IsNil ? "" : provider.Name(definition.BaseType);
            if (m_Types.ContainsKey(baseType))
                pending.Enqueue(baseType);
            var fields = new List<string>();
            foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                FieldAttributes attributes = field.Attributes;
                if ((attributes & (FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.InitOnly |
                                   FieldAttributes.NotSerialized)) != 0)
                    continue;
                bool reference = HasAttribute(reader, field.GetCustomAttributes(), "UnityEngine.SerializeReference");
                if ((attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public && !reference &&
                    !HasAttribute(reader, field.GetCustomAttributes(), "UnityEngine.SerializeField"))
                    continue;
                string fieldType = field.DecodeSignature(provider, null);
                fields.Add(reader.GetString(field.Name) + ":" + fieldType + (reference ? ":ref" : ""));
                foreach (string candidate in Elements(fieldType))
                {
                    if (reference)
                        foreach (string implementation in Implementations(candidate))
                            pending.Enqueue(implementation);
                    else if (Serializable(candidate))
                        pending.Enqueue(candidate);
                }
            }
            string serializable = (definition.Attributes & TypeAttributes.Serializable) != 0 ? "[Serializable]" : "";
            return $"{serializable}{baseType}{{{string.Join(", ", fields)}}}";
        }

        // 项目内的 UnityEngine.Object 子类按引用序列化，不展开；其它类型需带 [Serializable] 且非枚举才按值展开。
        bool Serializable(string type)
        {
            if (!m_Types.TryGetValue(type, out var entry))
                return false;
            TypeDefinition definition = entry.Reader.GetTypeDefinition(entry.Handle);
            return (definition.Attributes & TypeAttributes.Serializable) != 0 && !IsUnityObject(type) &&
                   BaseName(type) != "System.Enum";
        }

        bool IsUnityObject(string type)
        {
            for (string? current = type; current != null; current = BaseName(current))
                if (s_UnityObjects.Contains(current.Split('|')[^1]))
                    return true;
            return false;
        }

        string? BaseName(string type)
        {
            if (!m_Types.TryGetValue(type, out var entry))
                return null;
            TypeDefinition definition = entry.Reader.GetTypeDefinition(entry.Handle);
            return definition.BaseType.IsNil ? null : new TypeNames(entry.Reader, entry.Assembly).Name(definition.BaseType);
        }

        // [SerializeReference] 的实际对象可以是声明类型的任一可序列化实现，全部纳入比较。
        IEnumerable<string> Implementations(string declared)
        {
            foreach ((string type, var entry) in m_Types)
            {
                TypeDefinition definition = entry.Reader.GetTypeDefinition(entry.Handle);
                if ((definition.Attributes & TypeAttributes.Serializable) == 0 || IsUnityObject(type))
                    continue;
                var names = new TypeNames(entry.Reader, entry.Assembly);
                bool assignable = type == declared ||
                                  definition.GetInterfaceImplementations().Any(handle =>
                                      names.Name(entry.Reader.GetInterfaceImplementation(handle).Interface) == declared);
                for (string? current = BaseName(type); !assignable && current != null; current = BaseName(current))
                    assignable = current == declared;
                if (assignable)
                    yield return type;
            }
        }

        static IEnumerable<string> Elements(string type)
        {
            int open = type.IndexOf('<');
            if (open < 0)
            {
                yield return type.TrimEnd('[', ']', ',');
                yield break;
            }
            int depth = 0, start = open + 1;
            for (int index = open + 1; index < type.Length; index++)
            {
                char character = type[index];
                if (character == '<') depth++;
                else if (character == '>' && depth-- == 0 || character == ',' && depth == 0)
                {
                    foreach (string element in Elements(type[start..index]))
                        yield return element;
                    start = index + 1;
                }
            }
        }

        static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string name)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                EntityHandle type = attribute.Constructor.Kind == HandleKind.MemberReference
                    ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
                    : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                string full = type.Kind == HandleKind.TypeReference
                    ? Qualified(reader, reader.GetTypeReference((TypeReferenceHandle)type))
                    : FullName(reader, (TypeDefinitionHandle)type);
                if (full == name)
                    return true;
            }
            return false;
        }
    }

    static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        TypeDefinitionHandle declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
            return FullName(reader, declaring) + "+" + name;
        string space = reader.GetString(definition.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    static string Qualified(MetadataReader reader, TypeReference reference)
    {
        string name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return Qualified(reader, reader.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope)) + "+" + name;
        string space = reader.GetString(reference.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    // 类型名带程序集前缀（项目类型可与 Catalog 键直接对照），泛型实参与数组保留在名字中，字段类型变化即布局变化。
    sealed class TypeNames(MetadataReader reader, string assembly) : ISignatureTypeProvider<string, object?>
    {
        public string Name(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
            _ => handle.Kind.ToString()
        };

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
        public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind) =>
            assembly + "|" + FullName(metadata, handle);

        public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference reference = metadata.GetTypeReference(handle);
            EntityHandle scope = reference.ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
                scope = metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
            string owner = scope.Kind == HandleKind.AssemblyReference
                ? metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                : assembly;
            string full = Qualified(metadata, reference);
            return s_UnityObjects.Contains(full) || full.StartsWith("System.", StringComparison.Ordinal) ? full : owner + "|" + full;
        }

        public string GetTypeFromSpecification(MetadataReader metadata, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            metadata.GetTypeSpecification(handle).DecodeSignature(this, context);

        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
