namespace PIYA_API.Model;

// Role hierarchy (ascending privilege):
//   Patient, Doctor, Pharmacist, PharmacyManager,
//   HospitalDirector, PharmacyNetworkOwner, Admin, SuperAdmin
//
// HospitalDirector:
//   Dean / Medical Director of a single hospital.
//   Scoped read access to all doctors, appointments, and notes within their hospital.
//   Cannot modify system configuration or access other hospitals' data.
//
// PharmacyNetworkOwner:
//   Owner of a PharmacyCompany (network / chain of pharmacies).
//   Can view and manage all pharmacies, staff, and inventory within their company.
//   Cannot access prescriptions or patient medical records.

/// <summary>User roles for role-based access control (RBAC).</summary>
public enum UserRole
{
    Patient              = 1,
    Doctor               = 2,
    Pharmacist           = 3,
    PharmacyManager      = 4,
    HospitalDirector     = 5,
    PharmacyNetworkOwner = 6,
    Admin                = 7,
    SuperAdmin           = 8
}

