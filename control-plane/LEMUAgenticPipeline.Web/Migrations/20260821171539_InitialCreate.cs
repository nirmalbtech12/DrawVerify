using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEMUAgenticPipeline.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    JiraId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IssueType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScheduleType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CurrentAgent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CommitHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PullRequestUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SelectedAgentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    StopOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduleId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScheduleType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StopOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineRunAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PipelineRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRunAgents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineRunAgents_PipelineRuns_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineScheduleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PipelineScheduleId = table.Column<int>(type: "INTEGER", nullable: false),
                    JiraId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PipelineRunId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineScheduleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineScheduleItems_PipelineRuns_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PipelineScheduleItems_PipelineSchedules_PipelineScheduleId",
                        column: x => x.PipelineScheduleId,
                        principalTable: "PipelineSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PipelineRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    PipelineRunAgentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Agent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineLogs_PipelineRunAgents_PipelineRunAgentId",
                        column: x => x.PipelineRunAgentId,
                        principalTable: "PipelineRunAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PipelineLogs_PipelineRuns_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineLogs_PipelineRunAgentId",
                table: "PipelineLogs",
                column: "PipelineRunAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineLogs_PipelineRunId",
                table: "PipelineLogs",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineLogs_Timestamp",
                table: "PipelineLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunAgents_PipelineRunId_AgentId",
                table: "PipelineRunAgents",
                columns: new[] { "PipelineRunId", "AgentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_CreatedAt",
                table: "PipelineRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_JiraId",
                table: "PipelineRuns",
                column: "JiraId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_RunId",
                table: "PipelineRuns",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_Status",
                table: "PipelineRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineScheduleItems_JiraId",
                table: "PipelineScheduleItems",
                column: "JiraId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineScheduleItems_PipelineRunId",
                table: "PipelineScheduleItems",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineScheduleItems_PipelineScheduleId",
                table: "PipelineScheduleItems",
                column: "PipelineScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineSchedules_ScheduleId",
                table: "PipelineSchedules",
                column: "ScheduleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineLogs");

            migrationBuilder.DropTable(
                name: "PipelineScheduleItems");

            migrationBuilder.DropTable(
                name: "PipelineRunAgents");

            migrationBuilder.DropTable(
                name: "PipelineSchedules");

            migrationBuilder.DropTable(
                name: "PipelineRuns");
        }
    }
}
