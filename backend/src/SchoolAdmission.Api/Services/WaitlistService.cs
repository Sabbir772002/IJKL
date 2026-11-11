using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Config;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public sealed class WaitlistService(AdmissionDbContext db, AppOptions options)
{
    public Task<int> PromoteAsync(string classId, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"""
            WITH capacity AS (
                SELECT GREATEST(0, c.total_seats - c.blocked_seats - COUNT(h.id)) AS free
                FROM classes c LEFT JOIN holds h ON h.class_id = c.id AND
                    (h.status = 'CONFIRMED' OR (h.status = 'HELD' AND h.expires_at > clock_timestamp()))
                WHERE c.id = {classId} AND c.deleted_at IS NULL
                GROUP BY c.id
            ), next_holds AS (
                SELECT id FROM holds WHERE class_id = {classId} AND status = 'PENDING'
                ORDER BY created_at, id LIMIT COALESCE((SELECT free FROM capacity), 0)
            )
            UPDATE holds SET status = 'HELD',
                expires_at = clock_timestamp() + make_interval(secs => {(double)options.HoldDurationSeconds})
            WHERE id IN (SELECT id FROM next_holds)
            """, token);
}
