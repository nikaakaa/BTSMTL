using System;

namespace BTSMTL.Authoring.Workspace
{

public sealed class AuthoringUndoConflictException : InvalidOperationException
{
    public AuthoringUndoConflictException(string documentId, string elementId, string field,
        string expected, string current) : base(
        $"文档 '{documentId}' 的对象 '{elementId}' 在 '{field}' 上已有后续修改，不能覆盖为旧内容。")
    {
        DocumentId = documentId; ElementId = elementId; Field = field; Expected = expected; Current = current;
    }
    public string DocumentId { get; }
    public string ElementId { get; }
    public string Field { get; }
    public string Expected { get; }
    public string Current { get; }
}
}
