using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927121500_RequireVerifiedRunnerStorageLayout")]
public sealed class RequireVerifiedRunnerStorageLayout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE RunnerHosts " +
            "SET StorageState = 'Unknown', " +
            "StorageMeasurementStatus = 'Stale', " +
            "StorageMeasurementError = 'Awaiting Docker image/cache filesystem verification.', " +
            "StorageMeasuredAtUtc = NULL, " +
            "StorageReceivedAtUtc = NULL " +
            "WHERE ProvisioningTransport = 'infrastructureRunner'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Prior telemetry cannot be safely restored as verified after this migration.
    }
}
