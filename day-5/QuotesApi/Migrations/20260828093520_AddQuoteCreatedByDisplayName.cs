using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteCreatedByDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Quotes",
                type: "TEXT",
                nullable: true);

            // Backfill existing rows from Users where CreatedByUserId still
            // resolves to a known internal-login user id. Rows created via
            // Entra (an oid, not a Users.Id) or by a since-deleted user are
            // left null - there is no display name left to recover for them.
            migrationBuilder.Sql(@"
                UPDATE Quotes
                SET CreatedBy = (
                    SELECT Users.Email FROM Users WHERE CAST(Users.Id AS TEXT) = Quotes.CreatedByUserId
                )
                WHERE CreatedByUserId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Quotes");
        }
    }
}
