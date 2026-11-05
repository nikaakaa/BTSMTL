using System;

namespace BTSMTL.Authoring
{
    public enum BtsmtlBusinessKeyRole { Declaration = 0, Reference = 1 }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = true, Inherited = false)]
    public sealed class BtsmtlBusinessKeyAttribute : Attribute
    {
        public BtsmtlBusinessKeyAttribute(string domain, string keyKind, BtsmtlBusinessKeyRole role)
        {
            Domain = domain;
            KeyKind = keyKind;
            Role = role;
        }

        public BtsmtlBusinessKeyAttribute(string domain, BtsmtlBusinessKeyRole role)
        {
            Domain = domain;
            Role = role;
        }

        public string Domain { get; }
        public string KeyKind { get; }
        public BtsmtlBusinessKeyRole Role { get; }
        public string KindParameter { get; set; }
        public Type KindType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeyKindAttribute : Attribute
    {
        public BtsmtlBusinessKeyKindAttribute(string domain, string keyKind)
        {
            Domain = domain;
            KeyKind = keyKind;
        }

        public string Domain { get; }
        public string KeyKind { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeyExternalParameterAttribute : Attribute
    {
        public BtsmtlBusinessKeyExternalParameterAttribute(Type declaringType, string methodName, string parameterName,
            string domain, string keyKind, BtsmtlBusinessKeyRole role, params Type[] parameterTypes)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
            ParameterName = parameterName;
            Domain = domain;
            KeyKind = keyKind;
            Role = role;
            ParameterTypes = Array.AsReadOnly(parameterTypes);
        }

        public Type DeclaringType { get; }
        public string MethodName { get; }
        public string ParameterName { get; }
        public string Domain { get; }
        public string KeyKind { get; }
        public BtsmtlBusinessKeyRole Role { get; }
        public System.Collections.Generic.IReadOnlyList<Type> ParameterTypes { get; }
        public string KindParameter { get; set; }
        public Type KindType { get; set; }
    }

}
