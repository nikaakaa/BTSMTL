using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed record UpdatePlayerCodeRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string PlayerBuildId);
