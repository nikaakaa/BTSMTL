using System.Collections.Generic;
using NodeCanvas.Framework;

namespace BTSMTL
{
    public interface IBtsmtlGraphVariableSource
    {
        IBlackboard SourceVariables { get; }
    }

    // 节点快照之外仍参与编译的图数据（如子图接口、身份、宿主准入与局部变量声明）；指纹将其计入调用方身份。
    public interface IBtsmtlGraphCompilationInputs
    {
        void AppendCompilationInputs(List<string> parts);
    }
}
