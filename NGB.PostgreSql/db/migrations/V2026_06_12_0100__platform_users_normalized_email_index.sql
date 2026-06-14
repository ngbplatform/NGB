DO $$
DECLARE
    duplicate_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO duplicate_count
    FROM (
        SELECT lower(trim(email)) AS normalized_email
        FROM platform_users
        WHERE email IS NOT NULL
        GROUP BY lower(trim(email))
        HAVING COUNT(*) > 1
    ) duplicates;

    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'Cannot create ux_platform_users_normalized_email: % duplicate normalized platform user email group(s) exist. Repair duplicate platform_users before applying this migration.', duplicate_count;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_users_normalized_email
    ON platform_users(lower(trim(email)))
    WHERE email IS NOT NULL;
