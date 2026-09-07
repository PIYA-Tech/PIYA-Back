-- LIMITED PATIENT SAMPLE for the confirmed live database "piya-db".
-- This is NOT conference-demo.sql. It adds no public directory/catalog entries.
-- First run, in the SAME psql connection (replace the placeholder privately):
--   SET piya.demo_password = '<YOUR_PRIVATE_DEMO_PASSWORD>';
-- Then paste/execute this entire file. Choose 12-72 bytes with a digit and symbol.
-- Back up the database first. Reruns/collisions abort without overwriting anything.
-- This creates an ordinary Patient account, NOT an enforced read-only/sandbox mode.
-- Do not book real visits, request refills, share emergency access, or connect
-- real Apple Health/identity/insurance information while demonstrating it.

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SET LOCAL search_path = public;

DO $guard$
DECLARE
    v_table text;
    v_password text := current_setting('piya.demo_password', true);
BEGIN
    IF current_database() <> 'piya-db' THEN
        RAISE EXCEPTION 'This limited patient script targets piya-db, not %. No changes made.', current_database();
    END IF;
    IF v_password IS NULL OR v_password LIKE '<%' OR v_password LIKE 'REPLACE%'
        OR octet_length(v_password) NOT BETWEEN 12 AND 72
        OR v_password !~ '[0-9]' OR v_password !~ '[^[:alnum:]]' THEN
        RAISE EXCEPTION 'Before running the script, SET piya.demo_password to a private 12-72-byte password with a digit and symbol in this same connection.';
    END IF;
    FOREACH v_table IN ARRAY ARRAY['Users', 'PatientMedication', 'MedicationDoseSchedule',
        'MedicationDoseOccurrence', 'PatientInboxNotification', 'EmergencyHealthProfiles', 'AuditLogs'] LOOP
        IF to_regclass(format('public.%I', v_table)) IS NULL THEN
            RAISE EXCEPTION 'Required PIYA table % is missing. Stop and check deployed migrations. No changes made.', v_table;
        END IF;
    END LOOP;
END $guard$;

-- BCrypt matches PIYA's PasswordHasher. Any later error rolls this back too.
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;

DO $patient_demo$
DECLARE
    v_patient uuid := gen_random_uuid();
    v_med_a uuid := gen_random_uuid();
    v_med_b uuid := gen_random_uuid();
    v_schedule_a uuid := gen_random_uuid();
    v_schedule_b uuid := gen_random_uuid();
    v_now timestamptz := now();
    v_today timestamptz := date_trunc('day', now() AT TIME ZONE 'Asia/Baku') AT TIME ZONE 'Asia/Baku';
    v_day integer;
    v_dose_time timestamptz;
BEGIN
    -- Same advisory key as the isolated full demo: those scripts cannot race.
    PERFORM pg_advisory_xact_lock(20260907, 101);
    IF EXISTS (SELECT 1 FROM "Users"
        WHERE lower("Username") = 'conference_patient'
           OR lower("Email") = 'conference.patient@example.invalid') THEN
        RAISE EXCEPTION 'Conference username/email already exists. Nothing changed; existing passwords, 2FA and records are preserved.';
    END IF;

    -- One new normal patient, no staff/admin account and no contact verification.
    INSERT INTO "Users" ("Id", "Username", "PasswordHash", "FirstName", "LastName", "Email",
        "PhoneNumber", "DateOfBirth", "Role", "IsActive", "IsEmailVerified", "IsPhoneVerified",
        "CreatedAt", "UpdatedAt")
    VALUES (v_patient, 'conference_patient',
        crypt(current_setting('piya.demo_password'), gen_salt('bf', 12)),
        'Conference', 'Demo — Not a real patient', 'conference.patient@example.invalid', '',
        '1993-04-18 00:00:00+00', 1, true, false, false, v_now, v_now);

    -- PRIVATE, patient-entered sample items only. No Medications master-catalog
    -- insert, PrescriptionItem, doctor attribution, pharmacy or inventory link.
    INSERT INTO "PatientMedication" ("Id", "PatientId", "MedicationId", "PrescriptionItemId",
        "Source", "Status", "DisplayName", "GenericName", "Strength", "Form", "Dosage", "Instructions",
        "StartDate", "EndDate", "SupplyTotal", "SupplyRemaining", "SupplyUnit", "LowSupplyThreshold",
        "LastRefilledAt", "CreatedAt", "UpdatedAt")
    VALUES
        (v_med_a, v_patient, NULL, NULL, 1, 1, '[DEMO] Morning sample', 'Fictional item — not a medicine',
         'Not applicable', 'Demo tablet', '1 sample unit',
         'CONFERENCE DEMO ONLY. Fictional patient-entered example; do not take or request this item.',
         v_today - interval '7 days', v_today + interval '23 days', 30, 24, 'sample units', 7,
         v_today - interval '7 days', v_today - interval '7 days', v_now),
        (v_med_b, v_patient, NULL, NULL, 1, 1, '[DEMO] Evening sample — low supply', 'Fictional item — not a medicine',
         'Not applicable', 'Demo capsule', '1 sample unit',
         'CONFERENCE DEMO ONLY. Fictional low-supply example; no prescription or pharmacy order exists.',
         v_today - interval '7 days', v_today + interval '3 days', 10, 3, 'sample units', 5,
         v_today - interval '7 days', v_today - interval '7 days', v_now);

    -- Today's dose schedule is visible, but notifications/reminder processing
    -- stay OFF. No DeviceTokens, appointment reminders or outbound jobs are added.
    INSERT INTO "MedicationDoseSchedule" ("Id", "PatientMedicationId", "DoseAmount", "TimeOfDayMinutes",
        "DaysOfWeek", "TimeZoneId", "RemindersEnabled", "GracePeriodMinutes", "NextReminderAt", "CreatedAt", "UpdatedAt")
    VALUES
        (v_schedule_a, v_med_a, 1, 480, 127, 'Asia/Baku', false, 120, NULL, v_now, v_now),
        (v_schedule_b, v_med_b, 1, 1200, 127, 'Asia/Baku', false, 120, NULL, v_now, v_now);

    -- Seven fictional days, 13 taken + 1 skipped; supply balances match history.
    FOR v_day IN 1..7 LOOP
        v_dose_time := v_today - make_interval(days => v_day) + interval '8 hours';
        INSERT INTO "MedicationDoseOccurrence" ("Id", "ScheduleId", "PatientMedicationId", "PatientId",
            "ScheduledFor", "Status", "RecordedAt", "SupplyDeducted", "Note", "CreatedAt", "UpdatedAt")
        VALUES (gen_random_uuid(), v_schedule_a, v_med_a, v_patient, v_dose_time,
            CASE WHEN v_day = 3 THEN 4 ELSE 3 END, v_dose_time + interval '5 minutes',
            CASE WHEN v_day = 3 THEN 0 ELSE 1 END,
            '[DEMO] Synthetic interaction, not an actual medication dose.',
            v_dose_time + interval '5 minutes', v_dose_time + interval '5 minutes');
        v_dose_time := v_today - make_interval(days => v_day) + interval '20 hours';
        INSERT INTO "MedicationDoseOccurrence" ("Id", "ScheduleId", "PatientMedicationId", "PatientId",
            "ScheduledFor", "Status", "RecordedAt", "SupplyDeducted", "Note", "CreatedAt", "UpdatedAt")
        VALUES (gen_random_uuid(), v_schedule_b, v_med_b, v_patient, v_dose_time,
            3, v_dose_time + interval '5 minutes', 1,
            '[DEMO] Synthetic interaction, not an actual medication dose.',
            v_dose_time + interval '5 minutes', v_dose_time + interval '5 minutes');
    END LOOP;

    -- No fabricated blood type, allergy, diagnosis, emergency contact or token.
    INSERT INTO "EmergencyHealthProfiles" ("Id", "PatientId", "AdditionalNotes", "IsSharingEnabled", "CreatedAt", "UpdatedAt")
    VALUES (gen_random_uuid(), v_patient,
        'CONFERENCE DEMO — NOT A REAL PATIENT. No clinical facts are recorded. Do not use for treatment or share emergency access.',
        false, v_now, v_now);

    -- In-app inbox only; not email, SMS, push or provider acknowledgements.
    INSERT INTO "PatientInboxNotification" ("Id", "UserId", "Category", "Title", "Body", "DedupeKey", "CreatedAt")
    VALUES
        (gen_random_uuid(), v_patient, 7, '[DEMO] Welcome to PIYA',
         'This conference account contains only fictional, patient-entered samples. It is not a real patient record.',
         'limited-conference-welcome-v1', v_now),
        (gen_random_uuid(), v_patient, 7, '[DEMO] Explore medication tracking',
         'Open My Health > Medications to view sample supply, dose schedules and history. No medicine is prescribed or ordered.',
         'limited-conference-tracking-v1', v_now - interval '1 minute'),
        (gen_random_uuid(), v_patient, 7, '[DEMO] Live-service reminder',
         'This is a normal patient account on the live service. Do not book real appointments, request refills or connect real health data.',
         'limited-conference-live-notice-v1', v_now - interval '2 minutes');

    INSERT INTO "AuditLogs" ("Id", "Action", "EntityType", "EntityId", "Description", "Metadata", "IsSuccess", "CreatedAt")
    VALUES (gen_random_uuid(), 'ConferenceLimitedDemoSeed', 'User', v_patient::text,
        'SQL operator created one labelled conference patient with patient-entered sample tracking; no clinical or public catalog records.',
        json_build_object('fixture', 'limited-conference-v1', 'database', current_database(),
            'operator', session_user, 'patientId', v_patient)::text,
        true, v_now);
    RAISE NOTICE 'Created conference_patient (%). No public directory, prescription, appointment or verification records added.', v_patient;
END $patient_demo$;

COMMIT;

SELECT "Id", "Username", "Email", "FirstName", "LastName", "IsActive"
FROM "Users" WHERE "Username" = 'conference_patient';
