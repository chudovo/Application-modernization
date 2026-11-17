using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        // The tables already exist: Entity Framework 6 created them. The generated CREATE TABLE statements were
        // deleted, so applying this migration only records it in __EFMigrationsHistory. The model snapshot next to this
        // file describes the existing database, and every later migration is a difference from it.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        // Nothing to undo. The tables do not belong to this migration.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
