using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations;

[DbContext(typeof(Automaton.Api.Data.ApplicationDbContext))]
[Migration("20261102200000_AddMustChangePassword")]
public sealed class AddMustChangePassword : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "MustChangePassword",
            table: "AspNetUsers",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MustChangePassword", table: "AspNetUsers");
    }
}
