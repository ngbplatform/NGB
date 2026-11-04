-- ColumnType.String uses the provider's text contract. Retain the database
-- length limits while bringing the physical types into that contract.
ALTER TABLE cat_example_equipment
    ALTER COLUMN display TYPE text,
    ADD CONSTRAINT ck_example_equipment_display_length CHECK (char_length(display) <= 200);

ALTER TABLE doc_example_inspection
    ALTER COLUMN display TYPE text,
    ALTER COLUMN result TYPE text,
    ADD CONSTRAINT ck_example_inspection_display_length CHECK (char_length(display) <= 200),
    ADD CONSTRAINT ck_example_inspection_result_length CHECK (char_length(result) <= 2000);

ALTER TABLE doc_example_equipment_usage
    ALTER COLUMN display TYPE text,
    ADD CONSTRAINT ck_example_equipment_usage_display_length CHECK (char_length(display) <= 200);

ALTER TABLE doc_example_equipment_condition
    ALTER COLUMN display TYPE text,
    ALTER COLUMN condition TYPE text,
    ADD CONSTRAINT ck_example_equipment_condition_display_length CHECK (char_length(display) <= 200),
    ADD CONSTRAINT ck_example_equipment_condition_value_length CHECK (char_length(condition) <= 200);

ALTER TABLE doc_example_equipment_service
    ALTER COLUMN display TYPE text,
    ALTER COLUMN condition TYPE text,
    ADD CONSTRAINT ck_example_equipment_service_display_length CHECK (char_length(display) <= 200),
    ADD CONSTRAINT ck_example_equipment_service_condition_length CHECK (char_length(condition) <= 200);

ALTER TABLE doc_example_checklist
    ALTER COLUMN display TYPE text,
    ADD CONSTRAINT ck_example_checklist_display_length CHECK (char_length(display) <= 200);

ALTER TABLE doc_example_checklist_checks
    ALTER COLUMN description TYPE text,
    ADD CONSTRAINT ck_example_checklist_description_length CHECK (char_length(description) <= 200);
