using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Catalog.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorySizeScales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "size_scale_id",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "size_scales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_size_scales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "size_scale_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_scale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_size_scale_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_size_scale_values_size_scales_size_scale_id",
                        column: x => x.size_scale_id,
                        principalTable: "size_scales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_size_scale_id",
                table: "categories",
                column: "size_scale_id");

            migrationBuilder.CreateIndex(
                name: "ix_size_scale_values_size_scale_id_position",
                table: "size_scale_values",
                columns: new[] { "size_scale_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_size_scale_values_size_scale_id_value",
                table: "size_scale_values",
                columns: new[] { "size_scale_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_size_scales_slug",
                table: "size_scales",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_categories_size_scales_size_scale_id",
                table: "categories",
                column: "size_scale_id",
                principalTable: "size_scales",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_categories_size_scales_size_scale_id",
                table: "categories");

            migrationBuilder.DropTable(
                name: "size_scale_values");

            migrationBuilder.DropTable(
                name: "size_scales");

            migrationBuilder.DropIndex(
                name: "ix_categories_size_scale_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "size_scale_id",
                table: "categories");
        }
    }
}
