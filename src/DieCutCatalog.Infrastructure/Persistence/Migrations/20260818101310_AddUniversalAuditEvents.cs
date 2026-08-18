using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_events_employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_events_employees_ApproverEmployeeId",
                        column: x => x.ApproverEmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorEmployeeId_OccurredAt",
                table: "audit_events",
                columns: new[] { "ActorEmployeeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ApproverEmployeeId",
                table: "audit_events",
                column: "ApproverEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_CorrelationId",
                table: "audit_events",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OccurredAt",
                table: "audit_events",
                column: "OccurredAt");

            migrationBuilder.Sql(
                """
                INSERT INTO audit_events
                    ("Id", "OccurredAt", "ActorEmployeeId", "ApproverEmployeeId",
                     "EntityType", "EntityId", "Action", "BeforeJson", "AfterJson", "CorrelationId")
                SELECT
                    "Id",
                    "OccurredAt",
                    "EmployeeId",
                    NULL,
                    CASE "DestinationSection"
                        WHEN 'Материалы' THEN 'Material'
                        WHEN 'Фигуры' THEN 'Figure'
                        WHEN 'Оборудование' THEN 'Equipment'
                        ELSE 'ReferenceValue'
                    END,
                    "DestinationPositionId",
                    CASE "Type" WHEN 'Moved' THEN 'Moved' ELSE 'Copied' END,
                    jsonb_build_object(
                        'positionId', "SourcePositionId",
                        'name', "SourceName",
                        'section', "SourceSection"),
                    jsonb_build_object(
                        'positionId', "DestinationPositionId",
                        'name', "DestinationName",
                        'section', "DestinationSection"),
                    "Id"
                FROM reference_position_events;
                """);

            migrationBuilder.DropTable(
                name: "reference_position_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_position_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationSection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourcePositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_position_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reference_position_events_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reference_position_events_EmployeeId_OccurredAt",
                table: "reference_position_events",
                columns: new[] { "EmployeeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reference_position_events_OccurredAt",
                table: "reference_position_events",
                column: "OccurredAt");

            migrationBuilder.Sql(
                """
                INSERT INTO reference_position_events
                    ("Id", "EmployeeId", "Type", "SourcePositionId", "DestinationPositionId",
                     "SourceName", "DestinationName", "SourceSection", "DestinationSection", "OccurredAt")
                SELECT
                    "Id",
                    "ActorEmployeeId",
                    CASE "Action" WHEN 'Moved' THEN 'Moved' ELSE 'Copied' END,
                    COALESCE(("BeforeJson" ->> 'positionId')::uuid, "EntityId"),
                    "EntityId",
                    COALESCE("BeforeJson" ->> 'name', ''),
                    COALESCE("AfterJson" ->> 'name', ''),
                    COALESCE("BeforeJson" ->> 'section', ''),
                    COALESCE("AfterJson" ->> 'section', ''),
                    "OccurredAt"
                FROM audit_events
                WHERE "Action" IN ('Copied', 'Moved');
                """);

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
