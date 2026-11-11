using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed record ClassSeatRow(
    string Id, string Name, string Session, int TotalSeats, int BlockedSeats,
    int HeldSeats, int ConfirmedSeats)
{
    public int AvailableSeats => Math.Max(0, TotalSeats - HeldSeats - ConfirmedSeats - BlockedSeats);
}

public sealed class ClassService(AdmissionDbContext db, ILogger<ClassService> logger)
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
            await db.Database.ExecuteSqlAsync($"""
                SELECT pg_advisory_xact_lock(hashtextextended(
                    {session} || '|' || lower(btrim(regexp_replace({name}, '[[:space:]]+', ' ', 'g'))), 1))
                """, token);

            var existing = await ReadClasses(null, session, name).FirstOrDefaultAsync(token);
            if (existing is not null)
                return (null, existing);

            db.Classes.Add(item);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        try
        {
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_notify('class_changes', {item.Id})", CancellationToken.None);
        }
        catch (System.Data.Common.DbException exception)
        {
            logger.LogWarning(exception, "Could not publish the creation of class {ClassId}.", item.Id);
        }

        return (item, null);
    }

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
