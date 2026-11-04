CREATE TABLE doc_example_checklist (
    document_id uuid PRIMARY KEY REFERENCES documents(id),
    display varchar(200),
    reviewed boolean DEFAULT false
);

CREATE TABLE doc_example_checklist_checks (
    document_id uuid NOT NULL REFERENCES doc_example_checklist(document_id) ON DELETE CASCADE,
    line_no integer NOT NULL CHECK (line_no > 0),
    description varchar(200),
    passed boolean,
    PRIMARY KEY (document_id, line_no)
);
