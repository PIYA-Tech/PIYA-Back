# Conference doctor sandbox

Reserved username: `conference_doctor`.
Reserved user ID: `b3456a28-a834-46cc-9bc3-59a70569bead`.
Only allowed patient: `conference_patient` / `178934fc-4829-4b1b-b008-f21d68e2de6c`.

## Provisioning

`conference-doctor.sql` creates an inactive account with an empty password hash, an inactive fictional clinic, an unlicensed/offline doctor profile, and five fictional appointments. It checks the patient's exact ID, username, email and role, refuses account collisions, and runs in a transaction. It was applied successfully to the live database on 13 September 2026. Do not run it again to reset credentials or duplicate appointments.

## Required deployment sequence

1. Deploy the backend with `ConferenceDoctorMiddleware` and the companion controller checks before activation. These work with the existing PIYA Care app; no mobile rebuild is required.
2. Confirm the deployed revision and healthy API. The account must stay inactive until this is confirmed.
3. The owner must choose and enter a unique password directly, using the project's BCrypt-compatible password hashing. Never copy another user's hash or use the shared demo-seeder password. Do not mark the fictional license verified or change its expired date.
4. Activate only the reserved doctor account after the password is set. Do not give admin permissions or change the UUID.
5. Log in to PIYA Care and test the fictional appointments, patient records and prescription workflow. Confirm other staff operations return 403. Keep conference credentials private.
6. Deactivate the account after the conference. Before rolling back the backend to a revision without these restrictions, deactivate it and revoke its sessions first.

## Scope

Middleware identifies the account by immutable ID, not editable display names or client claims about demo status. It denies every route except explicitly reviewed authentication, public medicine lookup, selected doctor-dashboard actions, and the scoped emergency demo described below. Hubs, token generation, account/profile updates, exports and alternative clinical controllers remain blocked. Existing authorization remains in place.

### Emergency demonstration

The reserved doctor may request emergency access ONLY using a current token from the fixed conference patient. The lookup is restricted to that patient before token matching, and the patient's ID, username, email, active state and Patient role are verified. No real medical license is assigned: the license-check exception applies only to this reserved demonstration identity and never permits another patient's data. Ordinary doctors retain the existing license check.

Requests and successful demo grant reads are audited; the grant reason is labeled fictional. Grants expire within 30 minutes or at token expiry, whichever comes first. Grant reads are restricted to the same demo doctor and patient and still enforce revocation, expiry and sharing state. Patient notification remains enabled. Generate a fresh token from the conference patient's app; do not paste it in chat or reuse exposed tokens.

The selected dashboard actions additionally filter lists and verify patient IDs before writes or patient-record queries. Even an accidentally assigned appointment for a real patient is denied. Prescriptions are explicitly labeled fictional and returned through DTOs. The doctor is excluded from public doctor-directory routes. No fake medical license is supplied.

The account is **not a separate tenant or general sandbox for arbitrary staff users**. Never provision a second demo doctor with a different UUID and assume it inherits these controls. The conference patient must remain fictional; never repurpose its UUID for a real person.

## Verification

- 27 conference-specific tests and 5 existing medical-authorization unit tests passed (32 total).
- `git diff --check` passed.
- The broader HTTP integration fixture refused to start without an explicitly configured disposable `*_test`/`*_audit` database. No production database was used for tests.
- Live SQL transaction confirmed the doctor inactive, password unset, and five appointments inserted.
- Authenticated end-to-end checks remain pending owner password entry and activation after deployment.
