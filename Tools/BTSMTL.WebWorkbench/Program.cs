using BTSMTL.WebWorkbench;

var app = WorkbenchApplication.Create(args);
string workspace = app.Configuration["Workspace"] ?? throw new ArgumentException("请指定 --Workspace 作者工作区。");
var authoring = new StandaloneAuthoring(Path.GetFullPath(workspace));
app.MapAuthoringApi(authoring.Documents, projectTools: false);
app.MapAuthoringEditorApi(authoring.Document, authoring.Types, authoring.Type, authoring.Fields, authoring.Ports,
    authoring.Configuration, authoring.Elements);
var preview = new StandalonePreview(authoring.Documents);
app.MapPreviewApi(preview);
app.MapPreviewQuickApi(preview);
app.MapPreviewImageApi(null);
app.Run();
