using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiles.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyConsentImmutabilityTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_consent_record_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'UPDATE operations are not allowed on consent_records table. Consent records are immutable for audit trail purposes.';
                    ELSIF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'DELETE operations are not allowed on consent_records table. Consent records are immutable for audit trail purposes.';
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS prevent_consent_record_update ON consent_records;
                CREATE TRIGGER prevent_consent_record_update
                    BEFORE UPDATE ON consent_records
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_consent_record_modification();

                DROP TRIGGER IF EXISTS prevent_consent_record_delete ON consent_records;
                CREATE TRIGGER prevent_consent_record_delete
                    BEFORE DELETE ON consent_records
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_consent_record_modification();

                COMMENT ON TABLE consent_records IS 'Immutable audit trail of user consent. INSERT only - UPDATE and DELETE are blocked by trigger.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS prevent_consent_record_update ON consent_records;
                DROP TRIGGER IF EXISTS prevent_consent_record_delete ON consent_records;
                DROP FUNCTION IF EXISTS prevent_consent_record_modification();
                """);
        }
    }
}
