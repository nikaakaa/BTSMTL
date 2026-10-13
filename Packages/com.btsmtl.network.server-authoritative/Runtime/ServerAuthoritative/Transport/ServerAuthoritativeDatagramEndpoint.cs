using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    public interface IServerAuthoritativeAuthorityDataTransport : IDisposable
    {
        IPEndPoint LocalEndPoint { get; }
        int ReceiveQueueDepth { get; }
        int SendQueueDepth { get; }
        bool IsFailed { get; }
        ServerAuthoritativeDatagramMetrics CaptureMetrics();
        void BindRemote(ServerAuthoritativeDatagramIdentity identity, IPEndPoint remoteEndPoint);
        void RevokeRemote(ServerAuthoritativeDatagramIdentity identity);
        void EnqueueSend(ServerAuthoritativeDatagramHeader header, ReadOnlySpan<byte> payload);
        void PumpSend();
        bool TryReceive(out ServerAuthoritativeReceivedDatagram datagram);
        void ReturnReceived(in ServerAuthoritativeReceivedDatagram datagram);
        void ThrowIfUnavailable();
    }

    public readonly struct ServerAuthoritativeDatagramMetrics
    {
        public ServerAuthoritativeDatagramMetrics(
            long sentPackets,
            long sentBytes,
            long receivedPackets,
            long receivedBytes,
            long malformedDrops,
            long unknownRouteDrops,
            long oversizeDrops,
            long endpointMismatchDrops)
        {
            SentPackets = sentPackets;
            SentBytes = sentBytes;
            ReceivedPackets = receivedPackets;
            ReceivedBytes = receivedBytes;
            MalformedDrops = malformedDrops;
            UnknownRouteDrops = unknownRouteDrops;
            OversizeDrops = oversizeDrops;
            EndpointMismatchDrops = endpointMismatchDrops;
        }

        public long SentPackets { get; }
        public long SentBytes { get; }
        public long ReceivedPackets { get; }
        public long ReceivedBytes { get; }
        public long MalformedDrops { get; }
        public long UnknownRouteDrops { get; }
        public long OversizeDrops { get; }
        public long EndpointMismatchDrops { get; }
    }

    public readonly struct ServerAuthoritativeReceivedDatagram
    {
        internal ServerAuthoritativeReceivedDatagram(ServerAuthoritativeDatagramPacket packet, IPEndPoint remoteEndPoint)
        {
            Packet = packet;
            RemoteEndPoint = remoteEndPoint;
        }

        public ServerAuthoritativeDatagramPacket Packet { get; }
        public IPEndPoint RemoteEndPoint { get; }
    }

    public sealed class ServerAuthoritativeDatagramEndpoint :
        IServerAuthoritativeAuthorityDataTransport,
        ICanonicalTextResolver
    {
        readonly Socket m_Socket;
        readonly Thread m_ReceiveThread;
        readonly Queue<ServerAuthoritativeReceivedDatagram> m_ReceiveQueue;
        readonly object m_ReceiveGate = new object();
        readonly Stack<IPEndPoint> m_ReceiveEndPoints;
        readonly Stack<byte[]> m_ReceivePayloads;
        readonly Queue<PendingSend> m_SendQueue;
        readonly object m_SendGate = new object();
        readonly object m_SendPumpGate = new object();
        readonly Stack<IPEndPoint> m_SendEndPoints;
        readonly Stack<byte[]> m_SendBuffers;
        readonly CanonicalWriter m_SendWriter;
        readonly Dictionary<ServerAuthoritativeDatagramIdentity, Route> m_Routes =
            new Dictionary<ServerAuthoritativeDatagramIdentity, Route>();
        readonly object m_RouteLock = new object();
        CanonicalDeclaredIdentityReader m_DeclaredIdentities = new CanonicalDeclaredIdentityReader(Array.Empty<string>());
        readonly EndPoint m_ReceiveFromEndPoint;
        readonly int m_QueueCapacity;
        readonly int m_MaximumDatagramBytes;
        int m_Disposed;
        Exception m_Failure;
        long m_SentPackets;
        long m_SentBytes;
        long m_ReceivedPackets;
        long m_ReceivedBytes;
        long m_MalformedDrops;
        long m_UnknownRouteDrops;
        long m_OversizeDrops;
        long m_EndpointMismatchDrops;

        public ServerAuthoritativeDatagramEndpoint(
            IPEndPoint localEndPoint,
            int queueCapacity,
            int maximumDatagramBytes)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (maximumDatagramBytes < 256 || maximumDatagramBytes > 1200)
                throw new ArgumentOutOfRangeException(nameof(maximumDatagramBytes));
            m_QueueCapacity = queueCapacity;
            m_MaximumDatagramBytes = maximumDatagramBytes;
            m_ReceiveQueue = new Queue<ServerAuthoritativeReceivedDatagram>(queueCapacity);
            int receiveStorageCount = checked(queueCapacity + 2);
            m_ReceiveEndPoints = new Stack<IPEndPoint>(receiveStorageCount);
            m_ReceivePayloads = new Stack<byte[]>(receiveStorageCount);
            for (int i = 0; i < receiveStorageCount; i++)
            {
                m_ReceiveEndPoints.Push(new IPEndPoint(localEndPoint.Address, 0));
                m_ReceivePayloads.Push(new byte[maximumDatagramBytes]);
            }
            m_SendQueue = new Queue<PendingSend>(queueCapacity);
            int sendStorageCount = checked(queueCapacity + 1);
            m_SendEndPoints = new Stack<IPEndPoint>(sendStorageCount);
            m_SendBuffers = new Stack<byte[]>(sendStorageCount);
            for (int i = 0; i < sendStorageCount; i++)
            {
                m_SendEndPoints.Push(new IPEndPoint(localEndPoint.Address, 0));
                m_SendBuffers.Push(new byte[maximumDatagramBytes]);
            }
            m_SendWriter = new CanonicalWriter(new byte[maximumDatagramBytes]);
            m_Socket = new Socket(localEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = 250,
                SendTimeout = 250
            };
            m_Socket.Bind(localEndPoint);
            LocalEndPoint = (IPEndPoint)m_Socket.LocalEndPoint;
            m_ReceiveFromEndPoint = LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);
            m_ReceiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "ServerAuthoritativeGameplayDatagram"
            };
            m_ReceiveThread.Start();
        }

        public IPEndPoint LocalEndPoint { get; }
        public int ReceiveQueueDepth { get { lock (m_ReceiveGate) return m_ReceiveQueue.Count; } }
        public int SendQueueDepth { get { lock (m_SendGate) return m_SendQueue.Count; } }
        public bool IsFailed => Volatile.Read(ref m_Failure) != null;

        public ServerAuthoritativeDatagramMetrics CaptureMetrics() => new ServerAuthoritativeDatagramMetrics(
            Interlocked.Read(ref m_SentPackets),
            Interlocked.Read(ref m_SentBytes),
            Interlocked.Read(ref m_ReceivedPackets),
            Interlocked.Read(ref m_ReceivedBytes),
            Interlocked.Read(ref m_MalformedDrops),
            Interlocked.Read(ref m_UnknownRouteDrops),
            Interlocked.Read(ref m_OversizeDrops),
            Interlocked.Read(ref m_EndpointMismatchDrops));

        public void BindRemote(ServerAuthoritativeDatagramIdentity identity, IPEndPoint remoteEndPoint)
        {
            ThrowIfUnavailable();
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
            lock (m_RouteLock)
            {
                if (m_Routes.TryGetValue(identity, out Route current))
                {
                    if (!EndPointEquals(current.RemoteEndPoint, remoteEndPoint))
                        throw new InvalidOperationException($"Gameplay data endpoint for '{identity}' cannot change while active.");
                    return;
                }
                m_Routes.Add(identity, new Route(identity, Clone(remoteEndPoint)));
                RebuildDeclaredIdentities();
            }
        }

        public void RevokeRemote(ServerAuthoritativeDatagramIdentity identity)
        {
            lock (m_RouteLock)
            {
                m_Routes.Remove(identity);
                RebuildDeclaredIdentities();
            }
        }

        public void EnqueueSend(ServerAuthoritativeDatagramHeader header, ReadOnlySpan<byte> payload)
        {
            lock (m_SendGate)
            {
                ThrowIfUnavailable();
                IPEndPoint remote;
                lock (m_RouteLock)
                {
                    if (!m_Routes.TryGetValue(header.Identity, out Route route))
                        throw new InvalidOperationException($"Gameplay data route '{header.Identity}' is not bound.");
                    remote = route.RemoteEndPoint;
                }
                int length = ServerAuthoritativeGameplayDatagramCodec.Write(header, payload, m_SendWriter, m_MaximumDatagramBytes);
                if (m_SendQueue.Count == m_QueueCapacity)
                {
                    Fail(new InvalidOperationException("Gameplay datagram send queue overflow."));
                    ThrowIfUnavailable();
                }
                byte[] bytes = m_SendBuffers.Pop();
                m_SendWriter.WrittenSpan.CopyTo(bytes);
                IPEndPoint endpoint = m_SendEndPoints.Pop();
                endpoint.Address = remote.Address;
                endpoint.Port = remote.Port;
                m_SendQueue.Enqueue(new PendingSend(bytes, length, endpoint));
            }
        }

        public void PumpSend()
        {
            lock (m_SendPumpGate)
            {
                while (true)
                {
                    PendingSend pending;
                    lock (m_SendGate)
                    {
                        ThrowIfUnavailable();
                        if (!m_SendQueue.TryDequeue(out pending))
                            return;
                    }
                    try
                    {
                        try
                        {
                            int sent = m_Socket.SendTo(pending.Bytes, 0, pending.Length, SocketFlags.None, pending.RemoteEndPoint);
                            if (sent != pending.Length)
                                throw new IOException($"Gameplay datagram send wrote '{sent}' of '{pending.Length}' bytes.");
                            Interlocked.Increment(ref m_SentPackets);
                            Interlocked.Add(ref m_SentBytes, sent);
                        }
                        finally
                        {
                            ReturnSend(in pending);
                        }
                    }
                    catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException || exception is IOException)
                    {
                        Fail(exception);
                        ThrowIfUnavailable();
                    }
                }
            }
        }

        public bool TryReceive(out ServerAuthoritativeReceivedDatagram datagram)
        {
            lock (m_ReceiveGate)
            {
                ThrowIfUnavailable();
                return m_ReceiveQueue.TryDequeue(out datagram);
            }
        }

        public void ReturnReceived(in ServerAuthoritativeReceivedDatagram datagram)
        {
            lock (m_ReceiveGate)
            {
                m_ReceivePayloads.Push(datagram.Packet.PayloadBuffer);
                m_ReceiveEndPoints.Push(datagram.RemoteEndPoint);
            }
        }

        void ReturnReceivePayload(byte[] payload)
        {
            lock (m_ReceiveGate)
                m_ReceivePayloads.Push(payload);
        }

        public void ThrowIfUnavailable()
        {
            if (Volatile.Read(ref m_Disposed) != 0)
                throw new ObjectDisposedException(nameof(ServerAuthoritativeDatagramEndpoint));
            Exception failure = Volatile.Read(ref m_Failure);
            if (failure != null)
                throw new InvalidOperationException("Gameplay datagram endpoint failed.", failure);
        }

        void ReceiveLoop()
        {
            var buffer = new byte[m_MaximumDatagramBytes + 1];
            var reader = new CanonicalReader(buffer);
            reader.DeclaredIdentityReader = ReadIdentity;
            reader.SetTextResolver(this);
            while (Volatile.Read(ref m_Disposed) == 0 && Volatile.Read(ref m_Failure) == null)
            {
                EndPoint remote = m_ReceiveFromEndPoint;
                try
                {
                    int received = m_Socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remote);
                    if (received <= 0)
                        continue;
                    if (received > m_MaximumDatagramBytes)
                    {
                        Interlocked.Increment(ref m_OversizeDrops);
                        continue;
                    }
                    byte[] payloadBuffer = RentReceivePayload();
                    ServerAuthoritativeDatagramPacket packet;
                    try
                    {
                        packet = ServerAuthoritativeGameplayDatagramCodec.Read(
                            new ArraySegment<byte>(buffer, 0, received),
                            reader,
                            payloadBuffer);
                    }
                    catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException)
                    {
                        ReturnReceivePayload(payloadBuffer);
                        Interlocked.Increment(ref m_MalformedDrops);
                        continue;
                    }

                    lock (m_RouteLock)
                    {
                        if (m_Routes.TryGetValue(packet.Header.Identity, out Route route))
                        {
                            if (!EndPointEquals(route.RemoteEndPoint, (IPEndPoint)remote))
                            {
                                Interlocked.Increment(ref m_EndpointMismatchDrops);
                                Fail(new InvalidOperationException($"Gameplay data endpoint changed for '{packet.Header.Identity}'."));
                                ReturnReceivePayload(payloadBuffer);
                                return;
                            }
                        }
                        else if (packet.Header.Kind != ServerAuthoritativeDatagramKind.DataPlaneHello)
                        {
                            Interlocked.Increment(ref m_UnknownRouteDrops);
                            ReturnReceivePayload(payloadBuffer);
                            return;
                        }
                    }

                    IPEndPoint remoteEndPoint = RentReceiveEndPoint((IPEndPoint)remote);
                    lock (m_ReceiveGate)
                    {
                        if (m_ReceiveQueue.Count == m_QueueCapacity)
                        {
                            m_ReceiveEndPoints.Push(remoteEndPoint);
                            m_ReceivePayloads.Push(payloadBuffer);
                            Fail(new InvalidOperationException("Gameplay datagram receive queue overflow."));
                            return;
                        }
                        m_ReceiveQueue.Enqueue(new ServerAuthoritativeReceivedDatagram(packet, remoteEndPoint));
                    }
                    Interlocked.Increment(ref m_ReceivedPackets);
                    Interlocked.Add(ref m_ReceivedBytes, received);
                }
                catch (SocketException exception) when (
                    exception.SocketErrorCode == SocketError.TimedOut ||
                    exception.SocketErrorCode == SocketError.WouldBlock ||
                    exception.SocketErrorCode == SocketError.Interrupted && Volatile.Read(ref m_Disposed) != 0)
                {
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref m_Disposed) != 0)
                {
                }
                catch (Exception exception)
                {
                    if (Volatile.Read(ref m_Disposed) == 0)
                        Fail(exception);
                }
            }
        }

        void Fail(Exception exception)
        {
            Interlocked.CompareExchange(ref m_Failure, exception, null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
                return;
            m_Socket.Close();
            if (m_ReceiveThread.IsAlive)
                m_ReceiveThread.Join(1000);
            lock (m_ReceiveGate)
            {
                while (m_ReceiveQueue.TryDequeue(out ServerAuthoritativeReceivedDatagram datagram))
                {
                    m_ReceivePayloads.Push(datagram.Packet.PayloadBuffer);
                    m_ReceiveEndPoints.Push(datagram.RemoteEndPoint);
                }
            }
            lock (m_SendPumpGate)
            lock (m_SendGate)
            {
                while (m_SendQueue.TryDequeue(out PendingSend pending))
                {
                    m_SendBuffers.Push(pending.Bytes);
                    m_SendEndPoints.Push(pending.RemoteEndPoint);
                }
                m_SendWriter.Dispose();
            }
            lock (m_RouteLock)
                m_Routes.Clear();
        }

        static IPEndPoint Clone(IPEndPoint value) => new IPEndPoint(value.Address, value.Port);

        void RebuildDeclaredIdentities()
        {
            var texts = new CanonicalStringTableBuilder();
            foreach (KeyValuePair<ServerAuthoritativeDatagramIdentity, Route> pair in m_Routes)
                ServerAuthoritativeDatagramIdentity.CanonicalVisitTexts(texts, pair.Value.Identity);
            m_DeclaredIdentities = new CanonicalDeclaredIdentityReader(texts.Values);
        }

        string ReadIdentity(CanonicalReader reader)
        {
            lock (m_RouteLock)
                return m_DeclaredIdentities.ReadInitialization(reader);
        }

        CanonicalTextValue ICanonicalTextResolver.ResolveText(ArraySegment<byte> utf16)
        {
            lock (m_RouteLock)
                return CanonicalTextValue.FromString(m_DeclaredIdentities.ResolveInitialization(utf16));
        }

        IPEndPoint RentReceiveEndPoint(IPEndPoint value)
        {
            IPEndPoint endpoint;
            lock (m_ReceiveGate)
                endpoint = m_ReceiveEndPoints.Pop();
            endpoint.Address = value.Address;
            endpoint.Port = value.Port;
            return endpoint;
        }

        byte[] RentReceivePayload()
        {
            lock (m_ReceiveGate)
                return m_ReceivePayloads.Pop();
        }

        static bool EndPointEquals(IPEndPoint left, IPEndPoint right) =>
            left.Port == right.Port && left.Address.Equals(right.Address);

        void ReturnSend(in PendingSend pending)
        {
            lock (m_SendGate)
            {
                m_SendBuffers.Push(pending.Bytes);
                m_SendEndPoints.Push(pending.RemoteEndPoint);
            }
        }

        readonly struct PendingSend
        {
            public PendingSend(byte[] bytes, int length, IPEndPoint remoteEndPoint)
            {
                Bytes = bytes;
                Length = length;
                RemoteEndPoint = remoteEndPoint;
            }

            public byte[] Bytes { get; }
            public int Length { get; }
            public IPEndPoint RemoteEndPoint { get; }
        }

        readonly struct Route
        {
            public Route(ServerAuthoritativeDatagramIdentity identity, IPEndPoint remoteEndPoint)
            {
                Identity = identity;
                RemoteEndPoint = remoteEndPoint;
            }

            public ServerAuthoritativeDatagramIdentity Identity { get; }
            public IPEndPoint RemoteEndPoint { get; }
        }
    }
}
