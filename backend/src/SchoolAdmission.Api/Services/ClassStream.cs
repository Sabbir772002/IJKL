using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SchoolAdmission.Api.Services;

public sealed class ClassStream(
    IConfiguration configuration, IServiceScopeFactory scopes,
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions, ILogger<ClassStream> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> clients = new();

    public async Task WriteAsync(HttpContext context, ClassService classes)
    {
        var token = context.RequestAborted;
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        clients.TryAdd(id, channel);
        try
        {
            await WriteEventAsync(response, Serialize(await classes.ListAsync(null, token)), token);
            while (!token.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(token);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                string snapshot;
                try
                {
                    snapshot = await channel.Reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    await response.WriteAsync(": ping\nevent: ping\ndata: {}\n\n", token);
                    await response.Body.FlushAsync(token);
                    continue;
                }

                await WriteEventAsync(response, snapshot, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            clients.TryRemove(id, out _);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Default"));
                await connection.OpenAsync(stoppingToken);
                var changed = false;
                connection.Notification += (_, _) => changed = true;
                await using var listen = new NpgsqlCommand("LISTEN class_changes", connection);
                await listen.ExecuteNonQueryAsync(stoppingToken);
                await BroadcastAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!changed) await connection.WaitAsync(stoppingToken);
                    if (!changed) continue;

                    changed = false;
                    await BroadcastAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Class stream disconnected. Retrying in two seconds.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task BroadcastAsync(CancellationToken token)
    {
        if (clients.IsEmpty) return;
        await using var scope = scopes.CreateAsyncScope();
        var classes = scope.ServiceProvider.GetRequiredService<ClassService>();
        var snapshot = Serialize(await classes.ListAsync(null, token));
        foreach (var channel in clients.Values)
            channel.Writer.TryWrite(snapshot);
    }

    private string Serialize(List<ClassSeatRow> classes) =>
        JsonSerializer.Serialize(classes, jsonOptions.Value.SerializerOptions);

    private static async Task WriteEventAsync(HttpResponse response, string snapshot, CancellationToken token)
    {
        await response.WriteAsync($"event: classes\ndata: {snapshot}\n\n", token);
        await response.Body.FlushAsync(token);
    }
}
