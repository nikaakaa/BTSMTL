using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Configuration
{
    public enum GameplayConfigValueKind : byte
    {
        Unsupported,
        Boolean,
        Int32,
        UInt64,
        Number,
        Text
    }

    public readonly struct GameplayConfigValue
    {
        GameplayConfigValue(GameplayConfigValueKind kind, bool boolean, int int32, ulong uint64, float number, string text)
        {
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            Number = number;
            Text = text;
        }

        public GameplayConfigValueKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public float Number { get; }
        public string Text { get; }
        public static GameplayConfigValue FromBoolean(bool value) => new GameplayConfigValue(GameplayConfigValueKind.Boolean, value, 0, 0, 0, null);
        public static GameplayConfigValue FromInt32(int value) => new GameplayConfigValue(GameplayConfigValueKind.Int32, false, value, 0, 0, null);
        public static GameplayConfigValue FromUInt64(ulong value) => new GameplayConfigValue(GameplayConfigValueKind.UInt64, false, 0, value, 0, null);
        public static GameplayConfigValue FromNumber(float value) => new GameplayConfigValue(GameplayConfigValueKind.Number, false, 0, 0, value, null);
        public static GameplayConfigValue FromText(string value) => new GameplayConfigValue(GameplayConfigValueKind.Text, false, 0, 0, 0, value);
    }

    public readonly struct GameplayConfigFieldDescription
    {
        public GameplayConfigFieldDescription(string identity, string contract, string path, string type, GameplayConfigValueKind valueKind)
        {
            Identity = identity;
            Contract = contract;
            Path = path;
            Type = type;
            ValueKind = valueKind;
        }

        public string Identity { get; }
        public string Contract { get; }
        public string Path { get; }
        public string Type { get; }
        public GameplayConfigValueKind ValueKind { get; }
        public bool CanRead => ValueKind != GameplayConfigValueKind.Unsupported;
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct GameplayConfigRecordSource
    {
        public GameplayConfigRecordSource(string table, string recordId, string workbook, string sheet, int row, string contentIdentity)
        {
            Table = table;
            RecordId = recordId;
            Workbook = workbook;
            Sheet = sheet;
            Row = row;
            ContentIdentity = contentIdentity;
        }

        public string Table { get; }
        public string RecordId { get; }
        public string Workbook { get; }
        public string Sheet { get; }
        public int Row { get; }
        public string ContentIdentity { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct GameplayConfigDataFile
    {
        public GameplayConfigDataFile(string name, string hash)
        {
            Name = name;
            Hash = hash;
        }

        public string Name { get; }
        public string Hash { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayConfigMetadata
    {
        public GameplayConfigMetadata(int protocol, string sourceId, string schemaIdentity, string contentIdentity,
            IReadOnlyList<GameplayConfigRecordSource> records, IReadOnlyList<GameplayConfigDataFile> files)
        {
            Protocol = protocol;
            SourceId = sourceId;
            SchemaIdentity = schemaIdentity;
            ContentIdentity = contentIdentity;
            Records = records;
            Files = files;
        }

        public int Protocol { get; }
        public string SourceId { get; }
        public string SchemaIdentity { get; }
        public string ContentIdentity { get; }
        public IReadOnlyList<GameplayConfigRecordSource> Records { get; }
        public IReadOnlyList<GameplayConfigDataFile> Files { get; }
    }

    public static class GameplayConfigProtocol
    {
        public const int Version = 1;
        public const string MetadataFile = "gameplay-config.identity";
    }
}
