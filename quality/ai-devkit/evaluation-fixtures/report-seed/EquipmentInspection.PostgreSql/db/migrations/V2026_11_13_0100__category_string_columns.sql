-- Match ColumnType.String without rewriting the original category migration.
ALTER TABLE cat_example_equipment_category
    ALTER COLUMN code TYPE text,
    ALTER COLUMN name TYPE text,
    ADD CONSTRAINT ck_equipment_category_code_length CHECK (char_length(code) <= 64),
    ADD CONSTRAINT ck_equipment_category_name_length CHECK (char_length(name) <= 200);
