using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class MigrateToUtcTimestamps : Migration
    {
        /// <summary>
        /// Converts every <c>timestamp without time zone</c> column in the public schema to
        /// <c>timestamp with time zone</c> (a.k.a. <c>timestamptz</c>), interpreting the
        /// stored value as UTC.  This closes issue #13 and removes the Npgsql legacy
        /// timestamp AppContext switch that was masking the type mismatch.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE
    col RECORD;
BEGIN
    FOR col IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND data_type = 'timestamp without time zone'
        ORDER BY table_name, column_name
    LOOP
        EXECUTE format(
            'ALTER TABLE %I ALTER COLUMN %I TYPE timestamptz USING %I AT TIME ZONE ''UTC''',
            col.table_name, col.column_name, col.column_name
        );
    END LOOP;
END;
$$;
            ");
        }

        /// <summary>
        /// Reverts all <c>timestamptz</c> columns back to <c>timestamp without time zone</c>,
        /// stripping timezone info (values remain numerically identical — they were always UTC).
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE
    col RECORD;
BEGIN
    FOR col IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND data_type = 'timestamp with time zone'
        ORDER BY table_name, column_name
    LOOP
        EXECUTE format(
            'ALTER TABLE %I ALTER COLUMN %I TYPE timestamp without time zone USING %I AT TIME ZONE ''UTC''',
            col.table_name, col.column_name, col.column_name
        );
    END LOOP;
END;
$$;
            ");
        }
    }
}
