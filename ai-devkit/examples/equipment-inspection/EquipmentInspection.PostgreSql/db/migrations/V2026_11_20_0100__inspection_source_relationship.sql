ALTER TABLE doc_example_inspection
    ADD COLUMN source_inspection_id uuid REFERENCES doc_example_inspection (document_id);

SELECT ngb_install_mirrored_document_relationship_trigger(
    'doc_example_inspection', 'source_inspection_id', 'based_on');
