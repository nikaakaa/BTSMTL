using BTSMTL.Compilation;

namespace BTSMTL.Graphs.Editor
{
    public interface IBtsmtlGraphNodeCompileSource : IGraphCompileReferences
    {
        GraphNodeCompileDescription Describe(GraphNodeFields fields);
    }
}
