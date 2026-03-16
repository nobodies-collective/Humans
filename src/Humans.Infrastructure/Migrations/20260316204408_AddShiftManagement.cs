using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_outbox_messages_duty_signups_DutySignupId",
                table: "email_outbox_messages");

            migrationBuilder.DropTable(
                name: "duty_signups");

            migrationBuilder.RenameColumn(
                name: "DutySignupId",
                table: "email_outbox_messages",
                newName: "ShiftSignupId");

            migrationBuilder.RenameIndex(
                name: "IX_email_outbox_messages_DutySignupId",
                table: "email_outbox_messages",
                newName: "IX_email_outbox_messages_ShiftSignupId");

            migrationBuilder.CreateTable(
                name: "shift_signups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Enrolled = table.Column<bool>(type: "boolean", nullable: false),
                    EnrolledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_signups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shift_signups_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_signups_users_EnrolledByUserId",
                        column: x => x.EnrolledByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_shift_signups_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_shift_signups_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shift_signups_EnrolledByUserId",
                table: "shift_signups",
                column: "EnrolledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_signups_ReviewedByUserId",
                table: "shift_signups",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_signups_ShiftId",
                table: "shift_signups",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_signups_ShiftId_Status",
                table: "shift_signups",
                columns: new[] { "ShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_shift_signups_UserId",
                table: "shift_signups",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_email_outbox_messages_shift_signups_ShiftSignupId",
                table: "email_outbox_messages",
                column: "ShiftSignupId",
                principalTable: "shift_signups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_outbox_messages_shift_signups_ShiftSignupId",
                table: "email_outbox_messages");

            migrationBuilder.DropTable(
                name: "shift_signups");

            migrationBuilder.RenameColumn(
                name: "ShiftSignupId",
                table: "email_outbox_messages",
                newName: "DutySignupId");

            migrationBuilder.RenameIndex(
                name: "IX_email_outbox_messages_ShiftSignupId",
                table: "email_outbox_messages",
                newName: "IX_email_outbox_messages_DutySignupId");

            migrationBuilder.CreateTable(
                name: "duty_signups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrolledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Enrolled = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duty_signups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_duty_signups_shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_duty_signups_users_EnrolledByUserId",
                        column: x => x.EnrolledByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duty_signups_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duty_signups_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_duty_signups_EnrolledByUserId",
                table: "duty_signups",
                column: "EnrolledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_duty_signups_ReviewedByUserId",
                table: "duty_signups",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_duty_signups_ShiftId",
                table: "duty_signups",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_duty_signups_ShiftId_Status",
                table: "duty_signups",
                columns: new[] { "ShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_duty_signups_UserId",
                table: "duty_signups",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_email_outbox_messages_duty_signups_DutySignupId",
                table: "email_outbox_messages",
                column: "DutySignupId",
                principalTable: "duty_signups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
