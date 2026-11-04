CREATE TABLE cat_example_equipment_category (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
    code varchar(64) NOT NULL,
    name varchar(200) NOT NULL
);

CREATE UNIQUE INDEX ux_example_equipment_category_code ON cat_example_equipment_category(code);
