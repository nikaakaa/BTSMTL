using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public interface IPreviewInputAdapter
{
    JObject Fragment(JsonElement parameters);
    Task<AuthoringQueryResult> Create(string documentPath, JsonElement parameters);
    Task<AuthoringQueryResult> Import(string documentPath, JsonElement parameters);
}
