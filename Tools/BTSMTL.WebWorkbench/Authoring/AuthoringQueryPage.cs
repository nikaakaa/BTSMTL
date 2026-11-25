using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public static class AuthoringQueryPage
{
    public static void Require(int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 128)
            throw new ArgumentOutOfRangeException(nameof(limit), "查询需要 offset>=0，limit 为 1—128。");
    }

    public static JObject Slice(IEnumerable<JObject> source, int offset, int limit)
    {
        JObject[] items = source.ToArray();
        return new JObject
        {
            ["offset"] = offset, ["limit"] = limit, ["total"] = items.Length,
            ["hasMore"] = offset + limit < items.Length, ["items"] = new JArray(items.Skip(offset).Take(limit))
        };
    }
}
