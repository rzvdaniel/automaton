using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<bool>(
                name: "CanUseCustomRunners",
                table: "SubscriptionPlans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxApps",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxCustomSteps",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxFlows",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanCreateOrganizations",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "CanCreateProjects",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "CanUseCustomRunners",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "MaxApps",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "MaxCustomSteps",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "MaxFlows",
                table: "SubscriptionPlans");
        }
    }
}
