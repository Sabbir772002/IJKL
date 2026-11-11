using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Config;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed class ExpirySweeper(
    IDbContextFactory<AdmissionDbContext> factory, AppOptions options,
    ILogger<ExpirySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            options.SweepIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var expired = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE holds SET status = 'EXPIRED' WHERE status = 'HELD' AND expires_at <= clock_timestamp()",
                    stoppingToken);
                if (expired > 0)
                    await HoldService.NotifyAsync(db, "sweep", logger);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not sweep expired holds. The next sweep will retry.");
            }
        }
    }
}
