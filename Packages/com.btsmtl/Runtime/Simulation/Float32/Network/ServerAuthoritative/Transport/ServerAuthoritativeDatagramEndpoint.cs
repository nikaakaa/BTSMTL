using System;
using System.Collections.Concurrent;
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
        public ServerAuthoritativeReceivedDatagram(ServerAuthoritativeDatagramPacket packet, IPEndPoint remoteEndPoint)
        {
            Packet = packet;
            if (!packet.IsValid)
                throw new ArgumentException("Gameplay received packet is invalid.", nameof(packet));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
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
        readonly ConcurrentQueue<ServerAuthoritativeReceivedDatagram> m_ReceiveQueue = new ConcurrentQueue<ServerAuthoritativeReceivedDatagram>();
        readonly Stack<IPEndPoint> m_ReceiveEndPoints;
        readonly Stack<byte[]> m_ReceivePayloads;
        readonly ConcurrentQueue<PendingSend> m_SendQueue = new ConcurrentQueue<PendingSend>();
        readonly Stack<IPEndPoint> m_SendEndPoints;
        readonly Stack<byte[]> m_SendBuffers;
        readonly ThreadLocal<CanonicalWriter> m_SendWriter;
        readonly Dictionary<ServerAuthoritativeDatagramIdentity, Route> m_Routes =
            new Dictionary<ServerAuthoritativeDatagramIdentity, Route>();
        readonly object m_RouteLock = new object();
        CanonicalDeclaredIdentityReader m_DeclaredIdentities = new CanonicalDeclaredIdentityReader(Array.Empty<string>());
        readonly EndPoint m_ReceiveFromEndPoint;
        readonly int m_QueueCapacity;
        readonly int m_MaximumDatagramBytes;
        int m_ReceiveCount;
        int m_SendCount;
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
            m_ReceiveEndPoints = new Stack<IPEndPoint>(queueCapacity);
            m_ReceivePayloads = new Stack<byte[]>(queueCapacity);
            m_SendEndPoints = new Stack<IPEndPoint>(queueCapacity);
            m_SendBuffers = new Stack<byte[]>(queueCapacity);
            m_SendWriter = new ThreadLocal<CanonicalWriter>(() => new CanonicalWriter(new byte[maximumDatagramBytes]));
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
        public int ReceiveQueueDepth => Volatile.Read(ref m_ReceiveCount);
        public int SendQueueDepth => Volatile.Read(ref m_SendCount);
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
            ThrowIfUnavailable();
            IPEndPoint remote;
            lock (m_RouteLock)
            {
                if (!m_Routes.TryGetValue(header.Identity, out Route route))
                    throw new InvalidOperationException($"Gameplay data route '{header.Identity}' is not bound.");
                remote = route.RemoteEndPoint;
            }
            CanonicalWriter writer = m_SendWriter.Value;
            int length = ServerAuthoritativeGameplayDatagramCodec.Write(header, payload, writer, m_MaximumDatagramBytes);
            byte[] bytes = RentSendBuffer();
            writer.WrittenSpan.CopyTo(bytes);
            if (Interlocked.Increment(ref m_SendCount) > m_QueueCapacity)
            {
                Interlocked.Decrement(ref m_SendCount);
                ReturnSendBuffer(bytes);
                Fail(new InvalidOperationException("Gameplay datagram send queue overflow."));
                ThrowIfUnavailable();
            }
            m_SendQueue.Enqueue(new PendingSend(bytes, length, RentSendEndPoint(remote)));
        }

        public void PumpSend()
        {
            ThrowIfUnavailable();
            while (m_SendQueue.TryDequeue(out PendingSend pending))
            {
                Interlocked.Decrement(ref m_SendCount);
                IPEndPoint sendEndPoint = pending.RemoteEndPoint;
                try
                {
                    try
                    {
                        int sent = m_Socket.SendTo(pending.Bytes, 0, pending.Length, SocketFlags.None, sendEndPoint);
                        if (sent != pending.Length)
                            throw new IOException($"Gameplay datagram send wrote '{sent}' of '{pending.Length}' bytes.");
                        Interlocked.Increment(ref m_SentPackets);
                        Interlocked.Add(ref m_SentBytes, sent);
                    }
                    finally
                    {
                        ReturnSendBuffer(pending.Bytes);
                        ReturnSendEndPoint(sendEndPoint);
                    }
                }
                catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException || exception is IOException)
                {
                    Fail(exception);
                    ThrowIfUnavailable();
                }
            }
        }

        public bool TryReceive(out ServerAuthoritativeReceivedDatagram datagram)
        {
            ThrowIfUnavailable();
            if (!m_ReceiveQueue.TryDequeue(out datagram))
                return false;
            Interlocked.Decrement(ref m_ReceiveCount);
            return true;
        }

        void ReturnReceiveEndPoint(IPEndPoint remoteEndPoint)
        {
            lock (m_ReceiveEndPoints)
            {
                if (m_ReceiveEndPoints.Count < m_QueueCapacity)
                    m_ReceiveEndPoints.Push(remoteEndPoint);
            }
        }

        public void ReturnReceived(in ServerAuthoritativeReceivedDatagram datagram)
        {
            ReturnReceivePayload(datagram.Packet.PayloadBuffer);
            ReturnReceiveEndPoint(datagram.RemoteEndPoint);
        }

        void ReturnReceivePayload(byte[] payload)
        {
            lock (m_ReceivePayloads)
            {
                if (m_ReceivePayloads.Count < m_QueueCapacity)
                    m_ReceivePayloads.Push(payload);
            }
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
                            m_MaximumDatagramBytes,
                            payloadBuffer);
                    }
                    catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException)
                    {
                        ReturnReceivePayload(payloadBuffer);
                        Interlocked.Increment(ref m_MalformedDrops);
                        continue;
                    }

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
                        if (Interlocked.Increment(ref m_ReceiveCount) > m_QueueCapacity)
                        {
                            Interlocked.Decrement(ref m_ReceiveCount);
                            ReturnReceiveEndPoint(remoteEndPoint);
                            ReturnReceivePayload(payloadBuffer);
                            Fail(new InvalidOperationException("Gameplay datagram receive queue overflow."));
                            return;
                        }
                        m_ReceiveQueue.Enqueue(new ServerAuthoritativeReceivedDatagram(packet, remoteEndPoint));
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
            if (exception == null)
                return;
            Interlocked.CompareExchange(ref m_Failure, exception, null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
                return;
            try
            {
                m_Socket.Close();
            }
            catch
            {
            }
            if (m_ReceiveThread.IsAlive)
                m_ReceiveThread.Join(1000);
            while (m_ReceiveQueue.TryDequeue(out ServerAuthoritativeReceivedDatagram datagram))
                ReturnReceived(datagram);
            while (m_SendQueue.TryDequeue(out PendingSend pending))
            {
                ReturnSendBuffer(pending.Bytes);
                ReturnSendEndPoint(pending.RemoteEndPoint);
            }
            m_SendWriter.Dispose();
            Interlocked.Exchange(ref m_ReceiveCount, 0);
            Interlocked.Exchange(ref m_SendCount, 0);
            lock (m_RouteLock)
                m_Routes.Clear();
            m_Socket.Dispose();
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
            bool rented;
            lock (m_ReceiveEndPoints)
                rented = m_ReceiveEndPoints.TryPop(out endpoint);
            if (!rented)
                endpoint = new IPEndPoint(value.Address, value.Port);
            endpoint.Address = value.Address;
            endpoint.Port = value.Port;
            return endpoint;
        }

        byte[] RentReceivePayload()
        {
            lock (m_ReceivePayloads)
            {
                if (m_ReceivePayloads.TryPop(out byte[] payload))
                    return payload;
            }
            return new byte[m_MaximumDatagramBytes];
        }

        static bool EndPointEquals(IPEndPoint left, IPEndPoint right) =>
            left.Port == right.Port && left.Address.Equals(right.Address);

        IPEndPoint RentSendEndPoint(IPEndPoint value)
        {
            IPEndPoint endpoint;
            bool rented;
            lock (m_SendEndPoints)
                rented = m_SendEndPoints.TryPop(out endpoint);
            if (!rented)
                endpoint = new IPEndPoint(value.Address, value.Port);
            endpoint.Address = value.Address;
            endpoint.Port = value.Port;
            return endpoint;
        }

        void ReturnSendEndPoint(IPEndPoint value)
        {
            lock (m_SendEndPoints)
            {
                if (m_SendEndPoints.Count < m_QueueCapacity)
                    m_SendEndPoints.Push(value);
            }
        }

        byte[] RentSendBuffer()
        {
            lock (m_SendBuffers)
            {
                if (m_SendBuffers.TryPop(out byte[] buffer))
                    return buffer;
            }
            return new byte[m_MaximumDatagramBytes];
        }

        void ReturnSendBuffer(byte[] buffer)
        {
            lock (m_SendBuffers)
            {
                if (m_SendBuffers.Count < m_QueueCapacity)
                    m_SendBuffers.Push(buffer);
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
