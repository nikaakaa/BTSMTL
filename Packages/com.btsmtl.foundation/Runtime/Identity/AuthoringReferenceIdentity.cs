namespace BTSMTL.Authoring
{
    // 作者内容引用的对象不是引擎磁盘资产（如由正式文档在内存中投影）时，由对象自身给出稳定身份与内容修订。
    public interface IAuthoringReferenceIdentity
    {
        string AuthoringReferenceIdentity { get; }
    }
}
