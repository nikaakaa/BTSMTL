using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public sealed record PlayerContentPackage(JsonElement Content, JsonElement Receipt,
    JsonElement AdoptedIdentity, JsonElement Observation, PlayerOperationPhase[] Phases);
public sealed record PlayerContentPublication(string SourceRevision, JsonElement RequestIdentity,
    Func<PlayerContentPackage> Prepare);
public sealed record PlayerContentRefreshReceipt(Guid RequestId, string Stage, Guid LiveId,
    string? PlayerBuildId, string SourceRevision, JsonElement ProjectParameters,
    PlayerContentPackage? Package, PlayerCommandReceipt? Command, string? Error)
{
    public string? StatusPath { get; init; }
    public PlayerOperationPhase[] Phases { get; init; } = [];
    public JsonElement? AdoptedContent => Stage == "adopted" ? Package!.AdoptedIdentity : null;
}

public sealed class PlayerContentRefreshes(PlayerLiveSessions live, string output)
{
    readonly string m_Directory = Path.Combine(PlayerInputPreparation.OutputDirectory(output), "refreshes");
    readonly ConcurrentDictionary<Guid, SemaphoreSlim> m_Gates = new();

    public async Task<PlayerContentRefreshReceipt> Refresh(PlayerContentRefreshTarget target, PlayerContentPublication publication)
    {
        var receipt = new PlayerContentRefreshReceipt(target.RequestId, "accepted", target.LiveId,
            null, publication.SourceRevision, target.ProjectParameters, null, null, null);
        if (target.RequestId == Guid.Empty)
            return receipt with { Stage = "failed", Error = "Player刷新需要非空requestId。" };
        SemaphoreSlim gate = m_Gates.GetOrAdd(target.RequestId, _ => new(1));
        var elapsed = Stopwatch.StartNew();
        await gate.WaitAsync();
        double waitMilliseconds = elapsed.Elapsed.TotalMilliseconds;
        elapsed.Restart();
        try
        {
            string directory = DirectoryFor(target.RequestId);
            string requestPath = Path.Combine(directory, "request.json");
            string request = PlayerInputPreparation.Canonical(JsonSerializer.SerializeToElement(new
                { Target = target, publication.RequestIdentity }, WorkbenchArtifacts.JsonOptions));
            if (File.Exists(requestPath))
            {
                if (File.ReadAllText(requestPath, System.Text.Encoding.UTF8) != request)
                    return receipt with { Stage = "failed", Error = "同一Player刷新身份已用于不同的内容、输入或编译产物。" };
                return ReadUnlocked(target.RequestId);
            }
            Directory.CreateDirectory(directory);
            WorkbenchArtifacts.WriteBytes(requestPath, System.Text.Encoding.UTF8.GetBytes(request));
            receipt = receipt with { Phases = [new("request-wait", waitMilliseconds), new("request-prepare", elapsed.Elapsed.TotalMilliseconds)] };
            try
            {
                receipt = receipt with { PlayerBuildId = live.Identity(target.LiveId).PlayerBuildId };
                PlayerContentPackage package = publication.Prepare();
                receipt = receipt with { Package = package, Phases = [.. receipt.Phases, .. package.Phases] };
                PlayerCommandReceipt command = await live.Load(target.LiveId,
                    new(target.RequestId, package.Content, target.Channels));
                return Save(WithCommand(receipt, command));
            }
            catch (Exception failure) when (failure is KeyNotFoundException or PlayerInputUnavailableException)
            {
                return Save(receipt with { Stage = "unavailable", Error = failure.Message });
            }
            catch (Exception failure)
            {
                return Save(receipt with { Stage = "failed", Error = failure.Message });
            }
        }
        finally { gate.Release(); }
    }

    public async Task<PlayerContentRefreshReceipt> Read(Guid id)
    {
        SemaphoreSlim gate = m_Gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync();
        try { return ReadUnlocked(id); }
        finally { gate.Release(); }
    }

    PlayerContentRefreshReceipt ReadUnlocked(Guid id)
    {
        var receipt = WorkbenchArtifacts.Read<PlayerContentRefreshReceipt>(Path.Combine(DirectoryFor(id), "receipt.json"));
        if (receipt.Stage != "accepted") return receipt;
        try { return Save(WithCommand(receipt, live.ReadCommand(receipt.LiveId, receipt.Command!.CommandId!))); }
        catch (KeyNotFoundException failure) { return Save(receipt with { Stage = "unavailable", Error = failure.Message }); }
    }

    static PlayerContentRefreshReceipt WithCommand(PlayerContentRefreshReceipt receipt, PlayerCommandReceipt command)
    {
        if (command.Milliseconds is { } milliseconds)
            receipt = receipt with { Phases = [.. receipt.Phases, new("player-command", milliseconds)] };
        return command.Stage switch
        {
            "applied" when PlayerInputPreparation.Same(PlayerInputPreparation.Value(command.Content), receipt.Package!.AdoptedIdentity) =>
                receipt with { Stage = "adopted", Command = command },
            "submitted" => receipt with { Stage = "accepted", Command = command },
            _ => receipt with { Stage = "failed", Command = command, Error = (string?)command.Ack?["error"] ?? "Player未确认采用本次发布的内容修订。" }
        };
    }

    PlayerContentRefreshReceipt Save(PlayerContentRefreshReceipt receipt)
    {
        receipt = receipt with { StatusPath = $"/api/preview/refreshes/{receipt.RequestId:N}" };
        WorkbenchArtifacts.Write(Path.Combine(DirectoryFor(receipt.RequestId), "receipt.json"), receipt);
        return receipt;
    }
    string DirectoryFor(Guid id) => Path.Combine(m_Directory, id.ToString("N"));
}
