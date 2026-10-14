using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Luban;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Configuration
{
    public sealed class GameplayConfigurationData
    {
        readonly Dictionary<(GameplayConfigRecordKind, string), GameplayConfigRecordSource> m_Sources;

        public GameplayConfigurationData(Func<string, byte[]> loader)
        {
            var reader = new CanonicalReader(loader(GameplayConfigProtocol.MetadataFile));
            GameplayConfigMetadata.CanonicalRequireSchema(reader);
            Metadata = GameplayConfigMetadata.CanonicalReadMaterialized(reader);
            reader.RequireComplete();
            if (Metadata.Protocol != GameplayConfigProtocol.Version || Metadata.SchemaIdentity != GameplayConfigGeneratedContract.SchemaIdentity)
                throw new InvalidDataException("Gameplay 配置数据与已编译 reader 合同不匹配：" + Metadata.SourceId);
            var files = new Dictionary<string, byte[]>(Metadata.Files.Count, StringComparer.Ordinal);
            foreach (GameplayConfigDataFile file in Metadata.Files)
            {
                byte[] bytes = loader(file.Name);
                using SHA256 hash = SHA256.Create();
                string identity = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
                if (identity != file.Hash)
                    throw new InvalidDataException("Gameplay 配置文件与生成版本不匹配：" + Metadata.SourceId + "/" + file.Name);
                files.Add(file.Name, bytes);
            }
            Tables = new GameplayTables(name => new ByteBuf(files[name]));
            m_Sources = new Dictionary<(GameplayConfigRecordKind, string), GameplayConfigRecordSource>(Metadata.Records.Count);
            foreach (GameplayConfigRecordSource record in Metadata.Records)
                m_Sources.Add((GameplayConfigFields.RecordKind(record.Table), record.RecordId), record);
        }

        public GameplayConfigMetadata Metadata { get; }
        public GameplayTables Tables { get; }
        public GameplayConfigRecordSource Source(GameplayConfigRecordKind kind, string recordId) => m_Sources[(kind, recordId)];
        public bool Contains(GameplayConfigRecordKind kind, string recordId) => m_Sources.ContainsKey((kind, recordId));
    }
}
