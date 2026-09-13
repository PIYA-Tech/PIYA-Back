# Clinical cases — initial implementation

The lock-screen QR / Live Activity work is explicitly deferred. Existing in-app
emergency tokens remain the identification/access entry point.

## Implemented

- Separate, explicitly accepted care episodes, not automatic admission on scan.
- Admission requires an unexpired, unrevoked emergency grant owned by the doctor,
  an active patient who is still sharing, an active assigned hospital, and explicit
  acceptance of attending responsibility.
- Ordinary doctors additionally require a current licence and an active explicit
  `ClinicalCase.Admit` permission with `ResourceId` equal to the hospital UUID.
  The general Doctor role, public directory, and QR do not grant this permission.
- The fixed conference doctor is limited to the fixed fictional patient and an
  assigned hospital. Its licence exception does not apply to other doctors.
- One active case per patient/hospital; one admission per grant. Duplicate requests
  return a conflict. Create a fresh emergency grant for a later admission.
- Case reads/writes are limited to the attending doctor with a current hospital
  assignment/permission. New cases do not extend access to historical patient data.
- Append-only admission, note, simulation, acknowledgement, resolution and discharge
  events. Case Version prevents stale/repeated writes. Discharged cases are read-only.
- Synthetic heart rate/oxygen-saturation samples and alerts only for server-marked
  demo cases. No caller-supplied vital measurements or machine ingestion endpoint.
- iOS Care: cases, admission, overview, chart notes, timeline, simulated trend charts,
  foreground simulation, stale-sample indicator, and a demonstration alerts inbox.
- Existing scheduled visits and pharmacist workflows remain available.

## API

All routes are under `/api/clinical-cases`, authenticated as Doctor.

- `GET /facilities`: hospitals the caller is authorised to admit into.
- `GET /`: newest 100 assigned cases (active and discharged).
- `GET /{id}`: case summary and timeline; reads are audited.
- `POST /admit`: `grantId`, `hospitalId`, `department`, `bed`, `reason`,
  `acceptResponsibility=true`.
- `POST /{id}/events`: `version`, `action`, `text`, optional `relatedEventId`.
  Actions: Note, DemoReading, DemoAlert, Acknowledge, Resolve, Discharge.
  Resolution requires prior acknowledgement and a documented outcome.

## Deployment

Apply migration `20260913204826_ClinicalCases` through the established reviewed
deployment workflow. It creates ClinicalCases and ClinicalCaseEvents plus indexes
and foreign keys; it does not seed, rewrite, or delete existing data.

No production migration, permissions grant, database write or deployment was
performed during implementation. Do not grant admission permission indiscriminately.
Review hospital authority, record retention/deletion and operational policy before
enabling real admissions. The new foreign keys prevent deletion of referenced
patients/hospitals/grants while case records exist; use an approved retention process.

The in-memory Debug `--preview` flow uses fictional examples and never calls the
backend. It is not proof that a backend deployment or a live hospital integration
has been completed.

## Conference walkthrough (iOS Debug preview)

Launch PIYA Care with `--preview --dark`. Open **Scan / Admit → Start fictional
admission demo**. Select the fictional hospital, enter an admission reason, explicitly
confirm identity/responsibility, and confirm admission. Open the case, select **Vitals**,
add a simulated reading or turn on foreground simulation, then trigger a demo alert.
Acknowledge it, enter a fictional response, and resolve it. Add notes under **Chart**.
Under **Overview**, enter a fictional discharge outcome and confirm discharge.
**Timeline** retains the case events. **Cases → Discharged** retains the closed case
for the lifetime of this preview session. Restarting the preview clears its data.

## Deliberately not active / next stages

1. Real hospital validation of admission permissions, patient identity matching,
   treatment authority/consent, retention, and staff identity/licensing processes.
2. Care-team membership, shift coverage and two-sided accepted handover/transfer.
3. Hospital orders, lab results, medication administration and patient-side case views.
4. Device integration: vendor/monitor selection, authenticated gateway, patient-device
   binding, timestamps/units/quality, duplicate and out-of-order handling, clock skew,
   disconnection detection, retention and validated test feeds.
5. Clinically governed alarm rules, delivery receipts, acknowledgement deadlines,
   escalation/coverage, device permissions, offline/retry behaviour and hospital
   validation. Existing demo alerts are NOT a clinical alarm or push service.
6. Lock-screen QR fallback, accessibility and Live Activity lifecycle (deferred).

Never use this prototype as a substitute for bedside monitoring or hospital alarms.
