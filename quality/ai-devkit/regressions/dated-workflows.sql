-- Maintained outside the generated application and its acceptance plan.
INSERT INTO work_example.resource VALUES (1, 'Q1', 'quality@example.test', 'America/New_York', 'A', false);
DO $audit$
DECLARE
    first_credit bigint;
    dependent bigint;
    original bigint;
    subsequent bigint;
    amounts numeric[];
BEGIN
    INSERT INTO work_example.resource VALUES (5, 'Q9', 'five@example.test', 'UTC', 'C', false);
    -- Check the business total and the actual continuation at limit - 1, limit and limit + 1.
    PERFORM work_example.change_position(5, '2026-05-01', 3, 0);
    IF work_example.credit_balance(5, '2026-05-01') IS DISTINCT FROM 3 THEN
        RAISE EXCEPTION 'oracle: incorrect total before full page';
    END IF;
    dependent := work_example.change_position(5, '2026-05-01', -1, 0);
    IF work_example.credit_balance(5, '2026-05-01') IS DISTINCT FROM 2 THEN
        RAISE EXCEPTION 'oracle: incorrect total at full page';
    END IF;
    subsequent := work_example.change_position(5, '2026-05-01', -2, 0);
    IF work_example.credit_balance(5, '2026-05-01') IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION 'oracle: truncated history total';
    END IF;
    SELECT max(movement_id) INTO original FROM work_example.movement_history(5, '2026-05-01', 0, 2);
    IF original IS DISTINCT FROM dependent OR (SELECT array_agg(movement_id)
        FROM work_example.movement_history(5, '2026-05-01', original, 2)) IS DISTINCT FROM ARRAY[subsequent] THEN
        RAISE EXCEPTION 'oracle: missing history continuation';
    END IF;

    first_credit := work_example.change_position(1, '2026-05-03', 20, 0);
    BEGIN
        PERFORM work_example.change_position(1, '2026-05-02', -20, 0);
        RAISE EXCEPTION 'oracle: future funds accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    PERFORM set_config('TimeZone', 'Pacific/Kiritimati', true);
    IF work_example.credit_balance(1, '2026-05-03') IS DISTINCT FROM 20 THEN
        RAISE EXCEPTION 'oracle: business date shifted';
    END IF;
    PERFORM work_example.change_position(1, '2026-05-05', 0, 1);
    BEGIN
        PERFORM work_example.change_position(1, '2026-05-04', -10, -1);
        RAISE EXCEPTION 'oracle: future entitlement accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    dependent := work_example.change_position(1, '2026-05-06', -20, -1);
    PERFORM work_example.change_position(1, '2026-05-08', 20, 0);
    BEGIN
        PERFORM work_example.change_position(1, NULL, 0, 0, first_credit);
        RAISE EXCEPTION 'oracle: late replacement accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    IF work_example.credit_balance(1, '2026-05-03') IS DISTINCT FROM 20
       OR work_example.credit_balance(1, '2026-05-06') IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION 'oracle: refusal changed prior commitments';
    END IF;
    PERFORM work_example.change_position(1, '2026-05-03', 20, 0);
    PERFORM work_example.change_position(1, NULL, 0, 0, first_credit);
    IF work_example.credit_balance(1, '2026-05-03') IS DISTINCT FROM 20
       OR work_example.credit_balance(1, '2026-05-06') IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION 'oracle: permitted replacement changed the balance';
    END IF;
    -- Read the dependent movement through its public withdrawal interface,
    -- then roll back this probe so the original history is still exercised below.
    BEGIN
        PERFORM work_example.change_position(1, NULL, 0, 0, dependent);
        IF work_example.credit_balance(1, '2026-05-06') IS DISTINCT FROM 20 THEN
            RAISE EXCEPTION 'oracle: permitted replacement changed the dependent movement';
        END IF;
        RAISE SQLSTATE 'Z0001';
    EXCEPTION WHEN SQLSTATE 'Z0001' THEN NULL;
    END;

    original := work_example.begin_assignment(1, '2026-05-08T03:20:00Z', 12);
    BEGIN
        UPDATE work_example.assignment SET finished = started WHERE id = original;
        RAISE EXCEPTION 'oracle: zero duration accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF EXISTS (SELECT 1 FROM work_example.completed_charges WHERE id = original) THEN
        RAISE EXCEPTION 'oracle: refused closing produced a charge';
    END IF;
    UPDATE work_example.resource SET business_zone = 'UTC' WHERE id = 1;
    UPDATE work_example.assignment SET finished = '2026-05-08T04:20:00Z' WHERE id = original;
    UPDATE work_example.resource SET category = 'B' WHERE id = 1;
    BEGIN
        PERFORM work_example.begin_assignment(1, '2026-05-08T03:30:00Z', 12);
        RAISE EXCEPTION 'oracle: closed overlap accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    subsequent := work_example.begin_assignment(1, '2026-05-09T03:20:00Z', 12);
    UPDATE work_example.assignment SET finished = '2026-05-09T04:20:00Z' WHERE id = subsequent;
    BEGIN
        UPDATE work_example.assignment SET finished = '2026-05-09T03:30:00Z' WHERE id = original;
        RAISE EXCEPTION 'oracle: closing edit created overlap';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF (SELECT finished FROM work_example.assignment WHERE id = original)
       IS DISTINCT FROM timestamptz '2026-05-08T04:20:00Z' THEN
        RAISE EXCEPTION 'oracle: rejected closing edit changed history';
    END IF;
    SELECT array_agg(amount ORDER BY id) INTO amounts FROM work_example.completed_charges;
    IF amounts IS DISTINCT FROM ARRAY[24,12]::numeric[] THEN RAISE EXCEPTION 'oracle: changed calendar'; END IF;
    IF (SELECT sum(amount) FROM work_example.completed_charges WHERE captured_category = 'A') IS DISTINCT FROM 24 THEN
        RAISE EXCEPTION 'oracle: changed category';
    END IF;

    subsequent := work_example.begin_assignment(1, '2026-05-09T04:20:00Z', 12);
    UPDATE work_example.assignment SET finished = '2026-05-09T05:20:00Z' WHERE id = subsequent;
    UPDATE work_example.resource SET retired = true WHERE id = 1;
    BEGIN
        UPDATE work_example.resource SET retired = false WHERE id = 1;
        RAISE EXCEPTION 'oracle: reactivated identity';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE work_example.resource SET stable_code = 'Q2' WHERE id = 1;
        RAISE EXCEPTION 'oracle: mutable identifier';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE work_example.resource SET contact = ' ' WHERE id = 1;
        RAISE EXCEPTION 'oracle: blank contact';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE work_example.resource SET contact = E'\t\n' WHERE id = 1;
        RAISE EXCEPTION 'oracle: whitespace-only contact';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF (SELECT contact FROM work_example.resource WHERE id = 1) IS DISTINCT FROM 'quality@example.test' THEN
        RAISE EXCEPTION 'oracle: refused edit changed the contact';
    END IF;

    INSERT INTO work_example.resource VALUES (2, 'Q3', 'second@example.test', 'UTC', 'B', false);
    PERFORM work_example.change_position(2, '2026-05-03', 7, 0);
    IF work_example.credit_balance(1, '2026-05-03') IS DISTINCT FROM 20
       OR work_example.credit_balance(2, '2026-05-03') IS DISTINCT FROM 7 THEN
        RAISE EXCEPTION 'oracle: mixed resource balances';
    END IF;

    BEGIN
        PERFORM work_example.begin_assignment(1, '2026-05-10T12:00:00Z', 10);
        RAISE EXCEPTION 'oracle: retired resource accepted new work';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF work_example.begin_assignment(2, '2026-05-10T12:00:00Z', 10) IS NULL THEN
        RAISE EXCEPTION 'oracle: active resource could not start work';
    END IF;
    INSERT INTO work_example.resource VALUES (3, 'Q4', 'third@example.test', 'UTC', 'C', false);
    BEGIN
        IF work_example.begin_assignment(3, '2026-05-10T12:00:00Z', 10) IS NULL THEN
            RAISE EXCEPTION 'oracle: simultaneous independent work was not recorded';
        END IF;
    EXCEPTION WHEN check_violation THEN
        RAISE EXCEPTION 'oracle: independent simultaneous work refused';
    END;

    BEGIN
        PERFORM work_example.change_position(3, '2026-05-03', -1, 0);
        RAISE EXCEPTION 'oracle: used another resource funds';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF work_example.credit_balance(2, '2026-05-03') IS DISTINCT FROM 7
       OR work_example.credit_balance(3, '2026-05-03') IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION 'oracle: rejected withdrawal changed an independent balance';
    END IF;
    PERFORM work_example.change_position(3, '2026-05-03', 2, 0);
    PERFORM work_example.change_position(3, '2026-05-03', -1, 0);
    IF work_example.credit_balance(3, '2026-05-03') IS DISTINCT FROM 1
       OR work_example.credit_balance(2, '2026-05-03') IS DISTINCT FROM 7 THEN
        RAISE EXCEPTION 'oracle: own funded withdrawal changed the wrong balance';
    END IF;

    -- Even two independently funded owners cannot use each other's selected credit.
    first_credit := work_example.change_position(2, '2026-05-12', 5, 0);
    INSERT INTO work_example.request (beneficiary, funding_id, effective, amount)
    VALUES (2, first_credit, '2026-05-12', 1) RETURNING id INTO dependent;
    UPDATE work_example.request SET beneficiary = 3 WHERE id = dependent;
    BEGIN
        PERFORM work_example.confirm_request(dependent);
        RAISE EXCEPTION 'oracle: changed beneficiary consumed another owner credit';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF (SELECT confirmed_id FROM work_example.request WHERE id = dependent) IS NOT NULL THEN
        RAISE EXCEPTION 'oracle: refused beneficiary change created a movement';
    END IF;
    UPDATE work_example.request SET beneficiary = 2 WHERE id = dependent;
    PERFORM work_example.confirm_request(dependent);
    IF work_example.credit_balance(2, '2026-05-12') IS DISTINCT FROM 11 THEN
        RAISE EXCEPTION 'oracle: matching beneficiary control failed';
    END IF;

    INSERT INTO work_example.resource VALUES (4, 'Q8', 'four@example.test', 'UTC', 'C', false);
    original := work_example.begin_assignment(4, '2026-05-15T10:00Z', 12);
    UPDATE work_example.assignment SET finished = '2026-05-15T11:00Z' WHERE id = original;
    subsequent := work_example.begin_assignment(4, '2026-05-15T11:00Z', 12);
    BEGIN
        UPDATE work_example.assignment SET finished = NULL WHERE id = original;
        RAISE EXCEPTION 'oracle: same-instant release withdrawal displaced a successor';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF (SELECT array_agg(id) FROM work_example.assignment WHERE resource_id = 4 AND finished IS NULL)
        IS DISTINCT FROM ARRAY[subsequent] THEN
        RAISE EXCEPTION 'oracle: withdrawal changed current custody';
    END IF;
END;
$audit$;
