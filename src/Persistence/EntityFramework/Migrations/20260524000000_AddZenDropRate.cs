using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <summary>
    /// Adds <c>ZenDropRate</c> to <c>config.GameConfiguration</c>. Defaults to
    /// <c>1.0</c> so existing rows preserve the legacy "zen scales with exp"
    /// feel until an admin tunes it. Seeding with the current
    /// <c>ExperienceRate</c> keeps zen drops identical to what players saw
    /// before <c>ZenDropRate</c> was introduced; admins can then dial it
    /// independently from the admin panel.
    /// </summary>
    /// <inheritdoc />
    [DbContext(typeof(EntityDataContext))]
    [Migration("20260524000000_AddZenDropRate")]
    public partial class AddZenDropRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "ZenDropRate",
                schema: "config",
                table: "GameConfiguration",
                type: "real",
                nullable: false,
                defaultValue: 1f);

            migrationBuilder.Sql("""UPDATE config."GameConfiguration" SET "ZenDropRate" = "ExperienceRate";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ZenDropRate",
                schema: "config",
                table: "GameConfiguration");
        }
    }
}
