using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePublicUsernameRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Generate public usernames for existing users without one
                        migrationBuilder.Sql(
                                @"UPDATE AspNetUsers 
                                    SET PublicUsername = LEFT(CONCAT(
                                        LOWER(SUBSTRING(Name, 1, 1)),
                                        LOWER(SUBSTRING(Name, 2, 24)),
                                        '-',
                                        LOWER(HEX(SUBSTR(RAND(), 3, 6)))
                                    ), 32)
                                    WHERE PublicUsername IS NULL;"
                        );

            // Make PublicUsername NOT NULL with unique constraint
            migrationBuilder.AlterColumn<string>(
                name: "PublicUsername",
                table: "AspNetUsers",
                type: "VARCHAR(32)",
                maxLength: 32,
                nullable: false,
                collation: "utf8mb4_general_ci",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Add unique index on PublicUsername
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PublicUsername",
                table: "AspNetUsers",
                column: "PublicUsername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique index
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PublicUsername",
                table: "AspNetUsers");

            // Revert to nullable
            migrationBuilder.AlterColumn<string>(
                name: "PublicUsername",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(32)",
                oldMaxLength: 32,
                oldNullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
