using BTSMTL.WebWorkbench;

var app = WorkbenchApplication.Create(args);
app.MapPreviewApi();
app.MapPreviewImageApi(null);
app.Run();
