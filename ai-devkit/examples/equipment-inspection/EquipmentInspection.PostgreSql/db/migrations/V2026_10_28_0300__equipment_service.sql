CREATE TABLE doc_example_equipment_service (
    document_id uuid PRIMARY KEY REFERENCES documents(id),
    display varchar(200),
    equipment_id uuid REFERENCES cat_example_equipment(catalog_id),
    hours numeric(18,4),
    amount numeric(18,4),
    condition varchar(200)
);

CREATE INDEX ix_example_equipment_service_equipment ON doc_example_equipment_service(equipment_id);
