using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing medical tests ordered within a referral
/// </summary>
public interface IMedicalTestService
{
    Task<MedicalTest> CreateAsync(MedicalTest test);

    Task<MedicalTest?> GetByIdAsync(Guid id);

    /// <summary>
    /// All tests belonging to a referral
    /// </summary>
    Task<List<MedicalTest>> GetByReferralAsync(Guid referralId);

    /// <summary>
    /// All tests performed in a specific appointment
    /// </summary>
    Task<List<MedicalTest>> GetByAppointmentAsync(Guid appointmentId);

    /// <summary>
    /// Update a test's status and optionally record findings
    /// </summary>
    Task<MedicalTest> UpdateStatusAsync(
        Guid id,
        MedicalTestStatus status,
        string? findings = null,
        Guid? performedByDoctorId = null);

    /// <summary>
    /// Attach an uploaded MedicalDocument to a test result
    /// </summary>
    Task<MedicalTest> AttachDocumentAsync(
        Guid testId,
        Guid documentId,
        Guid attachingUserId,
        bool isAdministrator = false);

    /// <summary>
    /// All tests for a patient (across all referrals and standalone/emergency tests).
    /// </summary>
    Task<List<MedicalTest>> GetByPatientAsync(Guid patientId);

    /// <summary>
    /// Create a standalone test that is NOT linked to any referral
    /// (e.g. walk-in / ambulance encounter). PatientId and OrderedByDoctorId must be set;
    /// ReferralId should be null; IsEmergency should be true.
    /// </summary>
    Task<MedicalTest> CreateStandaloneAsync(MedicalTest test);
}
