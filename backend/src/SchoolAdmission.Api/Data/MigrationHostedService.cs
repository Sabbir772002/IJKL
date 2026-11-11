using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SchoolAdmission.Api.Data;

public sealed class MigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    private const long MigrationLockKey = 727001;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var lockCommand = new NpgsqlCommand(
                    $"SELECT pg_advisory_lock({MigrationLockKey});",
                    connection);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
                    await db.Database.MigrateAsync(cancellationToken);
                }
                finally
                {
                    await using var unlockCommand = new NpgsqlCommand(
                        $"SELECT pg_advisory_unlock({MigrationLockKey});",
                        connection);
                    await unlockCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                logger.LogInformation("Database migrations applied successfully.");
                return;
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
            {
                lastException = exception;
                if (attempt == 30)
                {
                    break;
                }

                logger.LogWarning(
                    exception,
                    "Database is not ready; migration attempt {Attempt}/30 will retry.",
                    attempt);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw new InvalidOperationException("Database migration failed after 30 attempts.", lastException);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
