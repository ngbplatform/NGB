-- Standalone reference fixture. Each resource owns an independent dated balance and calendar.
CREATE SCHEMA work_example;

CREATE TABLE work_example.resource (
    id integer PRIMARY KEY,
    stable_code text NOT NULL UNIQUE CHECK (stable_code ~ '[^[:space:]]'),
    contact text NOT NULL CHECK (contact ~ '[^[:space:]]'),
    business_zone text NOT NULL,
    category text NOT NULL,
    retired boolean NOT NULL DEFAULT false
);

CREATE FUNCTION work_example.protect_identity() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' OR NEW.id <> OLD.id OR NEW.stable_code <> OLD.stable_code OR (OLD.retired AND NOT NEW.retired) THEN
        RAISE EXCEPTION 'The resource identity and retirement are permanent' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER protect_identity BEFORE UPDATE OR DELETE ON work_example.resource
FOR EACH ROW EXECUTE FUNCTION work_example.protect_identity();

CREATE TABLE work_example.movement (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    resource_id integer NOT NULL REFERENCES work_example.resource,
    business_date date NOT NULL,
    credits integer NOT NULL,
    rights integer NOT NULL,
    active boolean NOT NULL DEFAULT true
);

-- All postings and corrections for one resource take the same lock. Every daily
-- closing position must remain valid, including days after a backdated correction.
CREATE FUNCTION work_example.change_position(
    resource_key integer, effective date, credits integer, rights integer, withdraw bigint DEFAULT NULL
) RETURNS bigint LANGUAGE plpgsql AS $$
DECLARE
    movement_id bigint;
BEGIN
    PERFORM id FROM work_example.resource WHERE id = resource_key FOR UPDATE;
    IF withdraw IS NULL THEN
        INSERT INTO work_example.movement (resource_id, business_date, credits, rights)
        VALUES (resource_key, effective, credits, rights) RETURNING id INTO movement_id;
    ELSE
        UPDATE work_example.movement SET active = false
        WHERE id = withdraw AND resource_id = resource_key AND active
        RETURNING id INTO STRICT movement_id;
    END IF;

    IF EXISTS (
        SELECT 1 FROM (
            SELECT sum(sum(m.credits)) OVER (ORDER BY business_date) AS funds,
                   sum(sum(m.rights)) OVER (ORDER BY business_date) AS entitlements
            FROM work_example.movement m
            WHERE resource_id = resource_key AND active
            GROUP BY business_date
        ) daily WHERE funds < 0 OR entitlements < 0
    ) THEN
        RAISE EXCEPTION 'Insufficient dated funds or entitlement' USING ERRCODE = '23514';
    END IF;
    RETURN movement_id;
END;
$$;

CREATE FUNCTION work_example.credit_balance(resource_key integer, cutoff date)
RETURNS numeric LANGUAGE sql STABLE AS $$
    SELECT coalesce(sum(credits), 0) FROM work_example.movement
    WHERE resource_id = resource_key AND active AND business_date <= cutoff;
$$;

CREATE TABLE work_example.assignment (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    resource_id integer NOT NULL REFERENCES work_example.resource,
    started timestamptz NOT NULL,
    finished timestamptz,
    captured_zone text NOT NULL,
    captured_category text NOT NULL,
    daily_rate numeric(12,2) NOT NULL CHECK (daily_rate > 0),
    CHECK (finished > started)
);

CREATE FUNCTION work_example.check_assignment_interval() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.finished < NEW.started THEN
        RAISE EXCEPTION 'An assignment cannot finish before it starts' USING ERRCODE = '23514';
    END IF;
    PERFORM id FROM work_example.resource WHERE id = NEW.resource_id FOR UPDATE;
    IF EXISTS (
        SELECT 1 FROM work_example.assignment a
        WHERE a.resource_id = NEW.resource_id AND a.id <> NEW.id
          AND tstzrange(a.started, a.finished, '[)') && tstzrange(NEW.started, NEW.finished, '[)')
    ) THEN
        RAISE EXCEPTION 'The resource is unavailable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER check_assignment_interval BEFORE INSERT OR UPDATE OF resource_id, started, finished
ON work_example.assignment FOR EACH ROW EXECUTE FUNCTION work_example.check_assignment_interval();

CREATE FUNCTION work_example.begin_assignment(resource_key integer, moment timestamptz, rate numeric)
RETURNS bigint LANGUAGE plpgsql AS $$
DECLARE
    item work_example.resource%ROWTYPE;
    assignment_id bigint;
BEGIN
    SELECT * INTO STRICT item FROM work_example.resource WHERE id = resource_key FOR UPDATE;
    IF item.retired THEN
        RAISE EXCEPTION 'The resource is unavailable' USING ERRCODE = '23514';
    END IF;
    INSERT INTO work_example.assignment (resource_id, started, captured_zone, captured_category, daily_rate)
    VALUES (resource_key, moment, item.business_zone, item.category, rate) RETURNING id INTO assignment_id;
    RETURN assignment_id;
END;
$$;

CREATE VIEW work_example.completed_charges AS
SELECT id, resource_id, captured_category,
       ((finished AT TIME ZONE captured_zone)::date - (started AT TIME ZONE captured_zone)::date + 1) * daily_rate AS amount
FROM work_example.assignment
WHERE finished IS NOT NULL;
