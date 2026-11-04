ALTER TABLE cat_example_equipment
    ADD COLUMN category_id uuid REFERENCES cat_example_equipment_category(catalog_id);

CREATE INDEX ix_example_equipment_category ON cat_example_equipment(category_id);
