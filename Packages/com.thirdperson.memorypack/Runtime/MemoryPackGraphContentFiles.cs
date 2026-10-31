using System;
using System.Collections.Generic;
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

        public static OrchestrationGraphContentBlocks Read(string manifestPath, string expectedContentIdentity)
        {
            GraphExecutionIdentity identity = MemoryPackGraphContentPublisher.Instance.ReadIdentity(File.ReadAllBytes(manifestPath));
            if (identity.ContentIdentity.Value != expectedContentIdentity)
                throw new InvalidDataException($"图 '{identity.Source.GraphId}' 的清单与发布目录身份不一致。");
            return new OrchestrationGraphContentBlocks(identity,
                File.ReadAllBytes(StructurePath(manifestPath)), File.ReadAllBytes(ParametersPath(manifestPath)),
                File.ReadAllBytes(SourcesPath(manifestPath)));
        }
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
        }

        readonly Dictionary<string, Write> m_Writes = new(StringComparer.Ordinal);

        public IReadOnlyList<string> ChangedFiles => m_Writes.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        public void PrepareGraph<TValue>(string manifestPath, OrchestrationGraphContent<TValue> content,
            IOrchestrationGraphContentEncoding<TValue> encoding) where TValue : unmanaged
        {
            GraphExecutionIdentity previous = File.Exists(manifestPath)
                ? encoding.ReadIdentity(File.ReadAllBytes(manifestPath)) : null;
            GraphExecutionIdentity identity = content.Identity;
            if (previous != null && previous.ContentIdentity == identity.ContentIdentity)
                return;
            OrchestrationGraphContentBlocks blocks = encoding.WriteBlocks(content);
            if (previous == null || previous.StructureIdentity != identity.StructureIdentity)
                Prepare(MemoryPackGraphContentFiles.StructurePath(manifestPath), blocks.Structure, 0);
            if (previous == null || previous.TargetParameterIdentity != identity.TargetParameterIdentity)
                Prepare(MemoryPackGraphContentFiles.ParametersPath(manifestPath), blocks.Parameters, 0);
            if (previous == null || previous.SourceIdentity != identity.SourceIdentity)
                Prepare(MemoryPackGraphContentFiles.SourcesPath(manifestPath), blocks.Sources, 0);
            Prepare(manifestPath, encoding.WriteIdentity(identity), 1);
        }

        public void PreparePublication(string path, byte[] content)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
                return;
            Prepare(path, content, 2);
        }

        void Prepare(string path, byte[] content, int order)
        {
            path = Path.GetFullPath(path);
            if (m_Writes.TryGetValue(path, out Write existing))
            {
                if (!existing.Content.AsSpan().SequenceEqual(content))
                    throw new InvalidDataException($"同一发布事务为 '{path}' 提供了不同内容。");
                return;
            }
            m_Writes.Add(path, new Write { Path = path, Content = content, Order = order,
                Previous = File.Exists(path) ? File.ReadAllBytes(path) : null });
        }

        public void Commit()
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
                    if (write.Previous == null)
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
                        if (write.Previous == null)
                            File.Delete(write.Path);
                        else
                            File.WriteAllBytes(write.Path, write.Previous);
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
        }
    }
}
