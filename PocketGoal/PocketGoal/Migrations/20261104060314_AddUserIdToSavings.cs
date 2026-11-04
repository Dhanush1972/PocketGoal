using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PocketGoal.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Savings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Savings_UserId",
                table: "Savings",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Savings_UserProfiles_UserId",
                table: "Savings",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(@"
                UPDATE ""Savings"" s
                SET ""UserId"" = g.""UserId""
                FROM ""SavingGoals"" g
                WHERE s.""SavingGoalId"" = g.""Id"" AND s.""UserId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Savings_UserProfiles_UserId",
                table: "Savings");

            migrationBuilder.DropIndex(
                name: "IX_Savings_UserId",
                table: "Savings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Savings");
        }
    }
}
