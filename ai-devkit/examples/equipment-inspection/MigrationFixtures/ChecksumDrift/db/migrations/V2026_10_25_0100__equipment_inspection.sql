CREATE TABLE cat_example_equipment (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
    display varchar(200) NOT NULL
);

CREATE TABLE doc_example_inspection (
    document_id uuid PRIMARY KEY REFERENCES documents(id),
    display varchar(200),
    equipment_id uuid REFERENCES cat_example_equipment(catalog_id),
    result varchar(2000)
);

CREATE INDEX ix_example_inspection_equipment ON doc_example_inspection(equipment_id);

-- Deliberate checksum mismatch used only by the migration acceptance fixture.
