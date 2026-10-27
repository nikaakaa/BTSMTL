using System;
using NodeCanvas.Framework;

namespace BTSMTL.Graphs
{
    public static class BtsmtlBlackboardUnityDeclaration
    {
        public static void Validate(this BtsmtlBlackboardDeclaration declaration, Variable variable)
        {
            if (variable == null || !variable.hasStableIdentity || variable.ID != declaration.VariableId ||
                variable.isDataBound || variable.isPropertyBound || variable is not ISerializedVariableValue)
                throw new InvalidOperationException("黑板变量必须有稳定身份，且不能绑定原生运行getter或对象属性。");
            BtsmtlBlackboardDeclaration.ValidateDefinition(BtsmtlGraphValueTypes.Kind(variable.varType),
                Type.GetTypeCode(variable.varType), declaration.Scope, declaration.Lifetime, declaration.InputBinding);
        }
    }
}
