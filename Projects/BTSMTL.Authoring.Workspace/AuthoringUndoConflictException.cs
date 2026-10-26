namespace BTSMTL.Authoring.Workspace;

public sealed class AuthoringUndoConflictException(string documentId, string elementId, string field,
    string expected, string current) : InvalidOperationException(
        $"文档 '{documentId}' 的对象 '{elementId}' 在 '{field}' 上已有后续修改，不能覆盖为旧内容。")
{
    public string DocumentId { get; } = documentId;
    public string ElementId { get; } = elementId;
    public string Field { get; } = field;
    public string Expected { get; } = expected;
    public string Current { get; } = current;
}
