ALTER TABLE doc_example_equipment_service
    ADD COLUMN pricing_date date,
    ADD COLUMN quoted_amount numeric(18,4),
    ADD COLUMN adjustment_reason text CHECK (char_length(adjustment_reason) <= 200);
