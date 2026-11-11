using Microsoft.EntityFrameworkCore;

namespace SchoolAdmission.Api.Data;

public sealed class AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : DbContext(options)
{
    public DbSet<ClassEntity> Classes => Set<ClassEntity>();
    public DbSet<HoldEntity> Holds => Set<HoldEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassEntity>(entity =>
        {
            entity.ToTable("classes", table =>
            {
                table.HasCheckConstraint("ck_classes_total_seats", "total_seats >= 1");
                table.HasCheckConstraint("ck_classes_blocked_seats_nonnegative", "blocked_seats >= 0");
                table.HasCheckConstraint("ck_classes_blocked_seats_limit", "blocked_seats <= total_seats");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            entity.Property(item => item.TotalSeats).HasColumnName("total_seats");
            entity.Property(item => item.Session).HasColumnName("session").HasMaxLength(32).IsRequired();
            entity.Property(item => item.BlockedSeats).HasColumnName("blocked_seats");
            entity.Property(item => item.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            entity.Property(item => item.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("clock_timestamp()");
        });

        modelBuilder.Entity<HoldEntity>(entity =>
        {
            entity.ToTable("holds", table =>
                table.HasCheckConstraint(
                    "ck_holds_status",
                    "status IN ('PENDING','HELD','CONFIRMED','EXPIRED','CANCELLED')"));

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ClassId).HasColumnName("class_id");
            entity.Property(item => item.StudentId)
                .HasColumnName("student_id")
                .HasMaxLength(17)
                .IsFixedLength()
                .IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(item => item.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("clock_timestamp()");
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");
            entity.Property(item => item.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamptz");
            entity.Property(item => item.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
            entity.Property(item => item.CancelledBy).HasColumnName("cancelled_by").HasMaxLength(16);
            entity.Property(item => item.CancelReason).HasColumnName("cancel_reason").HasMaxLength(500);

            entity.HasOne(item => item.Class)
                .WithMany(item => item.Holds)
                .HasForeignKey(item => item.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(item => new { item.ClassId, item.Status, item.ExpiresAt })
                .HasDatabaseName("ix_holds_class_status_exp");
            entity.HasIndex(item => new { item.StudentId, item.CreatedAt })
                .HasDatabaseName("ix_holds_student");
            entity.HasIndex(item => item.ExpiresAt)
                .HasDatabaseName("ix_holds_sweep")
                .HasFilter("status = 'HELD'");
        });
    }
}
