using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackGraphContentFiles
    {
        public static string StructurePath(string manifestPath) => Path.ChangeExtension(manifestPath, "structure.bin");
        public static string ParametersPath(string manifestPath) => Path.ChangeExtension(manifestPath, "parameters.bin");
        public static string SourcesPath(string manifestPath) => Path.ChangeExtension(manifestPath, "sources.bin");

        public static OrchestrationGraphContentBlocks Read(string manifestPath) =>
            ReadBlocks(manifestPath, ReadManifest(manifestPath));

        public static OrchestrationGraphContentBlocks Read(string manifestPath, string expectedContentIdentity)
        {
            GraphExecutionIdentity identity = ReadManifest(manifestPath);
            if (identity.ContentIdentity.Value != expectedContentIdentity)
                throw new InvalidDataException($"图 '{identity.Source.GraphId}' 的清单与发布目录身份不一致。");
            return ReadBlocks(manifestPath, identity);
        }

        static GraphExecutionIdentity ReadManifest(string manifestPath) =>
            MemoryPackGraphContentPublisher.Instance.ReadIdentity(File.ReadAllBytes(manifestPath));

        static OrchestrationGraphContentBlocks ReadBlocks(string manifestPath, GraphExecutionIdentity identity) =>
            new(identity,
                File.ReadAllBytes(StructurePath(manifestPath)), File.ReadAllBytes(ParametersPath(manifestPath)),
                File.ReadAllBytes(SourcesPath(manifestPath)));
    }

    public readonly struct MemoryPackGraphPreparation
    {
        public MemoryPackGraphPreparation(GraphCompilationWork work, double encodingMilliseconds, double milliseconds)
        {
            Work = work;
            EncodingMilliseconds = encodingMilliseconds;
            Milliseconds = milliseconds;
        }

        public GraphCompilationWork Work { get; }
        public double EncodingMilliseconds { get; }
        public double Milliseconds { get; }
    }

    public sealed class MemoryPackGraphFileTransaction
    {
        sealed class Write
        {
            internal string Path;
            internal byte[] Content;
            internal byte[] Previous;
            internal string Staged;
            internal int Order;
            internal bool Shared;
        }

        readonly Dictionary<string, Write> m_Writes = new(StringComparer.Ordinal);

        public IReadOnlyList<string> ChangedFiles => m_Writes.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        public MemoryPackGraphPreparation PrepareGraph<TValue>(string manifestPath, OrchestrationGraphContent<TValue> content,
            IOrchestrationGraphContentEncoding<TValue> encoding) where TValue : unmanaged
        {
            var elapsed = Stopwatch.StartNew();
            GraphExecutionIdentity previous = File.Exists(manifestPath)
                ? encoding.ReadIdentity(File.ReadAllBytes(manifestPath)) : null;
            GraphExecutionIdentity identity = content.Identity;
            if (previous != null && previous.ContentIdentity == identity.ContentIdentity)
                return new MemoryPackGraphPreparation(GraphCompilationWork.Unchanged, 0, elapsed.Elapsed.TotalMilliseconds);
            bool structureChanged = previous == null || previous.StructureIdentity != identity.StructureIdentity;
            bool parametersChanged = previous == null || previous.TargetParameterIdentity != identity.TargetParameterIdentity;
            bool sourcesChanged = previous == null || previous.SourceIdentity != identity.SourceIdentity;
            var encodingTime = Stopwatch.StartNew();
            OrchestrationGraphContentBlocks blocks = encoding.WriteBlocks(content);
            byte[] manifest = encoding.WriteIdentity(identity);
            encodingTime.Stop();
            if (structureChanged)
                Prepare(MemoryPackGraphContentFiles.StructurePath(manifestPath), blocks.Structure, 0);
            if (parametersChanged)
                Prepare(MemoryPackGraphContentFiles.ParametersPath(manifestPath), blocks.Parameters, 0);
            if (sourcesChanged)
                Prepare(MemoryPackGraphContentFiles.SourcesPath(manifestPath), blocks.Sources, 0);
            Prepare(manifestPath, manifest, 1);
            GraphCompilationWork work = structureChanged ? GraphCompilationWork.Structure
                : parametersChanged ? GraphCompilationWork.Parameters : GraphCompilationWork.Sources;
            return new MemoryPackGraphPreparation(work, encodingTime.Elapsed.TotalMilliseconds, elapsed.Elapsed.TotalMilliseconds);
        }

        public void PreparePublication(string path, byte[] content)
        {
            Prepare(path, content, 2);
        }

        public void PrepareSharedContent(string path, byte[] content) => Prepare(path, content, 0, true);

        void Prepare(string path, byte[] content, int order, bool shared = false)
        {
            path = Path.GetFullPath(path);
            if (m_Writes.TryGetValue(path, out Write existing))
            {
                if (!existing.Content.AsSpan().SequenceEqual(content))
                    throw new InvalidDataException($"同一发布事务为 '{path}' 提供了不同内容。");
                return;
            }
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (previous != null && previous.AsSpan().SequenceEqual(content))
                return;
            if (shared && previous != null)
                throw new InvalidDataException($"不可变共享内容 '{path}' 已存在不同字节。");
            m_Writes.Add(path, new Write { Path = path, Content = content, Order = order, Shared = shared, Previous = previous });
        }

        public IReadOnlyList<string> Commit()
        {
            Write[] writes = m_Writes.Values.OrderBy(write => write.Order)
                .ThenBy(write => write.Path, StringComparer.Ordinal).ToArray();
            var committed = new List<Write>();
            try
            {
                foreach (Write write in writes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(write.Path));
                    write.Staged = write.Path + ".publishing-" + Guid.NewGuid().ToString("N");
                    File.WriteAllBytes(write.Staged, write.Content);
                }
                foreach (Write write in writes)
                {
                    if (write.Shared)
                    {
                        try
                        {
                            File.Move(write.Staged, write.Path);
                        }
                        catch (IOException) when (File.Exists(write.Path))
                        {
                            if (!File.ReadAllBytes(write.Path).AsSpan().SequenceEqual(write.Content))
                                throw new InvalidDataException($"并发发布的共享内容 '{write.Path}' 包含不同字节。");
                            continue;
                        }
                    }
                    else if (write.Previous == null)
                        File.Move(write.Staged, write.Path);
                    else
                        File.Replace(write.Staged, write.Path, null);
                    committed.Add(write);
                }
            }
            catch (Exception publicationError)
            {
                var errors = new List<Exception> { publicationError };
                for (int index = committed.Count - 1; index >= 0; index--)
                    try
                    {
                        Write write = committed[index];
                        if (write.Shared)
                            continue;
                        if (write.Previous == null)
                            File.Delete(write.Path);
                        else
                        {
                            write.Staged = write.Path + ".publishing-" + Guid.NewGuid().ToString("N");
                            File.WriteAllBytes(write.Staged, write.Previous);
                            File.Replace(write.Staged, write.Path, null);
                        }
                    }
                    catch (Exception recoveryError)
                    {
                        errors.Add(recoveryError);
                    }
                if (errors.Count != 1)
                    throw new AggregateException("图发布失败，部分文件恢复也未完成。", errors);
                throw;
            }
            finally
            {
                foreach (Write write in writes)
                    if (write.Staged != null && File.Exists(write.Staged))
                        File.Delete(write.Staged);
            }
            return committed.Select(write => write.Path).ToArray();
        }
    }
}
