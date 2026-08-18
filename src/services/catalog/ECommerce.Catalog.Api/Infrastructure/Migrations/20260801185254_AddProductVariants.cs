using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Catalog.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Unisex", not EF's scaffolded "". The column stores the enum's NAME, so an empty string is not
            // a value Audience can parse - every pre-existing row would throw on read. A backfill default has
            // to be a real member of the type it is backfilling.
            migrationBuilder.AddColumn<string>(
                name: "audience",
                table: "products",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Unisex");

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    colour_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    colour_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    stock_on_hand = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_audience",
                table: "products",
                column: "audience");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_colour_name",
                table: "product_variants",
                column: "colour_name");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_size",
                table: "product_variants",
                column: "size");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_sku",
                table: "product_variants",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropIndex(
                name: "ix_products_audience",
                table: "products");

            migrationBuilder.DropColumn(
                name: "audience",
                table: "products");
        }
    }
}
