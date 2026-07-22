using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretDeliveryPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                table: "ExecutionSecretGrants",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPolicy",
                table: "ExecutionSecretGrants",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "singleDelivery")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "ExecutionSecretGrants");

            migrationBuilder.DropColumn(
                name: "DeliveryPolicy",
                table: "ExecutionSecretGrants");
        }
    }
}
