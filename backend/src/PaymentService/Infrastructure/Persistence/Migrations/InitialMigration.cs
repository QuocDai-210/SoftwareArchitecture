using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwiftRide.Payments.Infrastructure;

[DbContext(typeof(PayDb))]
[Migration("202611020001_Initial")]
public sealed class InitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var reader = new StreamReader(typeof(InitialMigration).Assembly.GetManifestResourceStream("InitialSchema.sql")!);
        migrationBuilder.Sql(reader.ReadToEnd());
        migrationBuilder.Sql(
            """
            CREATE FUNCTION ledger_balanced(lines text) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$
              SELECT COALESCE(count(*) >= 2 AND
                bool_and(COALESCE(jsonb_typeof(value->'amount') = 'number' AND (value->>'amount')::numeric >= 0
                  AND (value->>'amount')::numeric = trunc((value->>'amount')::numeric)
                  AND value->>'side' IN ('Debit','Credit') AND length(value->>'account') > 0, false)) AND
                sum(CASE value->>'side' WHEN 'Debit' THEN (value->>'amount')::numeric
                  WHEN 'Credit' THEN -(value->>'amount')::numeric END) = 0, false)
              FROM jsonb_array_elements(lines::jsonb)
            $$;
            ALTER TABLE "Journals" ADD CONSTRAINT "CK_Journal_Balanced" CHECK (ledger_balanced("Lines"));
            CREATE FUNCTION ledger_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN RAISE EXCEPTION 'Ledger is append-only'; END
            $$;
            CREATE TRIGGER journal_append_only BEFORE UPDATE OR DELETE ON "Journals"
              FOR EACH ROW EXECUTE FUNCTION ledger_immutable();
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a backup instead of deleting financial data.");
}
