using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PocketGoal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ColorHex = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentSavedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingGoals_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Savings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SavingGoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SavedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Savings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Savings_SavingGoals_SavingGoalId",
                        column: x => x.SavingGoalId,
                        principalTable: "SavingGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "ColorHex", "CreatedAt", "Icon", "Name", "UserId" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), "#f59e0b", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-egg-fried", "Food & Dining", null },
                    { new Guid("11111111-1111-1111-1111-111111111102"), "#3b82f6", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-airplane", "Travel & Commute", null },
                    { new Guid("11111111-1111-1111-1111-111111111103"), "#ef4444", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-fuel-pump", "Fuel & Vehicle", null },
                    { new Guid("11111111-1111-1111-1111-111111111104"), "#ec4899", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-bag-check", "Shopping", null },
                    { new Guid("11111111-1111-1111-1111-111111111105"), "#8b5cf6", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-film", "Entertainment & Movies", null },
                    { new Guid("11111111-1111-1111-1111-111111111106"), "#10b981", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-lightning-charge", "Bills & Utilities", null },
                    { new Guid("11111111-1111-1111-1111-111111111107"), "#6366f1", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-play-btn", "Subscriptions", null },
                    { new Guid("11111111-1111-1111-1111-111111111108"), "#14b8a6", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-heart-pulse", "Health & Medical", null },
                    { new Guid("11111111-1111-1111-1111-111111111109"), "#0284c7", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-book", "Education", null },
                    { new Guid("11111111-1111-1111-1111-111111111110"), "#64748b", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bi-three-dots", "Other", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_UserId",
                table: "ExpenseCategories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_UserId",
                table: "Expenses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingGoals_UserId",
                table: "SavingGoals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Savings_SavingGoalId",
                table: "Savings",
                column: "SavingGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Email",
                table: "UserProfiles",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_PhoneNumber",
                table: "UserProfiles",
                column: "PhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Savings");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "SavingGoals");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
