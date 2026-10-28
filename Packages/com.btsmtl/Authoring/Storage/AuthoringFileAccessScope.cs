#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BTSMTL.Authoring.Storage
{
public sealed class AuthoringFileAccessScope : IDisposable
{
    readonly List<Mutex> m_Acquired = new();

    AuthoringFileAccessScope() { }

    public static AuthoringFileAccessScope Acquire(IEnumerable<string> paths, CancellationToken cancellation = default)
    {
        var scope = new AuthoringFileAccessScope();
        try
        {
            foreach (string path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string key = Path.DirectorySeparatorChar == '\\' ? path.ToUpperInvariant() : path;
                var mutex = new Mutex(false, "BTSMTL.Authoring." + AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(key)));
                try
                {
                    if (WaitHandle.WaitAny(new WaitHandle[] { mutex, cancellation.WaitHandle }) == 1)
                    { mutex.Dispose(); cancellation.ThrowIfCancellationRequested(); }
                }
                catch (AbandonedMutexException) { scope.m_Acquired.Add(mutex); continue; }
                scope.m_Acquired.Add(mutex);
            }
            return scope;
        }
        catch { scope.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (Mutex mutex in m_Acquired.AsEnumerable().Reverse())
        { mutex.ReleaseMutex(); mutex.Dispose(); }
        m_Acquired.Clear();
    }
}
}
