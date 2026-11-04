using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BTSMTL.Diagnostics;
using ThirdPersonSimulation;

namespace BTSMTL.Preview
{
    public sealed class PreviewRecordingArchive
    {
        static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false);
        readonly string m_LayoutPath;
        RuntimeDiagnosticsStore[] m_DiagnosticStores;
        RuntimeCaptureFileArchive[] m_DiagnosticArchives;

        public PreviewRecordingArchive(string directory, string manifestFile, string layoutFile)
        {
            if (!Path.IsPathRooted(directory))
                throw new InvalidDataException("预览录制需要绝对输出目录。");
            DirectoryPath = Path.GetFullPath(directory);
            Directory.CreateDirectory(DirectoryPath);
            ManifestPath = Path.Combine(DirectoryPath, manifestFile);
            m_LayoutPath = Path.Combine(DirectoryPath, layoutFile);
        }

        public string DirectoryPath { get; }
        public string ManifestPath { get; }
        public RecordedFrameArchive Frames { get; private set; }
        public RecordedFrameArchive Images { get; private set; }
        public int DiagnosticCount { get; private set; }
        public bool LayoutWritten { get; private set; }
        public bool Closed { get; private set; }

        public void Initialize(Func<string> serializeManifest)
        {
            using var file = new FileStream(ManifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(file, s_Utf8);
            writer.Write(serializeManifest());
        }

        public string WriteInput(string relativePath, byte[] bytes, out string revision)
        {
            string path = Path.Combine(DirectoryPath, relativePath);
            using SHA256 hash = SHA256.Create();
            revision = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(bytes, 0, bytes.Length);
            return path;
        }

        public void PrepareFrames(string relativeDirectory, int pageCapacity, int pageCount) =>
            Frames = new RecordedFrameArchive(Path.Combine(DirectoryPath, relativeDirectory), pageCapacity, pageCount, true);

        public void PrepareImages(string relativeDirectory, int pageCapacity, int pageCount) =>
            Images = new RecordedFrameArchive(Path.Combine(DirectoryPath, relativeDirectory), pageCapacity, pageCount, true);

        public void PrepareDiagnostics(int capacity)
        {
            m_DiagnosticStores = new RuntimeDiagnosticsStore[capacity];
            m_DiagnosticArchives = new RuntimeCaptureFileArchive[capacity];
        }

        public void BeginDiagnostics(string relativeDirectory, RuntimeDiagnosticsStore store, RuntimeTraceChannel channels)
        {
            var archive = new RuntimeCaptureFileArchive(Path.Combine(DirectoryPath, relativeDirectory), true);
            m_DiagnosticStores[DiagnosticCount] = store;
            m_DiagnosticArchives[DiagnosticCount++] = archive;
            store.SetCaptureArchive(archive);
            if (!store.BeginCapture(channels, RuntimeDiagnosticsCaptureDetail.Continuous, out _))
                throw new InvalidOperationException("预览对象诊断源未接受录制。");
        }

        public RuntimeCaptureFileArchive DiagnosticArchive(int index) => m_DiagnosticArchives[index];

        public void Complete(Action detachSources, Action<int, RuntimeCaptureFileArchive> diagnosticCompleted)
        {
            Closed = true;
            detachSources();
            for (int i = 0; i < DiagnosticCount; i++)
            {
                m_DiagnosticStores[i].EndCapture();
                RuntimeCaptureFileArchive archive = m_DiagnosticArchives[i];
                archive.Complete();
                diagnosticCompleted(i, archive);
            }
            Frames?.Release();
            Images?.Release();
        }

        public void SaveLayout(string json)
        {
            File.WriteAllText(m_LayoutPath, json, s_Utf8);
            LayoutWritten = true;
        }

        public void SaveManifest(string json)
        {
            string pending = ManifestPath + ".writing";
            File.WriteAllText(pending, json, s_Utf8);
            File.Replace(pending, ManifestPath, null);
        }
    }
}
