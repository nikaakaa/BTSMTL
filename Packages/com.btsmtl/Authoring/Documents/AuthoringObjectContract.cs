using System;
using System.Linq;
using System.Reflection;

namespace BTSMTL.Authoring.Documents
{
    public enum AuthoringObjectConstruction
    {
        PublicFields,
        Constructor,
        Configure
    }

    public sealed class AuthoringObjectContract
    {
        AuthoringObjectContract(Type valueType, AuthoringObjectConstruction construction,
            FieldInfo[] fields, ConstructorInfo constructor, MethodInfo configure, ParameterInfo[] parameters)
        {
            ValueType = valueType;
            Construction = construction;
            Fields = fields;
            Constructor = constructor;
            Configure = configure;
            Parameters = parameters;
        }

        public Type ValueType { get; }
        public AuthoringObjectConstruction Construction { get; }
        public FieldInfo[] Fields { get; }
        public ConstructorInfo Constructor { get; }
        public MethodInfo Configure { get; }
        public ParameterInfo[] Parameters { get; }

        public static AuthoringObjectContract Read(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            ConstructorInfo[] constructors = type.GetConstructors()
                .Where(constructor => constructor.GetParameters().Length != 0).ToArray();
            MethodInfo configure = constructors.Length == 0 ? type.GetMethod("Configure") : null;
            if (type.IsValueType && fields.Length != 0 ||
                constructors.Length == 0 && configure == null && type.GetConstructor(Type.EmptyTypes) != null)
                return new AuthoringObjectContract(type, AuthoringObjectConstruction.PublicFields,
                    fields.OrderBy(field => field.MetadataToken).ToArray(), null, null, Array.Empty<ParameterInfo>());
            if (constructors.Length != 0)
            {
                int count = constructors.Max(constructor => constructor.GetParameters().Length);
                ConstructorInfo constructor = constructors.Single(candidate => candidate.GetParameters().Length == count);
                return new AuthoringObjectContract(type, AuthoringObjectConstruction.Constructor,
                    Array.Empty<FieldInfo>(), constructor, null, constructor.GetParameters());
            }
            if (configure == null)
                throw new InvalidOperationException($"正式值类型'{type.FullName}'没有公开构造或Configure合同。");
            return new AuthoringObjectContract(type, AuthoringObjectConstruction.Configure,
                Array.Empty<FieldInfo>(), null, configure, configure.GetParameters());
        }

        public static PropertyInfo ReadableProperty(Type type, ParameterInfo parameter)
        {
            string key = MemberKey(parameter.Name);
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .SingleOrDefault(property => string.Equals(MemberKey(property.Name), key, StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidOperationException($"作者值 '{type.FullName}' 的参数 '{parameter.Name}' 缺少公开读取成员。");
        }

        static string MemberKey(string name) => name.StartsWith("m_", StringComparison.Ordinal) ? name.Substring(2) :
            name.StartsWith("value_", StringComparison.Ordinal) ? name.Substring(6) : name;
    }
}
