using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxAngelos.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeExistingPhoneNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill for accounts created before RegisterModel.NormalizePhone() existed:
            // their PhoneNumber was stored as whatever the form posted (bare 10 digits,
            // no "+63"), which no longer matches the canonical "+63XXXXXXXXXX" shape the
            // duplicate-check queries now compare against. Mirrors NormalizePhone() exactly
            // (strip non-digits, drop a leading "63" or "0", prepend "+63") so old and new
            // rows land in the same format and duplicate detection covers legacy accounts too.
            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers""
                SET ""PhoneNumber"" = '+63' ||
                    CASE
                        WHEN left(regexp_replace(""PhoneNumber"", '\D', '', 'g'), 2) = '63'
                            THEN substring(regexp_replace(""PhoneNumber"", '\D', '', 'g') from 3)
                        WHEN left(regexp_replace(""PhoneNumber"", '\D', '', 'g'), 1) = '0'
                            THEN substring(regexp_replace(""PhoneNumber"", '\D', '', 'g') from 2)
                        ELSE regexp_replace(""PhoneNumber"", '\D', '', 'g')
                    END
                WHERE ""PhoneNumber"" IS NOT NULL
                  AND ""PhoneNumber"" != ''
                  AND ""PhoneNumber"" NOT LIKE '+63%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the original (unnormalized) formatting isn't recoverable
            // once digits have been stripped and reassembled.
        }
    }
}
