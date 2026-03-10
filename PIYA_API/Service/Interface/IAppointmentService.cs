using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing appointments with conflict detection
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// Book a new appointment with conflict detection
    /// </summary>
    Task<Appointment> BookAppointmentAsync(Appointment appointment);
    
    /// <summary>
    /// Get appointment by ID
    /// </summary>
    Task<Appointment?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get all appointments for a patient
    /// </summary>
    Task<List<Appointment>> GetPatientAppointmentsAsync(Guid patientId, AppointmentStatus? status = null);
    
    /// <summary>
    /// Get all appointments for a doctor
    /// </summary>
    Task<List<Appointment>> GetDoctorAppointmentsAsync(Guid doctorId, DateTime? date = null, AppointmentStatus? status = null);
    
    /// <summary>
    /// Check if doctor is available at specified time
    /// </summary>
    Task<bool> IsDoctorAvailableAsync(Guid doctorId, DateTime scheduledAt, int durationMinutes = 30);
    
    /// <summary>
    /// Update appointment status
    /// </summary>
    Task<Appointment> UpdateStatusAsync(Guid id, AppointmentStatus status, string? reason = null);
    
    /// <summary>
    /// Cancel appointment
    /// </summary>
    Task<Appointment> CancelAppointmentAsync(Guid id, Guid cancelledBy, string? reason);
    
    /// <summary>
    /// Reschedule appointment
    /// </summary>
    Task<Appointment> RescheduleAppointmentAsync(Guid id, DateTime newScheduledAt);
    
    /// <summary>
    /// Complete appointment
    /// </summary>
    Task<Appointment> CompleteAppointmentAsync(Guid id, string? doctorNotes);
    
    /// <summary>
    /// Get upcoming appointments for a hospital
    /// </summary>
    Task<List<Appointment>> GetHospitalAppointmentsAsync(Guid hospitalId, DateTime? date = null);

    /// <summary>
    /// Returns true when the doctor has at least one non-cancelled appointment with the patient.
    /// Used to gate access to patient records without loading full appointment lists.
    /// </summary>
    Task<bool> HasDoctorPatientRelationshipAsync(Guid doctorId, Guid patientId);

    /// <summary>
    /// Returns appointment counts for the doctor dashboard stats in a single DB query.
    /// </summary>
    Task<DoctorAppointmentCounts> GetDoctorAppointmentCountsAsync(Guid doctorId, DateTime asOf);
}

/// <summary>
/// Lightweight struct returned by <see cref="IAppointmentService.GetDoctorAppointmentCountsAsync"/>.
/// </summary>
public record DoctorAppointmentCounts(int TodayCount, int UpcomingCount, Appointment? NextAppointment);
