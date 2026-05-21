using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillComboColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComboType",
                schema: "config",
                table: "Skill",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ComboElement",
                schema: "config",
                table: "Skill",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrimeDurationMs",
                schema: "config",
                table: "Skill",
                type: "integer",
                nullable: false,
                defaultValue: 5000);

            migrationBuilder.AddColumn<float>(
                name: "DetonationRadius",
                schema: "config",
                table: "Skill",
                type: "real",
                nullable: false,
                defaultValue: 3.0f);

            migrationBuilder.AddColumn<float>(
                name: "DamageMultiplier",
                schema: "config",
                table: "Skill",
                type: "real",
                nullable: false,
                defaultValue: 3.0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ComboType", schema: "config", table: "Skill");
            migrationBuilder.DropColumn(name: "ComboElement", schema: "config", table: "Skill");
            migrationBuilder.DropColumn(name: "PrimeDurationMs", schema: "config", table: "Skill");
            migrationBuilder.DropColumn(name: "DetonationRadius", schema: "config", table: "Skill");
            migrationBuilder.DropColumn(name: "DamageMultiplier", schema: "config", table: "Skill");
        }
    }
}
