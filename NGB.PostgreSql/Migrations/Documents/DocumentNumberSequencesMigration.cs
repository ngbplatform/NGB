using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Stores last issued sequential number per (document type, fiscal year).
///
/// We intentionally do NOT use PostgreSQL sequences (nextval) because sequences advance
/// outside the current transaction and would create number gaps on rollback.
/// </summary>
public sealed class DocumentNumberSequencesMigration : IDdlObject
{
    public string Name => "documents_number_sequences";

    public string Sql => """
                         CREATE TABLE IF NOT EXISTS document_number_sequences
                         (
                             type_code   text   NOT NULL,
                             fiscal_year integer NOT NULL,
                             last_seq    bigint NOT NULL,

                             CONSTRAINT pk_document_number_sequences
                                 PRIMARY KEY (type_code, fiscal_year),

                             CONSTRAINT ck_document_number_sequences_fiscal_year
                                 CHECK (fiscal_year >= 1900 AND fiscal_year <= 3000),

                             CONSTRAINT ck_document_number_sequences_last_seq
                                 CHECK (last_seq >= 1)
                         );
                         """;

    public string Generate() => Sql;
}
