using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolAdmission.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    session = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    blocked_seats = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "clock_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classes", x => x.id);
                    table.CheckConstraint("ck_classes_blocked_seats_limit", "blocked_seats <= total_seats");
                    table.CheckConstraint("ck_classes_blocked_seats_nonnegative", "blocked_seats >= 0");
                    table.CheckConstraint("ck_classes_total_seats", "total_seats >= 1");
                });

            migrationBuilder.CreateTable(
                name: "holds",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "character(17)", fixedLength: true, maxLength: 17, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "clock_timestamp()"),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holds", x => x.id);
                    table.CheckConstraint("ck_holds_status", "status IN ('PENDING','HELD','CONFIRMED','EXPIRED','CANCELLED')");
                    table.ForeignKey(
                        name: "FK_holds_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holds_class_status_exp",
                table: "holds",
                columns: new[] { "class_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_student",
                table: "holds",
                columns: new[] { "student_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_holds_sweep",
                table: "holds",
                column: "expires_at",
                filter: "status = 'HELD'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holds");

            migrationBuilder.DropTable(
                name: "classes");
        }
    }
}
