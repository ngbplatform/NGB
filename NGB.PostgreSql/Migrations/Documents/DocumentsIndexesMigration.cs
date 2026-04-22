using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

public sealed class DocumentsIndexesMigration : IDdlObject
{
    public string Name => "documents_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_documents_type_date
                                    ON documents(type_code, date_utc);

                                CREATE INDEX IF NOT EXISTS ix_documents_status
                                    ON documents(status);

                                CREATE INDEX IF NOT EXISTS ix_documents_number
                                    ON documents(number);

                                -- Enforce uniqueness of numbering per document type when the number is set.
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_type_number_not_null
                                    ON documents(type_code, number)
                                    WHERE number IS NOT NULL;
                                """;
}
