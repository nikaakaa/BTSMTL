using System.Collections.Generic;
using System.Linq;
using BTSMTL.Graphs;

namespace BTSMTL.Compilation
{
    public static class BtsmtlGraphVariableCompilation
    {
        public static IReadOnlyList<BtsmtlBlackboardDeclarationSource> Declarations(
            BtsmtlGraphVariableDeclarations source, string route, string contentHash)
        {
            return source.Variables.OrderBy(variable => variable.VariableId, System.StringComparer.Ordinal).Select(variable =>
            {
                BtsmtlBlackboardDeclaration declaration = variable.Declaration;
                return new BtsmtlBlackboardDeclarationSource(source.OwnerId, typeof(BtsmtlGraphVariableDeclaration).FullName,
                    contentHash, variable, new BtsmtlBlackboardDeclarationSnapshot(variable.VariableId, variable.Name,
                        variable.ValueType, declaration.Scope, declaration.Lifetime, declaration.Category,
                        BtsmtlGraphVariableDeclarationsJson.ReadDefault(variable.ValueType, variable.DefaultValue), declaration.InputBinding?.InputValueId,
                        declaration.InputBinding?.Projection ?? default), route);
            }).ToArray();
        }
    }
}
