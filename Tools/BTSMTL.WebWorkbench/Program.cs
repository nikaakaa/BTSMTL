using System.Text.Json.Serialization;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using BTSMTL.WebWorkbench;
using BTSMTL.Authoring.Workspace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

string workspacePath = builder.Configuration["Workspace"]
    ?? throw new InvalidOperationException("请用 --Workspace 指定项目正式作者文档目录。");
app.MapWorkspaceApi(new AuthoringWorkspaceDirectory(workspacePath));

app.MapGet("/api/core", () => new
{
    name = "BTSMTL",
    compiler = typeof(BtsmtlGraphSemanticBuilder).Assembly.GetName().Name,
    runtime = typeof(OrchestrationProgram<>).Assembly.GetName().Name,
    contract = ThirdPersonSimulation.GraphNodeSemanticContract.Identity,
    operations = GraphCompiler.EditableOperations.Select(code => new
    {
        code = code.ToString(),
        inputs = OrchestrationValuePortContracts.Require(code).Inputs.Select(input => new
        {
            id = input.Identity,
            constraint = input.Constraint.ToString(),
            acceptedKinds = Enum.GetValues<ThirdPersonSimulation.SemanticValueKind>()
                .Where(input.Accepts).Select(kind => kind.ToString())
        })
    })
});

app.MapPost("/api/compile", (GraphRequest request) => GraphCompiler.Compile(request));
app.Run();
