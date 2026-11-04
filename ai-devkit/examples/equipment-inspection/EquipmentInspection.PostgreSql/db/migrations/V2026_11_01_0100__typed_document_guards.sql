-- The platform pack runs before the application's typed document tables exist.
-- Install the existing platform guards after all application tables are present.
SELECT ngb_install_typed_document_immutability_guards();
