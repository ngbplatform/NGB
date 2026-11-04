CREATE TABLE doc_example_equipment_usage (
    document_id uuid PRIMARY KEY REFERENCES documents(id),
    display varchar(200),
    equipment_id uuid REFERENCES cat_example_equipment(catalog_id),
    hours numeric(18,4)
);

CREATE INDEX ix_example_equipment_usage_equipment ON doc_example_equipment_usage(equipment_id);

INSERT INTO platform_dimensions(dimension_id, code, name)
VALUES ('08abcdd4-dd60-5e95-a39e-1b79dc835510', 'example.equipment', 'Equipment');
