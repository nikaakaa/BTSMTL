using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace BTSMTL.WebWorkbench;

sealed class PlayerCompilationProcesses(string workingDirectory, string temporary, string logs)
{
    public async Task<int> Run(string name, string executable, IEnumerable<string> arguments,
        IEnumerable<KeyValuePair<string, string>> environment, string directory)
    {
        using Process process = Process.Start(StartInfo(executable, arguments, environment, directory))!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(logs, name + ".log"),
            await output + "\n" + await error, new UTF8Encoding(false));
        return process.ExitCode;
    }

    public IlppRunner StartIlpp(string executable, Guid requestId)
    {
        string pipeName = "unity-ilpp-workbench-" + requestId.ToString("N");
        return new IlppRunner(StartInfo(executable, new[] { pipeName }, Array.Empty<KeyValuePair<string, string>>(), workingDirectory),
            pipeName, Path.Combine(logs, "ilpp-runner.log"));
    }

    ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments,
        IEnumerable<KeyValuePair<string, string>> environment, string directory)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var variable in environment)
            start.Environment[variable.Key] = variable.Value;
        start.Environment["TEMP"] = temporary;
        start.Environment["TMP"] = temporary;
        return start;
    }

    internal sealed class IlppRunner : IAsyncDisposable
    {
        readonly Process m_Process;
        readonly Task<string> m_Output;
        readonly Task<string> m_Error;
        readonly string m_Log;

        public IlppRunner(ProcessStartInfo start, string pipeName, string log)
        {
            PipeName = pipeName;
            m_Log = log;
            m_Process = Process.Start(start)!;
            m_Output = m_Process.StandardOutput.ReadToEndAsync();
            m_Error = m_Process.StandardError.ReadToEndAsync();
        }

        public string PipeName { get; }

        public async Task<bool> WaitForReady()
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cancellation = new CancellationTokenSource();
            Task connected = pipe.ConnectAsync(cancellation.Token);
            Task exited = m_Process.WaitForExitAsync();
            if (await Task.WhenAny(connected, exited) == connected)
            {
                await connected;
                return true;
            }
            cancellation.Cancel();
            try { await connected; }
            catch (OperationCanceledException) { return false; }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (!m_Process.HasExited)
                m_Process.Kill(true);
            await m_Process.WaitForExitAsync();
            await File.WriteAllTextAsync(m_Log, await m_Output + "\n" + await m_Error, new UTF8Encoding(false));
            m_Process.Dispose();
        }
    }
}
