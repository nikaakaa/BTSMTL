using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

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
        readonly object m_ImageGate = new object();
        Task m_Receive;
        Task m_Send;
        PreviewImagePacket m_Image;
        bool m_ImageRequested;
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

        public bool TrySendStreamImage(PreviewImagePacket packet, bool requested)
        {
            lock (m_ImageGate)
            {
                if (State != PreviewConnectionState.Connected || m_ImageRequested)
                {
                    packet.Release();
                    return false;
                }
                PreviewImagePacket previous = m_Image;
                m_Image = packet;
                m_ImageRequested = requested;
                previous?.Release();
                if (previous == null)
                    m_SendSignal.Release();
                return true;
            }
        }

        public async Task CloseAsync()
        {
            EndConnection(nameof(CloseAsync), null);
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
                        new ArraySegment<byte>(buffer), m_Cancel.Token).ConfigureAwait(false);
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
                EndConnection(nameof(Receive), null);
            }
            catch (OperationCanceledException) when (m_Cancel.IsCancellationRequested)
            {
                EndConnection(nameof(Receive), null);
            }
            catch (Exception failure)
            {
                EndConnection(nameof(Receive), failure);
            }
        }

        async Task Send()
        {
            try
            {
                while (!m_Cancel.IsCancellationRequested)
                {
                    await m_SendSignal.WaitAsync(m_Cancel.Token).ConfigureAwait(false);
                    while (m_Outgoing.TryDequeue(out string message))
                        await m_Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
                            WebSocketMessageType.Text, true, m_Cancel.Token).ConfigureAwait(false);
                    PreviewImagePacket image;
                    lock (m_ImageGate)
                    {
                        image = m_Image;
                        m_Image = null;
                        m_ImageRequested = false;
                    }
                    if (image != null)
                    {
                        try
                        {
                            await m_Socket.SendAsync(new ArraySegment<byte>(image.Buffer, 0, image.Length),
                                WebSocketMessageType.Binary, true, m_Cancel.Token).ConfigureAwait(false);
                        }
                        finally { image.Release(); }
                    }
                }
                EndConnection(nameof(Send), null);
            }
            catch (OperationCanceledException) when (m_Cancel.IsCancellationRequested)
            {
                EndConnection(nameof(Send), null);
            }
            catch (Exception failure)
            {
                EndConnection(nameof(Send), failure);
            }
        }

        void EndConnection(string origin, Exception failure)
        {
            PreviewConnectionState stateBefore;
            PreviewConnectionState stateAfter;
            bool cancelRequestedBefore;
            lock (m_ImageGate)
            {
                stateBefore = State;
                cancelRequestedBefore = m_Cancel.IsCancellationRequested;
                if (failure == null)
                    Interlocked.CompareExchange(ref m_State, (int)PreviewConnectionState.Closed,
                        (int)PreviewConnectionState.Connected);
                else if (stateBefore is not (PreviewConnectionState.Closed or PreviewConnectionState.Failed))
                {
                    Failure = failure;
                    Volatile.Write(ref m_State, (int)PreviewConnectionState.Failed);
                }
                m_Image?.Release();
                m_Image = null;
                m_ImageRequested = false;
                stateAfter = State;
            }
            m_Cancel.Cancel();
            string errorCodes = string.Empty;
            for (Exception error = failure; error != null; error = error.InnerException)
            {
                if (error is WebSocketException webSocket)
                    errorCodes += $" webSocketError={webSocket.WebSocketErrorCode}";
                if (error is SocketException socket)
                    errorCodes += $" socketError={socket.SocketErrorCode}";
            }
            int failureId = failure == null ? 0 : RuntimeHelpers.GetHashCode(failure);
            Debug.Log($"[PreviewAgent.ConnectionEnd] origin={origin} stateBefore={stateBefore} stateAfter={stateAfter} " +
                $"cancelRequestedBefore={cancelRequestedBefore} failureId={failureId}{errorCodes}" +
                (failure == null ? string.Empty : $"\n{failure}"));
        }
    }
}
