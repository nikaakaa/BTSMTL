using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

public sealed class UnityPreviewImageSource(PlayerPreviewJobs jobs, PlayerLiveSessions live, IPlayerAdapter adapter,
    Func<Guid, int, PreviewImageContent> readImage) : IPreviewImageSource
{
    static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);

    public PreviewImageSourceDescription Describe() => adapter.ImageDescription;
    public PreviewImageBuild[] Builds()
    {
        JsonNode result = JsonSerializer.SerializeToNode(jobs.Builds(), s_Json)!;
        return result["items"]!.AsArray().Select(build => adapter.DescribeBuild(build!)).ToArray();
    }
    public PreviewImageContent Read(Guid jobId, int imageIndex) => readImage(jobId, imageIndex);

    public void RequireLive(Guid id) => live.Read(id);
    public Task View(Guid id, WebSocket socket) => live.AcceptViewer(id,
        new ViewerSocket(socket, id, packet => live.FrameSource(id, packet.Span), adapter));

    // 画面连接只转采样头：像素在网页持有的共享画面区，按 source.sequence 对应帧槽。
    static byte[] Video(ReadOnlySpan<byte> packet, PreviewFrameSource source)
    {
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            HostFrame = source.Clock.Position.HostFrame, Source = source,
            Width = PlayerPreviewVideo.Width(packet), Height = PlayerPreviewVideo.Height(packet),
            FlipY = PlayerPreviewVideo.FlipY(packet), WorldToClip = PlayerPreviewVideo.WorldToClip(packet)
        }, s_Json);
        byte[] result = new byte[4 + metadata.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, metadata.Length);
        metadata.CopyTo(result, 4);
        return result;
    }

    sealed class ViewerSocket(WebSocket socket, Guid liveId, Func<ReadOnlyMemory<byte>, PreviewFrameSource> frameSource,
        IPlayerAdapter adapter) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => socket.CloseStatus;
        public override string? CloseStatusDescription => socket.CloseStatusDescription;
        public override WebSocketState State => socket.State;
        public override string? SubProtocol => socket.SubProtocol;
        public override void Abort() => socket.Abort();
        public override void Dispose() => socket.Dispose();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellation) =>
            socket.CloseAsync(status, description, cancellation);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellation) =>
            socket.CloseOutputAsync(status, description, cancellation);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellation) =>
            socket.ReceiveAsync(buffer, cancellation);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken cancellation)
        {
            byte[] bytes = type == WebSocketMessageType.Binary
                ? Video(buffer.AsSpan(), frameSource(buffer.AsMemory())) : adapter.ViewerMessage(liveId, buffer.AsSpan());
            return socket.SendAsync(new ArraySegment<byte>(bytes), type, end, cancellation);
        }
    }
}
