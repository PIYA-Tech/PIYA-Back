-- Creates a disabled, passwordless conference identity. Safe to stage before deployment.
-- Do not change this UUID: ConferenceDoctorScope enforces restrictions using this identity.
-- No real accounts are modified. Re-running refuses to overwrite an existing account.
\set ON_ERROR_STOP on
BEGIN;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"='178934fc-4829-4b1b-b008-f21d68e2de6c'
    AND "Username"='conference_patient' AND "Email"='conference.patient@example.invalid' AND "Role"=1)
  THEN RAISE EXCEPTION 'Expected fictional conference patient not found; refusing'; END IF;
  IF EXISTS (SELECT 1 FROM "Users" WHERE "Id"='b3456a28-a834-46cc-9bc3-59a70569bead'
    OR "Username"='conference_doctor' OR "Email"='conference.doctor@example.invalid')
  THEN RAISE EXCEPTION 'Conference doctor already exists; refusing to overwrite'; END IF;
END $$;
INSERT INTO "Users" ("Id","Username","FirstName","LastName","Email","PhoneNumber","PasswordHash",
  "Role","IsActive","IsEmailVerified","IsPhoneVerified","CreatedAt","UpdatedAt","SecurityStamp")
VALUES ('b3456a28-a834-46cc-9bc3-59a70569bead','conference_doctor','Conference','Doctor — FICTIONAL DEMO',
  'conference.doctor@example.invalid','','',2,false,false,false,now(),now(),gen_random_uuid());
INSERT INTO "Hospitals" ("Id","Name","Address","City","Country","PhoneNumber","Email","Departments",
  "EmergencyContact","IsActive","CreatedAt","UpdatedAt")
VALUES ('03b38d07-e527-46cd-98cb-d53283b18245','PIYA Conference Clinic — FICTIONAL',
  'Demonstration only — not a real clinic','Baku','Azerbaijan','','conference.clinic@example.invalid',
  ARRAY['Demo General Practice'],'',false,now(),now());
INSERT INTO "DoctorProfiles" ("Id","UserId","LicenseNumber","LicenseAuthority","LicenseExpiryDate",
  "Specialization","AdditionalSpecializations","YearsOfExperience","Certifications","Education","Languages",
  "Biography","AcceptingNewPatients","CurrentStatus","HospitalIds","AverageAppointmentDuration",
  "TotalPatientsTreated","TotalRatings","CreatedAt","UpdatedAt")
VALUES ('cc568a4e-385b-4eb7-9953-d5660c7586c8','b3456a28-a834-46cc-9bc3-59a70569bead','','Not licensed — fictional demo',
  '2000-01-01T00:00:00Z',1,ARRAY[]::integer[],0,ARRAY[]::text[],ARRAY[]::text[],
  ARRAY['Azerbaijani','English','Russian'],'Conference demonstration only. Not a real clinician.',false,0,
  ARRAY['03b38d07-e527-46cd-98cb-d53283b18245']::uuid[],30,0,0,now(),now());
INSERT INTO "Appointments" ("Id","PatientId","DoctorId","HospitalId","ScheduledAt","DurationMinutes","Status",
  "Reason","AppointmentNotes","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(),'178934fc-4829-4b1b-b008-f21d68e2de6c','b3456a28-a834-46cc-9bc3-59a70569bead',
  '03b38d07-e527-46cd-98cb-d53283b18245',now()+v.offset_days*interval '1 day',30,v.status,
  v.reason,'FICTIONAL CONFERENCE DATA — not medical advice.',now(),now()
FROM (VALUES
  (-7,4,'DEMO: completed wellbeing review'),
  (-2,4,'DEMO: completed follow-up consultation'),
  (0,2,'DEMO: today’s consultation — start and complete in PIYA Care'),
  (1,2,'DEMO: tomorrow’s follow-up'),
  (3,1,'DEMO: upcoming routine consultation')
) AS v(offset_days,status,reason);
COMMIT;
SELECT "Username","IsActive",("PasswordHash"='') AS "PasswordNotSet" FROM "Users"
WHERE "Id"='b3456a28-a834-46cc-9bc3-59a70569bead';
