using BTSMTL.Authoring.Workspace;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

// 项目对作者文档的解释：根配置类型、文档内声明的元素、保存前的规范化与变量类型候选。工作台只经此取得类型身份与字段合同。
public interface IAuthoringDocumentAdapter
{
    IReadOnlyList<AuthoringDocumentElement> DeclaredElements(AuthoringDocumentSnapshot owner);

    Type? AuthoringType(AuthoringDocumentSnapshot document);

    // 随文档正文给出的存储形状；不需要形状的类型返回 null。
    JToken? StorageShape(Type authoringType);

    // 保存前把编辑视图中的写入换成项目规范化后的文件（例如图编译后的正式文本）；不需要时原样返回。
    AuthoringDocumentWrite[] PrepareSave(string workspace, AuthoringDocumentWrite[] writes,
        IReadOnlyList<AuthoringDocumentSnapshot> documents, AuthoringDocumentEditingView request);

    JToken VariableTypeChoices();
}

public sealed record AuthoringReferenceCapture(IReadOnlyList<AuthoringReferenceDocument> Documents,
    IReadOnlyDictionary<string, string> SchemaInputs);

// 项目对资源引用的解释：从作者文档中取出带领域的资源引用，供工作台的引用替换使用。
public interface IAuthoringResourceAdapter
{
    AuthoringReferenceCapture CaptureReferences(string workspace, AuthoringCatalogSnapshot catalog);
}
