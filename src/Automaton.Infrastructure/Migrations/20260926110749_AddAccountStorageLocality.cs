using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountStorageLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageFilesystemUuid",
                table: "AccountStorageAllocations",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageRunnerHostId",
                table: "AccountStorageAllocations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageFilesystemUuid",
                table: "AccountStorageAllocations");

            migrationBuilder.DropColumn(
                name: "StorageRunnerHostId",
                table: "AccountStorageAllocations");
        }
    }
}
