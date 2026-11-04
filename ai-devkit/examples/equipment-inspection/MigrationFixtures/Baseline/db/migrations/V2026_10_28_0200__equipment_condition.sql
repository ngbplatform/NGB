CREATE TABLE doc_example_equipment_condition (
    document_id uuid PRIMARY KEY REFERENCES documents(id),
    display varchar(200),
    equipment_id uuid REFERENCES cat_example_equipment(catalog_id),
    condition varchar(200)
);

CREATE INDEX ix_example_equipment_condition_equipment ON doc_example_equipment_condition(equipment_id);
