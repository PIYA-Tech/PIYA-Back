-- PIYA conference patient: FICTIONAL DATA ONLY. Never use a live patient database.
-- Apply PIYA migrations first. This script does NOT create the application schema.
-- In the SAME SQL session, set the exact DEMO database name and a private password:
--   SET piya.demo_database = 'piya_conference_demo';
--   SET piya.demo_password = '<choose a private password, 12-72 bytes>';
-- Then execute this entire file. PostgreSQL 14+ with pgcrypto is required.
-- Dates are relative to the execution date in Asia/Baku. Reruns abort unchanged.
-- No real users/providers are reused, no staff login is enabled, no verification
-- badges or emergency-access grants are fabricated, and no outbound jobs are seeded.

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SET LOCAL search_path = public;

DO $guard$
BEGIN
    IF COALESCE(current_setting('piya.demo_database', true), '') <> current_database() THEN
        RAISE EXCEPTION 'Wrong database: set piya.demo_database to your separate demo database name first. Do not target production.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260907093742_VeriffVerificationConcurrency') THEN
        RAISE EXCEPTION 'Apply the current PIYA migrations to the demo database before running this script.';
    END IF;
END $guard$;

-- Transactional: if a later statement fails, a newly created extension rolls back too.
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;

DO $demo$
DECLARE
    v_password text := current_setting('piya.demo_password', true);
    v_patient uuid := gen_random_uuid();
    v_doctor uuid := gen_random_uuid();
    v_pharmacist uuid := gen_random_uuid();
    v_hospital uuid := gen_random_uuid();
    v_pharmacy uuid := gen_random_uuid();
    v_company uuid := gen_random_uuid();
    v_hospital_coordinate uuid := gen_random_uuid();
    v_pharmacy_coordinate uuid := gen_random_uuid();
    v_visit uuid := gen_random_uuid();
    v_next_visit uuid := gen_random_uuid();
    v_prescription uuid := gen_random_uuid();
    v_med_a uuid := gen_random_uuid();
    v_med_b uuid := gen_random_uuid();
    v_item_a uuid := gen_random_uuid();
    v_item_b uuid := gen_random_uuid();
    v_tracked_a uuid := gen_random_uuid();
    v_tracked_b uuid := gen_random_uuid();
    v_schedule_a uuid := gen_random_uuid();
    v_schedule_b uuid := gen_random_uuid();
    v_refill uuid := gen_random_uuid();
    v_referral uuid := gen_random_uuid();
    v_lab_old uuid := gen_random_uuid();
    v_lab_new uuid := gen_random_uuid();
    v_summary uuid := gen_random_uuid();
    v_loop uuid := gen_random_uuid();
    v_now timestamptz := now();
    v_today timestamptz := date_trunc('day', now() AT TIME ZONE 'Asia/Baku') AT TIME ZONE 'Asia/Baku';
    v_day integer;
    v_dose_time timestamptz;
BEGIN
    IF v_password IS NULL OR v_password LIKE '<%' OR v_password LIKE 'REPLACE%'
        OR octet_length(v_password) NOT BETWEEN 12 AND 72
        OR v_password !~ '[0-9]' OR v_password !~ '[^[:alnum:]]' THEN
        RAISE EXCEPTION 'Set piya.demo_password to a private 12-72-byte password including a digit and a symbol.';
    END IF;

    -- Serializes this seed across concurrent invocations without locking real tables.
    PERFORM pg_advisory_xact_lock(20260907, 101);
    IF EXISTS (SELECT 1 FROM "Users" WHERE
        "Username" IN ('conference_patient', 'conference_doctor_disabled', 'conference_pharmacist_disabled')
        OR lower("Email") IN ('conference.patient@example.invalid',
            'conference.doctor@example.invalid', 'conference.pharmacist@example.invalid')) THEN
        RAISE EXCEPTION 'Conference account already exists. Nothing changed; existing passwords and presentation edits are preserved.';
    END IF;

    -- 1. One patient login. Supporting staff are inactive with unknown random passwords.
    INSERT INTO "Users" ("Id", "Username", "PasswordHash", "FirstName", "LastName",
        "Email", "PhoneNumber", "DateOfBirth", "Role", "IsActive", "IsEmailVerified",
        "IsPhoneVerified", "CreatedAt", "UpdatedAt")
    VALUES
        (v_patient, 'conference_patient', crypt(v_password, gen_salt('bf', 12)),
         'Aylin', 'Conference Demo', 'conference.patient@example.invalid', '',
         '1993-04-18 00:00:00+00', 1, true, false, false, v_now, v_now),
        (v_doctor, 'conference_doctor_disabled', crypt(encode(gen_random_bytes(32), 'hex'), gen_salt('bf', 12)),
         'Demo', 'Clinician (Fictional)', 'conference.doctor@example.invalid', '',
         NULL, 2, false, false, false, v_now, v_now),
        (v_pharmacist, 'conference_pharmacist_disabled', crypt(encode(gen_random_bytes(32), 'hex'), gen_salt('bf', 12)),
         'Demo', 'Pharmacist (Fictional)', 'conference.pharmacist@example.invalid', '',
         NULL, 3, false, false, false, v_now, v_now);

    -- 2. Separate fictional facilities, never a real hospital/pharmacy affiliation.
    INSERT INTO "Coordinates" ("Id", "Latitude", "Longitude") VALUES
        (v_hospital_coordinate, 40.4093, 49.8671), (v_pharmacy_coordinate, 40.4110, 49.8700);
    INSERT INTO "Hospitals" ("Id", "Name", "Address", "City", "Country", "PhoneNumber",
        "Email", "Departments", "IsActive", "CoordinatesId", "CreatedAt", "UpdatedAt")
    VALUES (v_hospital, 'PIYA Conference Clinic (Fictional)', 'Demo map position only — not a real clinic',
        'Baku', 'Azerbaijan', '', 'clinic@example.invalid', ARRAY['General Practice'],
        true, v_hospital_coordinate, v_now, v_now);
    INSERT INTO "DoctorProfiles" ("Id", "UserId", "LicenseNumber", "Specialization",
        "AdditionalSpecializations", "YearsOfExperience", "Certifications", "Education", "Languages",
        "Biography", "AcceptingNewPatients", "CurrentStatus", "HospitalIds",
        "AverageAppointmentDuration", "TotalPatientsTreated", "TotalRatings", "CreatedAt", "UpdatedAt")
    VALUES (gen_random_uuid(), v_doctor, 'DEMO-NOT-A-LICENSE', 1, ARRAY[]::integer[], 0,
        ARRAY[]::text[], ARRAY[]::text[], ARRAY['Azerbaijani', 'English', 'Russian'],
        'Fictional conference character; not a verified or licensed practitioner.', false, 0,
        ARRAY[v_hospital], 30, 0, 0, v_now, v_now);
    INSERT INTO "PharmacyCompanies" ("Id", "Name")
    VALUES (v_company, 'PIYA Conference Network (Fictional)');
    INSERT INTO "Pharmacies" ("Id", "Name", "Address", "City", "Country", "PhoneNumber", "Email",
        "CompanyId", "CoordinatesId", "Services", "IsActive", "Is24Hours", "AverageRating",
        "TotalRatings", "CreatedAt", "UpdatedAt")
    VALUES (v_pharmacy, 'PIYA Conference Pharmacy (Fictional)', 'Demo map position only — not a real pharmacy',
        'Baku', 'Azerbaijan', '', 'pharmacy@example.invalid', v_company, v_pharmacy_coordinate,
        ARRAY['Demonstration only'], true, false, 0, 0, v_now, v_now);

    -- 3. A completed visit and two upcoming visits, with a continuous care story.
    INSERT INTO "Appointments" ("Id", "PatientId", "DoctorId", "HospitalId", "ScheduledAt",
        "DurationMinutes", "Status", "Reason", "AppointmentNotes", "ActualStartTime",
        "ActualEndTime", "CreatedAt", "UpdatedAt")
    VALUES (v_visit, v_patient, v_doctor, v_hospital, v_today - interval '8 days' + interval '10 hours',
        30, 4, '[DEMO] Annual wellbeing review', 'Fictional encounter for presentation; not clinical advice.',
        v_today - interval '8 days' + interval '10 hours',
        v_today - interval '8 days' + interval '10 hours 30 minutes',
        v_today - interval '15 days', v_today - interval '8 days' + interval '11 hours');
    INSERT INTO "Appointments" ("Id", "PatientId", "DoctorId", "HospitalId", "ScheduledAt",
        "DurationMinutes", "Status", "Reason", "CreatedAt", "UpdatedAt")
    VALUES
        (v_next_visit, v_patient, v_doctor, v_hospital, v_today + interval '1 day 11 hours',
         30, 2, '[DEMO] Review lab results and care plan', v_now, v_now),
        (gen_random_uuid(), v_patient, v_doctor, v_hospital, v_today + interval '4 days 15 hours',
         30, 1, '[DEMO] Medication follow-up', v_now, v_now);

    -- 4. Two explicitly fictional medicines, a prescription and tracked supply.
    INSERT INTO "Medications" ("Id", "BrandName", "GenericName", "ActiveIngredients", "Form",
        "Strength", "Manufacturer", "RequiresPrescription", "IsControlledSubstance", "GenericAlternatives",
        "Usage", "IsAvailable", "Country", "CreatedAt", "UpdatedAt")
    VALUES
        (v_med_a, 'DemoMed Morning (Fictional)', 'Demo ingredient A', ARRAY['Not a real substance'],
         'Tablet', 'Demo only', 'PIYA Conference', true, false, ARRAY[]::uuid[],
         'Presentation only. Do not prescribe, dispense or take.', true, 'Azerbaijan', v_now, v_now),
        (v_med_b, 'DemoMed Evening (Fictional)', 'Demo ingredient B', ARRAY['Not a real substance'],
         'Capsule', 'Demo only', 'PIYA Conference', true, false, ARRAY[]::uuid[],
         'Presentation only. Do not prescribe, dispense or take.', true, 'Azerbaijan', v_now, v_now);
    INSERT INTO "Prescriptions" ("Id", "PatientId", "DoctorId", "AppointmentId", "Status", "Diagnosis",
        "Instructions", "IssuedAt", "ExpiresAt", "CreatedAt", "UpdatedAt")
    VALUES (v_prescription, v_patient, v_doctor, v_visit, 1, '[DEMO] Fictional care-plan example',
        'CONFERENCE DEMO — not a valid medical prescription. All items are fictional.',
        v_today - interval '8 days' + interval '11 hours', v_today + interval '30 days',
        v_today - interval '8 days' + interval '11 hours', v_now);
    INSERT INTO "PrescriptionItems" ("Id", "PrescriptionId", "MedicationId", "Dosage", "Frequency",
        "Duration", "Quantity", "Instructions", "IsFulfilled", "CreatedAt")
    VALUES
        (v_item_a, v_prescription, v_med_a, '1 demo tablet', 'Daily at 08:00', '30 demo days', 30,
         'Fictional schedule for demonstrating reminders; not medical advice.', false, v_now),
        (v_item_b, v_prescription, v_med_b, '1 demo capsule', 'Daily at 20:00', '10 demo days', 10,
         'Fictional schedule for demonstrating low supply; not medical advice.', false, v_now);
    INSERT INTO "PatientMedication" ("Id", "PatientId", "MedicationId", "PrescriptionItemId", "Source",
        "Status", "DisplayName", "GenericName", "Strength", "Form", "Dosage", "Instructions",
        "StartDate", "EndDate", "SupplyTotal", "SupplyRemaining", "SupplyUnit", "LowSupplyThreshold",
        "LastRefilledAt", "CreatedAt", "UpdatedAt")
    VALUES
        (v_tracked_a, v_patient, v_med_a, v_item_a, 2, 1, 'DemoMed Morning (Fictional)', 'Demo ingredient A',
         'Demo only', 'Tablet', '1 demo tablet', 'Fictional medication tracking; not medical advice.',
         v_today - interval '7 days', v_today + interval '23 days', 30, 24, 'tablets', 7,
         v_today - interval '7 days', v_today - interval '7 days', v_now),
        (v_tracked_b, v_patient, v_med_b, v_item_b, 2, 1, 'DemoMed Evening (Fictional)', 'Demo ingredient B',
         'Demo only', 'Capsule', '1 demo capsule', 'Fictional low-supply example; not medical advice.',
         v_today - interval '7 days', v_today + interval '3 days', 10, 3, 'capsules', 5,
         v_today - interval '7 days', v_today - interval '7 days', v_now);
    -- Dose rules still display in Today's Doses. Push/local reminders are OFF.
    INSERT INTO "MedicationDoseSchedule" ("Id", "PatientMedicationId", "DoseAmount", "TimeOfDayMinutes",
        "DaysOfWeek", "TimeZoneId", "RemindersEnabled", "GracePeriodMinutes", "NextReminderAt", "CreatedAt", "UpdatedAt")
    VALUES (v_schedule_a, v_tracked_a, 1, 480, 127, 'Asia/Baku', false, 120, NULL, v_now, v_now),
        (v_schedule_b, v_tracked_b, 1, 1200, 127, 'Asia/Baku', false, 120, NULL, v_now, v_now);
    FOR v_day IN 1..7 LOOP
        v_dose_time := v_today - make_interval(days => v_day) + interval '8 hours';
        INSERT INTO "MedicationDoseOccurrence" ("Id", "ScheduleId", "PatientMedicationId", "PatientId",
            "ScheduledFor", "Status", "RecordedAt", "SupplyDeducted", "Note", "CreatedAt", "UpdatedAt")
        VALUES (gen_random_uuid(), v_schedule_a, v_tracked_a, v_patient, v_dose_time,
            CASE WHEN v_day = 3 THEN 4 ELSE 3 END, v_dose_time + interval '5 minutes',
            CASE WHEN v_day = 3 THEN 0 ELSE 1 END, 'Synthetic conference dose event.', v_dose_time, v_dose_time);
        v_dose_time := v_today - make_interval(days => v_day) + interval '20 hours';
        INSERT INTO "MedicationDoseOccurrence" ("Id", "ScheduleId", "PatientMedicationId", "PatientId",
            "ScheduledFor", "Status", "RecordedAt", "SupplyDeducted", "Note", "CreatedAt", "UpdatedAt")
        VALUES (gen_random_uuid(), v_schedule_b, v_tracked_b, v_patient, v_dose_time,
            3, v_dose_time + interval '5 minutes', 1, 'Synthetic conference dose event.', v_dose_time, v_dose_time);
    END LOOP;

    -- 5. Pharmacy stock and a ready-to-collect fictional refill (no dispensing token).
    INSERT INTO "PharmacyInventories" ("Id", "PharmacyId", "MedicationId", "QuantityInStock",
        "MinimumStockLevel", "ReorderQuantity", "Price", "Currency", "BatchNumber", "ExpirationDate",
        "IsAvailable", "LowStockAlertTriggered", "LastRestockedAt", "CreatedAt", "UpdatedAt")
    SELECT gen_random_uuid(), v_pharmacy, id, 100, 10, 50, 12.50, 'AZN', 'DEMO-NOT-FOR-SALE',
        v_today + interval '1 year', true, false, v_now, v_now, v_now
    FROM unnest(ARRAY[v_med_a, v_med_b]) AS medications(id);
    INSERT INTO "PatientRefillRequests" ("Id", "PatientId", "PrescriptionId", "PrescriptionItemId", "PharmacyId",
        "Status", "AutoRefill", "EstimatedReadyAt", "ReviewedByUserId", "ReviewedAt", "Note", "CreatedAt", "UpdatedAt")
    VALUES (v_refill, v_patient, v_prescription, v_item_b, v_pharmacy, 3, false,
        v_now - interval '30 minutes', v_pharmacist, v_now - interval '45 minutes',
        '[DEMO] Simulated pickup workflow; no real order, payment or medicine.', v_now - interval '2 hours', v_now);
    INSERT INTO "PatientRefillStatusEvent" ("Id", "RefillRequestId", "Status", "ActorUserId", "Note", "OccurredAt")
    VALUES
        (gen_random_uuid(), v_refill, 1, v_patient, '[DEMO] Request created', v_now - interval '2 hours'),
        (gen_random_uuid(), v_refill, 2, v_pharmacist, '[DEMO] Preparation started', v_now - interval '1 hour'),
        (gen_random_uuid(), v_refill, 3, v_pharmacist, '[DEMO] Ready for presentation', v_now - interval '30 minutes');

    -- 6. A completed referral and two structured lab reports for comparison.
    INSERT INTO "Referrals" ("Id", "ReferringDoctorId", "PatientId", "Origin", "SourceAppointmentId",
        "ReferredToSpecialty", "ReferredToDoctorId", "Status", "Urgency", "Reason", "ResultNotes",
        "IsExternal", "CompletedAt", "CreatedAt", "UpdatedAt")
    VALUES (v_referral, v_doctor, v_patient, 1, v_visit, 1, v_doctor, 4, 1,
        '[DEMO] Routine laboratory review', 'Synthetic results are ready for the follow-up demonstration.',
        false, v_today - interval '2 days' + interval '12 hours',
        v_today - interval '8 days' + interval '11 hours', v_now);
    INSERT INTO "MedicalTests" ("Id", "PatientId", "ReferralId", "OrderedByDoctorId", "PerformedByDoctorId",
        "TestType", "Status", "IsEmergency", "Notes", "Findings", "PerformedAt", "ResultsAt", "CreatedAt", "UpdatedAt")
    VALUES
        (v_lab_old, v_patient, NULL, v_doctor, v_doctor, 3, 4, false,
         '[DEMO] Earlier blood panel', 'Synthetic numbers only; not a clinical report.',
         v_today - interval '90 days' + interval '9 hours', v_today - interval '90 days' + interval '12 hours',
         v_today - interval '91 days', v_today - interval '90 days' + interval '12 hours'),
        (v_lab_new, v_patient, v_referral, v_doctor, v_doctor, 3, 3, false,
         '[DEMO] Follow-up blood panel', 'Synthetic numbers only; not a clinical report.',
         v_today - interval '2 days' + interval '9 hours', v_today - interval '2 days' + interval '12 hours',
         v_today - interval '8 days' + interval '11 hours', v_today - interval '2 days' + interval '12 hours');
    INSERT INTO "MedicalTestAnalyteResult" ("Id", "MedicalTestId", "Code", "Name", "NumericValue", "Unit",
        "ReferenceLow", "ReferenceHigh", "ReferenceText", "Flag", "SortOrder", "ObservedAt", "EnteredByUserId", "CreatedAt", "UpdatedAt")
    SELECT gen_random_uuid(), lab_id, code, name, value, unit, low, high,
        'Synthetic display range — not clinical guidance', 1, ordering, observed, v_doctor, observed, observed
    FROM (VALUES
        (v_lab_old, 'DEMO-HGB', '[DEMO] Haemoglobin', 13.4, 'g/dL', 12.0, 16.0, 1, v_today - interval '90 days' + interval '9 hours'),
        (v_lab_old, 'DEMO-WBC', '[DEMO] White blood cells', 6.2, '10^9/L', 4.0, 11.0, 2, v_today - interval '90 days' + interval '9 hours'),
        (v_lab_new, 'DEMO-HGB', '[DEMO] Haemoglobin', 13.8, 'g/dL', 12.0, 16.0, 1, v_today - interval '2 days' + interval '9 hours'),
        (v_lab_new, 'DEMO-WBC', '[DEMO] White blood cells', 6.0, '10^9/L', 4.0, 11.0, 2, v_today - interval '2 days' + interval '9 hours')
    ) AS sample(lab_id, code, name, value, unit, low, high, ordering, observed);

    -- 7. Consultation summary, scheduled follow-up and interactive care-loop tasks.
    INSERT INTO "ConsultationSummary" ("Id", "AppointmentId", "PatientId", "DoctorId", "Status", "Summary",
        "Diagnosis", "CareInstructions", "PatientMessage", "Version", "PublishedAt", "CreatedAt", "UpdatedAt")
    VALUES (v_summary, v_visit, v_patient, v_doctor, 2,
        '[DEMO] Your visit, medicines and lab results are connected in one care history.',
        '[DEMO] Fictional wellbeing review', 'Presentation workflow only; not a treatment plan.',
        'Demonstration: review the results and complete the example check-in before your follow-up.',
        1, v_today - interval '8 days' + interval '12 hours', v_today - interval '8 days' + interval '11 hours', v_now);
    INSERT INTO "FollowUpPlan" ("Id", "ConsultationSummaryId", "SourceAppointmentId", "PatientId", "DoctorId",
        "HospitalId", "Reason", "EarliestAt", "LatestAt", "DurationMinutes", "Status", "ScheduledAppointmentId",
        "ScheduledAt", "CreatedAt", "UpdatedAt")
    VALUES (gen_random_uuid(), v_summary, v_visit, v_patient, v_doctor, v_hospital,
        '[DEMO] Discuss the fictional results and care plan', v_today + interval '1 day',
        v_today + interval '7 days', 30, 2, v_next_visit, v_now, v_now, v_now);
    INSERT INTO "CareLoopWorkflow" ("Id", "PatientId", "DoctorId", "AppointmentId", "ConsultationSummaryId",
        "Title", "Description", "Source", "Status", "ActivatedAt", "CreatedAt", "UpdatedAt")
    VALUES (v_loop, v_patient, v_doctor, v_visit, v_summary, '[DEMO] Your follow-up journey',
        'Fictional care-team tasks demonstrating continuity between visits.', 'ConferenceDemo', 2,
        v_now, v_now, v_now);
    INSERT INTO "CareLoopTask" ("Id", "WorkflowId", "Type", "Title", "Instructions", "DueAt", "Status", "CreatedAt", "UpdatedAt")
    VALUES
        (gen_random_uuid(), v_loop, 1, '[DEMO] Medication check-in', 'Try recording an example dose in the demo account.',
         v_today + interval '20 hours', 2, v_now, v_now),
        (gen_random_uuid(), v_loop, 2, '[DEMO] How are you feeling?', 'Enter a fictional response to show the patient check-in.',
         v_today + interval '21 hours', 2, v_now, v_now),
        (gen_random_uuid(), v_loop, 4, '[DEMO] Prepare for your visit', 'Review the sample results before the fictional appointment.',
         v_today + interval '1 day 10 hours', 1, v_now, v_now);

    -- 8. Patient-maintained emergency info. Sharing remains OFF until enabled in-app.
    INSERT INTO "EmergencyHealthProfiles" ("Id", "PatientId", "BloodType", "Allergies", "ChronicConditions",
        "CurrentMedications", "EmergencyContacts", "AdditionalNotes", "IsSharingEnabled", "CreatedAt", "UpdatedAt")
    VALUES (gen_random_uuid(), v_patient, 'O+', '[DEMO] Penicillin allergy — fictional',
        '[DEMO] Asthma — fictional', '[DEMO] DemoMed Morning and Evening (not real medicines)',
        'Demo family contact — no real phone number',
        'CONFERENCE DEMO. Not a real patient or a clinically verified record.', false, v_now, v_now);

    -- 9. In-app inbox only. No DeviceTokens, outbound webhooks or reminder jobs.
    INSERT INTO "PatientInboxNotification" ("Id", "UserId", "Category", "Title", "Body", "DedupeKey", "CreatedAt")
    VALUES
        (gen_random_uuid(), v_patient, 7, 'Welcome to the PIYA conference demo',
         'All people, providers and clinical records in this account are fictional. Explore the patient journey.',
         'conference-welcome-v1', v_now),
        (gen_random_uuid(), v_patient, 4, '[DEMO] Your lab results are ready',
         'Open My Health to compare the two sample blood panels.', 'conference-labs-v1', v_now - interval '2 hours'),
        (gen_random_uuid(), v_patient, 2, '[DEMO] Your refill is ready',
         'A simulated refill is ready at PIYA Conference Pharmacy. No real medicine has been ordered.',
         'conference-refill-v1', v_now - interval '30 minutes'),
        (gen_random_uuid(), v_patient, 5, '[DEMO] Follow-up confirmed',
         'Your fictional follow-up is tomorrow at 11:00 Baku time.', 'conference-visit-v1', v_now - interval '1 hour');

    INSERT INTO "AuditLogs" ("Id", "Action", "EntityType", "EntityId", "Description", "Metadata", "IsSuccess", "CreatedAt")
    VALUES (gen_random_uuid(), 'ConferenceDemoSeed', 'User', v_patient::text,
        'SQL operator created isolated fictional conference fixtures; not a real clinical action.',
        json_build_object('fixture', 'conference-v1', 'database', current_database(), 'operator', session_user)::text,
        true, v_now);
    RAISE NOTICE 'Created conference_patient (%). Staff logins are disabled. All records are fictional.', v_patient;
END $demo$;

COMMIT;

-- This output contains no password, hash, token or real patient information.
SELECT "Username", "Email", "FirstName", "LastName", "IsActive"
FROM "Users" WHERE "Username" = 'conference_patient';
