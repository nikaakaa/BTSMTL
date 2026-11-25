using System;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Compilation;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench
{
    public static class AuthoringVariableChoices
    {
        public static JObject Describe(IEnumerable<Type> declaredEnums,
            IEnumerable<PipelineBlackboardInputProjection> inputProjections)
        {
            Type[] enums = declaredEnums.OrderBy(type => type.AssemblyQualifiedName,
                StringComparer.Ordinal).ToArray();
            return new JObject
            {
                ["types"] = new JArray(CompileValueTypes.WithEnums(enums).Select(type => new JObject
                {
                    ["typeId"] = CompileValueTypes.Id(type), ["clrType"] = type.FullName,
                    ["valueKind"] = type.Kind.ToString(),
                    ["defaultValue"] = BtsmtlGraphVariableDeclarationsJson.CreateDefault(type),
                    ["enumMembers"] = type.IsEnum ? EnumMembers(enums.Single(declared =>
                        declared.AssemblyQualifiedName == type.EnumIdentity)) : null
                })),
                ["scopes"] = new JArray(Enum.GetValues<PipelineBlackboardVariableScope>().Select(scope => new JObject
                {
                    ["name"] = scope.ToString(), ["value"] = (int)scope,
                    ["defaultLifetime"] = (int)PipelineBlackboardVariablePolicy.DefaultLifetime(scope),
                    ["lifetimes"] = new JArray(Enum.GetValues<PipelineBlackboardVariableLifetime>()
                        .Where(lifetime => PipelineBlackboardVariablePolicy.IsValid(scope, lifetime)).Select(lifetime => (int)lifetime))
                })),
                ["lifetimes"] = EnumMembers(typeof(PipelineBlackboardVariableLifetime)),
                ["inputProjections"] = new JArray(inputProjections.Select(projection => new JObject
                {
                    ["name"] = projection.ToString(), ["value"] = (int)projection,
                    ["objectKind"] = PipelineBlackboardInputProjections.ObjectKind(projection)?.Id
                })),
                ["objectKinds"] = new JArray(OrchestrationObjectKinds.All.OrderBy(kind => kind.Id, StringComparer.Ordinal)
                    .Select(kind => new JObject
                    {
                        ["id"] = kind.Id, ["displayName"] = kind.DisplayName, ["parent"] = kind.Parent?.Id
                    }))
            };
        }

        static JArray EnumMembers(Type type) => new JArray(Enum.GetNames(type).Select(name => new JObject
        {
            ["name"] = name,
            ["value"] = Convert.ToInt32(Enum.Parse(type, name), CultureInfo.InvariantCulture)
        }));
    }
}
