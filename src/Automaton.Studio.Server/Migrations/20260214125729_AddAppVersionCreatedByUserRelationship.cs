using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAppVersionCreatedByUserRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppVersions_CreatedBy",
                table: "AppVersions",
                column: "CreatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_AppVersions_AspNetUsers_CreatedBy",
                table: "AppVersions",
                column: "CreatedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppVersions_AspNetUsers_CreatedBy",
                table: "AppVersions");

            migrationBuilder.DropIndex(
                name: "IX_AppVersions_CreatedBy",
                table: "AppVersions");
        }
    }
}
