using System;

namespace BTSMTL.Authoring
{
    public enum BtsmtlBusinessKeyRole { Declaration = 0, Reference = 1 }

    public enum BtsmtlBusinessKeyResolution { Constant = 0, Unresolved = 1 }

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
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class BtsmtlBusinessKeyMetadataAttribute : Attribute
    {
        public const int CurrentVersion = 2;
        public const string SourceTextRevisionAlgorithm = "sha256-utf16le/1";

        public BtsmtlBusinessKeyMetadataAttribute(int version, string assemblyIdentity, int sourceFileCount,
            int declarationCount, int sourceCount, int constantCount, int constantUseCount)
        {
            Version = version;
            AssemblyIdentity = assemblyIdentity;
            SourceFileCount = sourceFileCount;
            DeclarationCount = declarationCount;
            SourceCount = sourceCount;
            ConstantCount = constantCount;
            ConstantUseCount = constantUseCount;
        }

        public int Version { get; }
        public string AssemblyIdentity { get; }
        public int SourceFileCount { get; }
        public int DeclarationCount { get; }
        public int SourceCount { get; }
        public int ConstantCount { get; }
        public int ConstantUseCount { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeySourceFileAttribute : Attribute
    {
        public BtsmtlBusinessKeySourceFileAttribute(string filePath, string sourceTextRevision)
        {
            FilePath = filePath;
            SourceTextRevision = sourceTextRevision;
        }

        public string FilePath { get; }
        public string SourceTextRevision { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeySourceAttribute : Attribute
    {
        public BtsmtlBusinessKeySourceAttribute(string domain, string keyKind, BtsmtlBusinessKeyRole role,
            string bindingAssemblyIdentity, string bindingSymbol, string rootSymbol, string containingSymbol,
            string filePath, string sourceTextRevision, int spanStart, int spanLength,
            BtsmtlBusinessKeyResolution resolution, string keyValue, string failure)
        {
            Domain = domain;
            KeyKind = keyKind;
            Role = role;
            BindingAssemblyIdentity = bindingAssemblyIdentity;
            BindingSymbol = bindingSymbol;
            RootSymbol = rootSymbol;
            ContainingSymbol = containingSymbol;
            FilePath = filePath;
            SourceTextRevision = sourceTextRevision;
            SpanStart = spanStart;
            SpanLength = spanLength;
            Resolution = resolution;
            KeyValue = keyValue;
            Failure = failure;
        }

        public string Domain { get; }
        public string KeyKind { get; }
        public BtsmtlBusinessKeyRole Role { get; }
        public string BindingAssemblyIdentity { get; }
        public string BindingSymbol { get; }
        public string RootSymbol { get; }
        public string ContainingSymbol { get; }
        public string FilePath { get; }
        public string SourceTextRevision { get; }
        public int SpanStart { get; }
        public int SpanLength { get; }
        public BtsmtlBusinessKeyResolution Resolution { get; }
        public string KeyValue { get; }
        public string Failure { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeyConstantAttribute : Attribute
    {
        public BtsmtlBusinessKeyConstantAttribute(string symbol, string rootSymbol, string filePath,
            string sourceTextRevision, int spanStart, int spanLength, string value)
        {
            Symbol = symbol;
            RootSymbol = rootSymbol;
            FilePath = filePath;
            SourceTextRevision = sourceTextRevision;
            SpanStart = spanStart;
            SpanLength = spanLength;
            Value = value;
        }

        public string Symbol { get; }
        public string RootSymbol { get; }
        public string FilePath { get; }
        public string SourceTextRevision { get; }
        public int SpanStart { get; }
        public int SpanLength { get; }
        public string Value { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class BtsmtlBusinessKeyConstantUseAttribute : Attribute
    {
        public BtsmtlBusinessKeyConstantUseAttribute(string filePath, int spanStart, int spanLength,
            string constantAssemblyIdentity, string constantSymbol)
        {
            FilePath = filePath;
            SpanStart = spanStart;
            SpanLength = spanLength;
            ConstantAssemblyIdentity = constantAssemblyIdentity;
            ConstantSymbol = constantSymbol;
        }

        public string FilePath { get; }
        public int SpanStart { get; }
        public int SpanLength { get; }
        public string ConstantAssemblyIdentity { get; }
        public string ConstantSymbol { get; }
    }
}
