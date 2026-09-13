-- OWNER-OPERATED ONLY, after confirming deployment of commit 4e02519 or newer
-- with ConferenceDoctorMiddleware. Do not send the password in chat.
-- FIRST run this command BY ITSELF in psql and answer its prompt:
-- \prompt 'Choose a unique demo password (12–72 bytes): ' conference_password
-- THEN paste this file. Do not paste the prompt and SQL in one batch: psql could
-- otherwise consume a pasted SQL line as the password. The prompt may be visible
-- on screen; do this privately, outside screen sharing/recording.
-- This deliberately refuses password resets or activation of any other account.
\set ON_ERROR_STOP on
\if :{?conference_password}
UPDATE "Users"
SET "PasswordHash" = crypt(:'conference_password', gen_salt('bf', 12)),
    "IsActive" = true, "SecurityStamp" = gen_random_uuid(), "UpdatedAt" = now()
WHERE "Id" = 'b3456a28-a834-46cc-9bc3-59a70569bead'
  AND "Username" = 'conference_doctor'
  AND "Email" = 'conference.doctor@example.invalid'
  AND "Role" = 2 AND NOT "IsActive" AND "PasswordHash" = ''
  AND octet_length(:'conference_password') BETWEEN 12 AND 72;
\unset conference_password
SELECT "Username", "IsActive" FROM "Users"
WHERE "Id" = 'b3456a28-a834-46cc-9bc3-59a70569bead';
-- Expected: UPDATE 1 and IsActive = t. If UPDATE 0, stop and investigate.
\else
\echo 'Password not entered. Run the prompt separately first; no changes made.'
\endif
