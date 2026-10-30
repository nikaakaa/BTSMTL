using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTSMTL.Preview
{
    public enum PreviewConnectionState : byte { Created, Connected, Closed, Failed }

    public sealed class PreviewAgentConnection
    {
        readonly ClientWebSocket m_Socket = new ClientWebSocket();
        readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
        readonly ConcurrentQueue<string> m_Incoming = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> m_Outgoing = new ConcurrentQueue<string>();
        readonly SemaphoreSlim m_SendSignal = new SemaphoreSlim(0);
        Task m_Receive;
        Task m_Send;
        byte[] m_Image;
        int m_State;

        public PreviewConnectionState State => (PreviewConnectionState)Volatile.Read(ref m_State);
        public Exception Failure { get; private set; }

        public async Task ConnectAsync(Uri endpoint)
        {
            if (!endpoint.IsLoopback || endpoint.Scheme != "ws" && endpoint.Scheme != "wss")
                throw new ArgumentException("预览代理仅连接本机 WebSocket 宿主。", nameof(endpoint));
            await m_Socket.ConnectAsync(endpoint, m_Cancel.Token);
            Volatile.Write(ref m_State, (int)PreviewConnectionState.Connected);
            m_Receive = Receive();
            m_Send = Send();
        }

        public bool TryReadCommand(out string command) => m_Incoming.TryDequeue(out command);

        public void SendText(string message)
        {
            m_Outgoing.Enqueue(message);
            m_SendSignal.Release();
        }

        public void SendLatestImage(byte[] packet)
        {
            Interlocked.Exchange(ref m_Image, packet);
            m_SendSignal.Release();
        }

        public async Task CloseAsync()
        {
            m_Cancel.Cancel();
            if (m_Receive != null)
                await Task.WhenAll(m_Receive, m_Send);
            m_Socket.Dispose();
            m_SendSignal.Dispose();
            m_Cancel.Dispose();
        }

        async Task Receive()
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            try
            {
                while (m_Socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await m_Socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), m_Cancel.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;
                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new InvalidDataException("预览代理的命令必须为文本消息。");
                    m_Incoming.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                }
                EndConnection(null);
            }
            catch (OperationCanceledException) when (m_Cancel.IsCancellationRequested)
            {
                EndConnection(null);
            }
            catch (Exception failure)
            {
                EndConnection(failure);
            }
        }

        async Task Send()
        {
            try
            {
                while (!m_Cancel.IsCancellationRequested)
                {
                    await m_SendSignal.WaitAsync(m_Cancel.Token);
                    while (m_Outgoing.TryDequeue(out string message))
                        await m_Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
                            WebSocketMessageType.Text, true, m_Cancel.Token);
                    byte[] image = Interlocked.Exchange(ref m_Image, null);
                    if (image != null)
                        await m_Socket.SendAsync(new ArraySegment<byte>(image),
                            WebSocketMessageType.Binary, true, m_Cancel.Token);
                }
                EndConnection(null);
            }
            catch (OperationCanceledException) when (m_Cancel.IsCancellationRequested)
            {
                EndConnection(null);
            }
            catch (Exception failure)
            {
                EndConnection(failure);
            }
        }

        void EndConnection(Exception failure)
        {
            if (failure == null)
                Interlocked.CompareExchange(ref m_State, (int)PreviewConnectionState.Closed,
                    (int)PreviewConnectionState.Connected);
            else
            {
                Failure = failure;
                Volatile.Write(ref m_State, (int)PreviewConnectionState.Failed);
            }
            m_Cancel.Cancel();
        }
    }
}
