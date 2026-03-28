using PIYA_API.DTOs;
using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing patient referrals
/// </summary>
public interface IReferralService
{
    /// <summary>
    /// Create a referral and optionally order medical tests.
    /// Automatically creates a shell result appointment and sends notifications.
    /// </summary>
    Task<Referral> CreateAsync(Referral referral, List<MedicalTestType> orderedTests);

    Task<Referral?> GetByIdAsync(Guid id);

    /// <summary>
    /// All referrals for a specific patient
    /// </summary>
    Task<List<Referral>> GetByPatientAsync(Guid patientId);

    /// <summary>
    /// Referrals sent by a doctor
    /// </summary>
    Task<List<Referral>> GetByReferringDoctorAsync(Guid doctorId);

    /// <summary>
    /// Referrals received by a doctor (assigned to them)
    /// </summary>
    Task<List<Referral>> GetByReferredDoctorAsync(Guid doctorId);

    /// <summary>
    /// Patient assigns a specific doctor to a pending referral
    /// </summary>
    Task<Referral> AssignDoctorAsync(Guid referralId, Guid doctorId);

    /// <summary>
    /// Receiving doctor accepts the referral
    /// </summary>
    Task<Referral> AcceptAsync(Guid referralId, Guid acceptingDoctorId);

    /// <summary>
    /// Receiving doctor declines the referral
    /// </summary>
    Task<Referral> DeclineAsync(Guid referralId, string? reason = null);

    /// <summary>
    /// Receiving doctor marks the referral complete and fills in result notes
    /// </summary>
    Task<Referral> CompleteAsync(Guid referralId, string resultNotes);

    /// <summary>
    /// Cancel a referral
    /// </summary>
    Task<Referral> CancelAsync(Guid referralId);

    /// <summary>
    /// List doctors whose specialty matches the referral's ReferredToSpecialty.
    /// Returns a <see cref="PIYA_API.DTOs.ReferralAvailableDoctorsDto"/> so callers can
    /// distinguish "no doctors registered for this specialty" from "all doctors full".
    /// </summary>
    Task<ReferralAvailableDoctorsDto> GetAvailableDoctorsAsync(Guid referralId);

    /// <summary>
    /// Forward an existing referral to another specialist, creating a child referral in the chain.
    /// The original referral is marked Completed; the new child referral is Pending.
    /// </summary>
    Task<Referral> ForwardAsync(Guid referralId, Guid forwardingDoctorId, Guid? targetDoctorId, MedicalSpecialization specialty, string reason);

    Task DeleteAsync(Guid id);
}
