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

    public enum GameplayConfigSourceLocationKind : byte
    {
        WorkbookRow,
        File
    }

    public readonly partial struct GameplayConfigRecordSource
    {
        public GameplayConfigRecordSource(string table, string recordId, GameplayConfigSourceLocationKind kind,
            string path, string sheet, int row, string contentIdentity)
        {
            bool workbook = kind == GameplayConfigSourceLocationKind.WorkbookRow;
            if (workbook ? string.IsNullOrEmpty(sheet) || row <= 0 : kind != GameplayConfigSourceLocationKind.File || !string.IsNullOrEmpty(sheet) || row != 0)
                throw new ArgumentException("Gameplay 配置来源位置与种类不一致：" + table + "/" + recordId);
            Table = table;
            RecordId = recordId;
            Kind = kind;
            Path = path;
            Sheet = sheet;
            Row = row;
            ContentIdentity = contentIdentity;
        }

        public static GameplayConfigRecordSource WorkbookRow(string table, string recordId, string path, string sheet, int row,
            string contentIdentity) =>
            new GameplayConfigRecordSource(table, recordId, GameplayConfigSourceLocationKind.WorkbookRow, path, sheet, row, contentIdentity);

        public static GameplayConfigRecordSource File(string table, string recordId, string path, string contentIdentity) =>
            new GameplayConfigRecordSource(table, recordId, GameplayConfigSourceLocationKind.File, path, string.Empty, 0, contentIdentity);

        public string Table { get; }
        public string RecordId { get; }
        public GameplayConfigSourceLocationKind Kind { get; }
        public string Path { get; }
        public string Sheet { get; }
        public int Row { get; }
        public string ContentIdentity { get; }

        public string Describe() => Kind == GameplayConfigSourceLocationKind.WorkbookRow ? Path + "#" + Sheet + ":" + Row : Path;
    }

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
        public const int Version = 2;
        public const string MetadataFile = "gameplay-config.identity";
    }
}
