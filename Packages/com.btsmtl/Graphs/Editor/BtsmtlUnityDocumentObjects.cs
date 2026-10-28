using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using UnityEditor;

namespace BTSMTL.Graphs.Editor
{
    public sealed class BtsmtlUnityDocumentObjects
    {
        readonly Dictionary<(string Root, string Document, string Element), UnityEngine.Object> m_Objects = new();
        readonly Dictionary<UnityEngine.Object, AuthoringContentReference> m_References = new();

        public BtsmtlUnityDocumentObjects(AuthoringRootDocument root)
        {
            var documents = root.Files.ToArray().Select(file => file.Id).ToHashSet();
            documents.Add(root.Id);
            foreach (var group in root.UnityOutputs.ToArray().GroupBy(output => output.Guid, StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(group.Key);
                var assets = new Dictionary<long, UnityEngine.Object>();
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long localId);
                    assets.Add(localId, asset);
                }
                foreach (AuthoringUnityOutput output in group)
                {
                    if (!assets.TryGetValue(output.LocalFileId, out UnityEngine.Object asset))
                        throw new InvalidOperationException($"作者文档 '{output.DocumentId.Value}' 的 Unity 对象缺失：GUID {output.Guid}，local file ID {output.LocalFileId}，原路径 '{output.AssetPath}'。必须恢复原引用对象后再派生。");
                    var reference = new AuthoringContentReference(documents.Contains(output.DocumentId) ? root.Id :
                        new GraphAuthoringElementId(output.Guid), output.DocumentId, output.ElementId);
                    m_Objects.Add(Key(reference), asset);
                    m_References.Add(asset, reference);
                }
            }
        }

        public AuthoringContentReference Identify(UnityEngine.Object asset) => m_References[asset];

        public UnityEngine.Object Resolve(Type type, AuthoringContentReference reference)
        {
            UnityEngine.Object asset = m_Objects[Key(reference)];
            if (!type.IsInstanceOfType(asset))
                throw new InvalidOperationException($"作者引用 '{reference.DocumentId.Value}/{reference.ElementId.Value}' 的 Unity 类型为 '{asset.GetType().FullName}'，字段要求 '{type.FullName}'。");
            return asset;
        }

        public static AuthoringUnityOutput Capture(GraphAuthoringElementId documentId,
            GraphAuthoringElementId elementId, UnityEngine.Object asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                throw new InvalidOperationException($"作者文档 '{documentId.Value}' 的 Unity 表示必须是已保存对象：'{asset.name}'。");
            return new AuthoringUnityOutput(documentId, elementId, AssetDatabase.GetAssetPath(asset), guid, localId);
        }

        static (string Root, string Document, string Element) Key(AuthoringContentReference reference) =>
            (reference.RootId.Value, reference.DocumentId.Value, reference.ElementId.Value);
    }
}
