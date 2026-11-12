using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

static class WorkbenchErrors
{
    // 格式错误逐项写出所在文档、元素与字段，作者能直接看到要改哪一处。
    public static string Explain(Exception error) => error is AuthoringJsonFormatException format
        ? $"{format.Message}{string.Concat(format.Errors.Select(item => $"\n· {Location(item)}：{(string?)item["message"]}"))}"
        : error.Message;

    static string Location(Newtonsoft.Json.Linq.JObject item)
    {
        string? element = (string?)item["element"];
        string? file = (string?)item["file"];
        string? owner = (string?)item["skill"];
        string place = element ?? (owner is null ? file : file is null ? owner : $"{owner}/{file}") ?? "未知位置";
        string? node = (string?)item["nodeId"];
        string? field = (string?)item["fieldId"] ?? (string?)item["path"];
        return string.Join(" · ", new[] { place, node is null ? null : $"节点 {node}", string.IsNullOrEmpty(field) ? null : field }
            .Where(part => part is not null));
    }
}
