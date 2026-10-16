CREATE TABLE cat_mcp_checkpoint (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
    display text NOT NULL
);
