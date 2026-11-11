using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed class AdminService(AdmissionDbContext db, ILogger<AdminService> logger)
{
    public async Task<IResult> DeleteClassAsync(string classId, CancellationToken token)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await LockClassAsync(classId, token);
            if (!await ClassExistsAsync(classId, token))
                return MissingClass();

            var active = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM holds WHERE class_id = {classId}
                    AND (status IN ('CONFIRMED', 'PENDING') OR
                         (status = 'HELD' AND expires_at > clock_timestamp()))) AS "Value"
                """).SingleAsync(token);
            if (active)
                return ClassEndpoints.Error(409, "CLASS_HAS_STUDENTS", "The class has active applications.");

            await db.Database.ExecuteSqlAsync(
                $"UPDATE classes SET deleted_at = clock_timestamp() WHERE id = {classId}", token);
            await transaction.CommitAsync(token);
        }
        await HoldService.NotifyAsync(db, classId, logger);
        return Results.NoContent();
    }

    public async Task<IResult> ListHoldsAsync(string classId, CancellationToken token)
    {
        if (!await ClassExistsAsync(classId, token))
            return MissingClass();

        var rows = await db.Database.SqlQuery<AdminHoldRow>($"""
            SELECT id AS "HoldId", student_id AS "StudentId",
                CASE WHEN status = 'HELD' AND expires_at <= clock_timestamp()
                     THEN 'EXPIRED' ELSE status END AS "Status",
                created_at AS "CreatedAt", expires_at AS "ExpiresAt",
                cancelled_by AS "CancelledBy", cancel_reason AS "CancelReason"
            FROM holds WHERE class_id = {classId}
            ORDER BY created_at DESC, id DESC
            """).ToListAsync(token);
        return Results.Ok(rows.Select(row => new
        {
            row.HoldId, row.StudentId, row.Status,
            createdAt = HoldService.FormatExpiry(row.CreatedAt),
            expiresAt = HoldService.FormatExpiry(row.ExpiresAt),
            row.CancelledBy, row.CancelReason
        }));
    }

    public async Task<IResult> CancelAsync(string id, string reason, bool blockSeat, CancellationToken token)
    {
        string classId;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var hold = await db.Holds.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
            if (hold is null)
                return ClassEndpoints.Error(404, "NOT_FOUND", "Application not found.");

            classId = hold.ClassId;
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({hold.StudentId}, 0))", token);
            await LockClassAsync(classId, token);
            if (!await ClassExistsAsync(classId, token))
                return MissingClass();

            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM holds WHERE id = {id} FOR UPDATE", token);
            hold = await db.Holds.AsNoTracking().SingleAsync(row => row.Id == id, token);
            var updated = await db.Database.ExecuteSqlAsync($"""
                UPDATE holds SET status = 'CANCELLED', cancelled_by = 'ADMIN',
                    cancel_reason = {reason}, cancelled_at = clock_timestamp()
                WHERE id = {id} AND (status IN ('CONFIRMED', 'PENDING') OR
                    (status = 'HELD' AND expires_at > clock_timestamp()))
                """, token);
            if (updated == 0)
                return ClassEndpoints.Error(409, "INVALID_STATE", "The application is no longer active.");

            if (blockSeat && hold.Status != "PENDING")
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE classes SET blocked_seats = blocked_seats + 1 WHERE id = {classId}", token);

            await transaction.CommitAsync(token);
        }
        await HoldService.NotifyAsync(db, classId, logger);
        return Results.NoContent();
    }

    private Task<bool> ClassExistsAsync(string id, CancellationToken token) =>
        db.Classes.AsNoTracking().AnyAsync(item => item.Id == id && item.DeletedAt == null, token);

    private Task LockClassAsync(string id, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM classes WHERE id = {id} FOR UPDATE", token);

    private static IResult MissingClass() => ClassEndpoints.Error(404, "NOT_FOUND", "Class not found.");

    private sealed record AdminHoldRow(
        string HoldId, string StudentId, string Status, DateTime CreatedAt, DateTime? ExpiresAt,
        string? CancelledBy, string? CancelReason);
}

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapDelete("/classes/{classId}", (string classId, AdminService admin, CancellationToken token) =>
            admin.DeleteClassAsync(classId, token));
        app.MapGet("/admin/classes/{classId}/holds", (string classId, AdminService admin, CancellationToken token) =>
            admin.ListHoldsAsync(classId, token));
        app.MapPost("/admin/holds/{holdId}/cancel", CancelAsync);
    }

    private static async Task<IResult> CancelAsync(
        string holdId, HttpRequest request, AdminService admin, CancellationToken token)
    {
        if (!request.HasJsonContentType())
            return ClassEndpoints.Error(400, "VALIDATION_ERROR", "Provide a JSON request body.");

        CancelRequest? input;
        try
        {
            input = await request.ReadFromJsonAsync<CancelRequest>(token);
        }
        catch (JsonException)
        {
            return ClassEndpoints.Error(400, "VALIDATION_ERROR", "Provide valid cancellation details.");
        }

        var reason = input?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500 || reason.Contains('\0'))
            return ClassEndpoints.Error(400, "VALIDATION_ERROR", "Provide a cancellation reason of 1 to 500 characters.");

        return await admin.CancelAsync(holdId, reason, input!.BlockSeat, token);
    }

    private sealed record CancelRequest(string? Reason, bool BlockSeat);
}
