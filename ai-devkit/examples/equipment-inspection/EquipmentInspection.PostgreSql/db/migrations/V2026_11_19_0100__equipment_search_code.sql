ALTER TABLE cat_example_equipment
    ADD COLUMN equipment_code text,
    ADD CONSTRAINT ck_example_equipment_code_length CHECK (char_length(equipment_code) <= 40);

CREATE INDEX ix_example_equipment_code ON cat_example_equipment (equipment_code);
