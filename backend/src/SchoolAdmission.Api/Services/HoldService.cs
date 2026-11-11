using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Config;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed record HoldView(string HoldId, string ClassId, string ClassName, string Status, DateTime? ExpiresAt, int? QueuePosition);

public sealed class HoldService(
    AdmissionDbContext db, ClassService classes, AppOptions options, WaitlistService waitlist, ILogger<HoldService> logger)
{
    public Task<HoldView?> GetAsync(string id, CancellationToken token) =>
        ReadHolds(null).SingleOrDefaultAsync(hold => hold.HoldId == id, token);

    public Task<List<HoldView>> ListAsync(string student, CancellationToken token) =>
        ReadHolds(student).ToListAsync(token);

    public async Task<IResult> ApplyAsync(string classId, string student, CancellationToken token, bool joinWaitlist = false)
    {
        var id = Guid.NewGuid().ToString();
        HoldEntity hold;
        int pending;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await LockStudentAsync(student, token);
            await LockClassAsync(classId, token);
            var cls = await classes.GetAsync(classId, token);
            if (cls is null)
                return Error(404, "NOT_FOUND", "Class not found.");

            var classScope = options.ScopeByClass;
            var active = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM holds h JOIN classes c ON c.id = h.class_id
                    WHERE h.student_id = {student} AND c.deleted_at IS NULL
                      AND CASE WHEN {classScope} THEN h.class_id = {classId} ELSE c.session = {cls.Session} END
                      AND (h.status IN ('CONFIRMED', 'PENDING')
                           OR (h.status = 'HELD' AND h.expires_at > clock_timestamp()))
                ) AS "Value"
                """).SingleAsync(token);

            if (active)
                return Error(409, "ALREADY_APPLIED", "This student already has an active application.");
            pending = await db.Holds.CountAsync(row => row.ClassId == classId && row.Status == "PENDING", token);
            if (joinWaitlist)
            {
                if (cls.AvailableSeats > 0)
                    return Error(409, "SEATS_AVAILABLE", "This class has an available seat.");
                if (pending >= cls.HeldSeats)
                    return Error(409, "WAITLIST_UNAVAILABLE", "No waitlist places are available.");
            }
            else if (cls.AvailableSeats <= pending)
                return Error(409, "SEAT_FULL", "This class has no available seats.");

            var duration = options.HoldDurationSeconds;
            var status = joinWaitlist ? "PENDING" : "HELD";
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO holds (id, class_id, student_id, status, created_at, expires_at)
                VALUES ({id}, {classId}, {student}, {status}, clock_timestamp(),
                        CASE WHEN {joinWaitlist} THEN NULL ELSE
                            clock_timestamp() + make_interval(secs => {(double)duration}) END)
                """, token);
            hold = await db.Holds.AsNoTracking().SingleAsync(row => row.Id == id, token);
            await transaction.CommitAsync(token);
        }

        await NotifyAsync(db, classId, logger);
        if (joinWaitlist)
            return Results.Created($"/holds/{id}", new { holdId = id, classId, studentId = student, hold.Status, queuePosition = pending + 1 });
        return Results.Created($"/holds/{id}", new
        {
            holdId = id, classId, studentId = student, hold.Status,
            expiresAt = FormatExpiry(hold.ExpiresAt)
        });
    }

    public async Task<IResult> ChangeAsync(string id, string student, bool confirm, CancellationToken token)
    {
        string classId;
        int changed;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var hold = await db.Holds.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
            if (hold is null)
                return Error(404, "NOT_FOUND", "Application not found.");
            if (hold.StudentId != student)
                return Error(403, "FORBIDDEN", "This application belongs to another student.");

            classId = hold.ClassId;
            await LockStudentAsync(student, token);
            await LockClassAsync(classId, token);
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM holds WHERE id = {id} FOR UPDATE", token);
            hold = await db.Holds.AsNoTracking().SingleAsync(row => row.Id == id, token);

            if (confirm && hold.Status == "CONFIRMED")
                return Results.Ok(new { holdId = id, status = "CONFIRMED" });
            if (hold.Status != "HELD" && (confirm || hold.Status != "PENDING"))
                return Error(409, "INVALID_STATE", "This application is no longer held.");

            changed = confirm
                ? await db.Database.ExecuteSqlAsync($"""
                    UPDATE holds SET status = 'CONFIRMED', confirmed_at = clock_timestamp()
                    WHERE id = {id} AND status = 'HELD' AND expires_at > clock_timestamp()
                    """, token)
                : await db.Database.ExecuteSqlAsync($"""
                    UPDATE holds SET status = 'CANCELLED', cancelled_at = clock_timestamp(), cancelled_by = 'STUDENT'
                    WHERE id = {id} AND (status = 'PENDING' OR (status = 'HELD' AND expires_at > clock_timestamp()))
                    """, token);
            if (changed == 0)
                await db.Database.ExecuteSqlAsync($"UPDATE holds SET status = 'EXPIRED' WHERE id = {id}", token);
            if (!confirm || changed == 0)
                await waitlist.PromoteAsync(classId, token);
            await transaction.CommitAsync(token);
        }

        await NotifyAsync(db, classId, logger);
        if (changed == 0)
            return Error(409, "HOLD_EXPIRED", "The hold has expired.");

        return confirm ? Results.Ok(new { holdId = id, status = "CONFIRMED" }) : Results.NoContent();
    }

    private IQueryable<HoldView> ReadHolds(string? student) => db.Database.SqlQuery<HoldView>($"""
        SELECT h.id AS "HoldId", h.class_id AS "ClassId", c.name AS "ClassName",
            CASE WHEN h.status = 'HELD' AND h.expires_at <= clock_timestamp()
                THEN 'EXPIRED' ELSE h.status END AS "Status",
            h.expires_at AS "ExpiresAt",
            CASE WHEN h.status = 'PENDING' THEN (
                SELECT COUNT(*)::int FROM holds p WHERE p.class_id = h.class_id AND p.status = 'PENDING'
                    AND (p.created_at, p.id) <= (h.created_at, h.id)
            ) END AS "QueuePosition"
        FROM holds h JOIN classes c ON c.id = h.class_id
        WHERE ({student}::text IS NULL OR h.student_id = {student})
        ORDER BY h.created_at DESC, h.id DESC
        """);

    private Task LockStudentAsync(string student, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({student}, 0))", token);

    private Task LockClassAsync(string id, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM classes WHERE id = {id} FOR UPDATE", token);

    internal static string? FormatExpiry(DateTime? expiresAt) =>
        expiresAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    internal static async Task NotifyAsync(AdmissionDbContext db, string payload, ILogger logger)
    {
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_notify('class_changes', {payload})");
        }
        catch (System.Data.Common.DbException exception)
        {
            logger.LogWarning(exception, "Could not publish class changes for {Payload}.", payload);
        }
    }

    private static IResult Error(int status, string code, string message) => ClassEndpoints.Error(status, code, message);
}
