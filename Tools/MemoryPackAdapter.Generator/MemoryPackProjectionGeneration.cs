using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        const string SerializationPurposeAttribute = "ThirdPersonSimulation.ValueSerializationPurposeAttribute";
        const string StorageProjectionAttribute = "ThirdPersonSimulation.ValueStorageProjectionAttribute";
        const string StorageSourceAttribute = "ThirdPersonSimulation.ValueStorageSourceAttribute";
        const string StorageContextAttribute = "ThirdPersonSimulation.ValueStorageContextAttribute";

        sealed class StorageProjection
        {
            internal string Purpose;
            internal IMethodSymbol Factory;
            internal Member[] Members;
            internal string[] Accesses;
        }

        static string SerializationPurpose(ISymbol owner)
        {
            for (; owner != null; owner = owner.ContainingSymbol)
            {
                AttributeData attribute = owner.GetAttributes().SingleOrDefault(candidate =>
                    Identity(candidate.AttributeClass) == SerializationPurposeAttribute);
                if (attribute != null) return (string)attribute.ConstructorArguments[0].Value;
            }
            return null;
        }

        static StorageProjection Projection(Compilation compilation, INamedTypeSymbol type, string purpose,
            out string reason)
        {
            reason = null;
            IMethodSymbol[] factories = type.GetMembers().OfType<IMethodSymbol>().Where(method =>
                method.GetAttributes().Any(attribute => Identity(attribute.AttributeClass) == StorageProjectionAttribute &&
                    (string)attribute.ConstructorArguments[0].Value == purpose)).ToArray();
            if (factories.Length == 0) return null;
            if (factories.Length != 1)
            {
                reason = "同一用途须由唯一原构造或工厂声明成员投影";
                return null;
            }
            IMethodSymbol factory = factories[0];
            if (!compilation.IsSymbolAccessibleWithin(factory, compilation.Assembly) ||
                factory.MethodKind != MethodKind.Constructor && (!factory.IsStatic ||
                    !SymbolEqualityComparer.Default.Equals(factory.ReturnType, type)))
            {
                reason = "用途投影须通过可访问的原构造或返回原类型的静态工厂恢复";
                return null;
            }
            IParameterSymbol[] context = factory.Parameters.Where(parameter => HasAttribute(parameter, StorageContextAttribute)).ToArray();
            IParameterSymbol[] parameters = factory.Parameters.Except(context).ToArray();
            var members = new Member[parameters.Length];
            var accesses = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                IParameterSymbol parameter = parameters[i];
                AttributeData source = parameter.GetAttributes().SingleOrDefault(attribute =>
                    Identity(attribute.AttributeClass) == StorageSourceAttribute);
                string path = source == null ? parameter.Name : (string)source.ConstructorArguments[0].Value;
                ITypeSymbol current = type;
                string access = "value";
                foreach (string part in path.Split('.'))
                {
                    ISymbol[] matches = current.GetMembers().Where(member => !member.IsStatic &&
                        string.Equals(MemberKey(member.Name), MemberKey(part), StringComparison.OrdinalIgnoreCase) &&
                        compilation.IsSymbolAccessibleWithin(member, compilation.Assembly) &&
                        (member is IFieldSymbol || member is IPropertySymbol property && !property.IsIndexer &&
                            property.GetMethod != null && compilation.IsSymbolAccessibleWithin(property.GetMethod, compilation.Assembly) ||
                         member is IMethodSymbol method && method.Parameters.All(input =>
                             context.Count(candidate => candidate.Name == input.Name &&
                                 SymbolEqualityComparer.Default.Equals(candidate.Type, input.Type)) == 1))).ToArray();
                    if (matches.Length != 1)
                    {
                        reason = "用途参数 '" + parameter.Name + "' 的原取值路径不唯一或不可访问：" + path;
                        return null;
                    }
                    ISymbol member = matches[0];
                    access += "." + Escape(member.Name);
                    if (member is IMethodSymbol methodSource)
                    {
                        access += "(" + string.Join(", ", methodSource.Parameters.Select(input => ContextAccess(input, "writer"))) + ")";
                        current = methodSource.ReturnType;
                    }
                    else current = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
                }
                if (!SymbolEqualityComparer.Default.Equals(current, parameter.Type))
                {
                    reason = "用途参数 '" + parameter.Name + "' 与原取值路径的真实类型不同：" + path;
                    return null;
                }
                members[i] = new Member { Symbol = parameter, Type = parameter.Type };
                accesses[i] = access;
            }
            return new StorageProjection { Purpose = purpose, Factory = factory, Members = members,
                Accesses = accesses };
        }

        static string ContextAccess(IParameterSymbol parameter, string stream) => "(" + TypeName(parameter.Type) + ")" +
            stream + ".Options.ServiceProvider.GetService(typeof(" + TypeName(parameter.Type) + "))";

        static string ProjectionConstruction(INamedTypeSymbol type, StorageProjection projection) =>
            (projection.Factory.MethodKind == MethodKind.Constructor ? "new " + TypeName(type) :
                TypeName(type) + "." + Escape(projection.Factory.Name)) + "(" +
            string.Join(", ", projection.Factory.Parameters.Select(parameter => HasAttribute(parameter, StorageContextAttribute)
                ? ContextAccess(parameter, "reader") : "member_" + parameter.Name)) + ")";
    }
}
