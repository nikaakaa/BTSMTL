#nullable enable
namespace BTSMTL.Authoring.Workspace
{
    public sealed class SkillSchemaError
    {
        public SkillSchemaError(string file, string jsonPath, string code, string message)
        { File = file; JsonPath = jsonPath; Code = code; Message = message; }
        public string File { get; }
        public string JsonPath { get; }
        public string Code { get; }
        public string Message { get; }
    }
}
