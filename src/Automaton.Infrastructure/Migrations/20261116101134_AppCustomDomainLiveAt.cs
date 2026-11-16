using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppCustomDomainLiveAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveAt",
                table: "AppCustomDomains",
                type: "timestamp with time zone",
                nullable: true);

            // Domains already acting as the primary address stay so; the background check keeps them honest.
            migrationBuilder.Sql("UPDATE \"AppCustomDomains\" SET \"LiveAt\" = \"VerifiedAt\" WHERE \"IsPrimary\" AND \"VerifiedAt\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiveAt",
                table: "AppCustomDomains");
        }
    }
}
