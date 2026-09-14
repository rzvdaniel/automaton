using Microsoft.EntityFrameworkCore.Migrations;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Automaton.Infrastructure.Migrations;

/// <summary>
/// Releases host runtime ports retained by Hosted Runner rows that are already
/// in the canonical deleted state. The row and its history remain intact.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914110000_ReleaseDeletedHostedRunnerRuntimePorts")]
public partial class ReleaseDeletedHostedRunnerRuntimePorts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE `HostedRunners`
            SET `RuntimePort` = NULL
            WHERE `Status` = 'deleted'
              AND `DeletedAt` IS NOT NULL
              AND `RunnerId` IS NULL
              AND `ContainerId` IS NULL
              AND `RuntimeEndpoint` IS NULL
              AND `RuntimePort` IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Runtime ports are released data, and their historical values are not
        // recoverable without reintroducing a live allocation.
    }
}
