using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConvertSubscriptionPlansToCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapabilitiesJson",
                table: "SubscriptionPlans",
                type: "longtext",
                nullable: false,
                defaultValue: "{}")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "AspNetUsers",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SubscriptionPlanId",
                table: "AspNetUsers",
                column: "SubscriptionPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_SubscriptionPlans_SubscriptionPlanId",
                table: "AspNetUsers",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                UPDATE `AspNetUsers` AS `u`
                INNER JOIN `UserTokenAllowances` AS `a` ON `a`.`UserId` = `u`.`Id`
                SET `u`.`SubscriptionPlanId` = `a`.`SubscriptionPlanId`;
                """);

            migrationBuilder.DropTable(
                name: "UserTokenAllowances");

            migrationBuilder.DropColumn(
                name: "CanCreateOrganizations",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "CanCreateProjects",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "MonthlyCostLimitUsd",
                table: "SubscriptionPlans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_SubscriptionPlans_SubscriptionPlanId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SubscriptionPlanId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CapabilitiesJson",
                table: "SubscriptionPlans");

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateOrganizations",
                table: "SubscriptionPlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateProjects",
                table: "SubscriptionPlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyCostLimitUsd",
                table: "SubscriptionPlans",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "UserTokenAllowances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionPlanId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CurrentMonth = table.Column<int>(type: "int", nullable: false),
                    CurrentMonthCostUsd = table.Column<decimal>(type: "decimal(10,6)", nullable: false, defaultValue: 0m),
                    CurrentMonthInputTokens = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CurrentMonthOutputTokens = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CurrentYear = table.Column<int>(type: "int", nullable: false),
                    MonthlyCostLimitUsd = table.Column<decimal>(type: "decimal(10,6)", nullable: false, defaultValue: 10.00m),
                    OverageAction = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokenAllowances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokenAllowances_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTokenAllowances_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserTokenAllowances_SubscriptionPlanId",
                table: "UserTokenAllowances",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTokenAllowances_UserId",
                table: "UserTokenAllowances",
                column: "UserId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO `UserTokenAllowances`
                    (`Id`, `SubscriptionPlanId`, `UserId`, `CurrentMonth`, `CurrentMonthCostUsd`,
                     `CurrentMonthInputTokens`, `CurrentMonthOutputTokens`, `CurrentYear`,
                     `MonthlyCostLimitUsd`, `OverageAction`, `UpdatedAt`)
                SELECT UUID(), `SubscriptionPlanId`, `Id`, MONTH(UTC_TIMESTAMP()), 0,
                       0, 0, YEAR(UTC_TIMESTAMP()), 0, 'downgrade', UTC_TIMESTAMP()
                FROM `AspNetUsers`
                WHERE `SubscriptionPlanId` IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "AspNetUsers");
        }
    }
}
