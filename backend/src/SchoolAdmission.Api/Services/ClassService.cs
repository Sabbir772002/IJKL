using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed record ClassSeatRow(
    string Id, string Name, string Session, int TotalSeats, int BlockedSeats,
    int HeldSeats, int ConfirmedSeats)
{
    public int AvailableSeats => Math.Max(0, TotalSeats - HeldSeats - ConfirmedSeats - BlockedSeats);
}

public sealed class ClassService(AdmissionDbContext db, WaitlistService waitlist, ILogger<ClassService> logger)
{
    public Task<List<ClassSeatRow>> ListAsync(string? session, CancellationToken token) =>
        ReadClasses(null, session, null).ToListAsync(token);

    public Task<ClassSeatRow?> GetAsync(string id, CancellationToken token) =>
        ReadClasses(id, null, null).SingleOrDefaultAsync(token);

    public async Task<(ClassEntity? Created, ClassSeatRow? Existing)> CreateAsync(
        string name, string session, int totalSeats, CancellationToken token)
    {
        var item = new ClassEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Session = session,
            TotalSeats = totalSeats
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await LockNameAsync(session, name, token);

            var existing = await ReadClasses(null, session, name).FirstOrDefaultAsync(token);
            if (existing is not null)
                return (null, existing);

            db.Classes.Add(item);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        await HoldService.NotifyAsync(db, item.Id, logger);
        return (item, null);
    }

    public async Task<IResult> EditAsync(string id, string? name, int? totalSeats, int? blockedSeats, CancellationToken token)
    {
        ClassSeatRow updated;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM classes WHERE id = {id} FOR UPDATE", token);
            var current = await GetAsync(id, token);
            if (current is null)
                return ClassEndpoints.Error(404, "NOT_FOUND", "Class not found.");

            var capacity = totalSeats ?? current.TotalSeats;
            var occupied = current.HeldSeats + current.ConfirmedSeats;
            if (capacity < occupied || blockedSeats < 0 || blockedSeats > capacity - occupied)
                return ClassEndpoints.Error(409, "SEATS_BELOW_OCCUPIED", "Seat count cannot be below occupied seats.");

            var newName = name ?? current.Name;
            if (name is not null)
            {
                await LockNameAsync(current.Session, name, token);
                var existing = await ReadClasses(null, current.Session, name)
                    .FirstOrDefaultAsync(item => item.Id != id, token);
                if (existing is not null)
                    return ClassEndpoints.Duplicate(existing);
            }

            var blocked = blockedSeats ?? Math.Min(current.BlockedSeats, capacity - occupied);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE classes SET name = {newName}, total_seats = {capacity}, blocked_seats = {blocked}
                WHERE id = {id}
                """, token);
            updated = (await GetAsync(id, token))!;
            if (updated.AvailableSeats > current.AvailableSeats)
            {
                await waitlist.PromoteAsync(id, token);
                updated = (await GetAsync(id, token))!;
            }
            await transaction.CommitAsync(token);
        }

        await HoldService.NotifyAsync(db, id, logger);
        return Results.Ok(updated);
    }

    private Task LockNameAsync(string session, string name, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended(
                {session} || '|' || lower(btrim(regexp_replace({name}, '[[:space:]]+', ' ', 'g'))), 1))
            """, token);

    private IQueryable<ClassSeatRow> ReadClasses(string? id, string? session, string? name) =>
        db.Database.SqlQuery<ClassSeatRow>($"""
            SELECT c.id AS "Id", c.name AS "Name", c.session AS "Session",
                c.total_seats AS "TotalSeats", c.blocked_seats AS "BlockedSeats",
                COUNT(h.id) FILTER (WHERE h.status = 'HELD' AND h.expires_at > clock_timestamp())::int AS "HeldSeats",
                COUNT(h.id) FILTER (WHERE h.status = 'CONFIRMED')::int AS "ConfirmedSeats"
            FROM classes c
            LEFT JOIN holds h ON h.class_id = c.id
            WHERE c.deleted_at IS NULL
                AND ({id}::text IS NULL OR c.id = {id})
                AND ({session}::text IS NULL OR c.session = {session})
                AND ({name}::text IS NULL OR
                    lower(btrim(regexp_replace(c.name, '[[:space:]]+', ' ', 'g'))) =
                    lower(btrim(regexp_replace({name}::text, '[[:space:]]+', ' ', 'g'))))
            GROUP BY c.id
            ORDER BY c.created_at, c.id
            """);
}
