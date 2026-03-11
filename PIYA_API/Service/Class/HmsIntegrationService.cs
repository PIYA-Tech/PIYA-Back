using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Hospital Management System (HMS) integration service
/// Connects to external HMS via REST API for patient records, appointments, billing, and lab results
/// </summary>
public class HmsIntegrationService : IHmsIntegrationService
{
    private readonly PharmacyApiDbContext _context;
    private readonly ILogger<HmsIntegrationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly string _hmsBaseUrl;
    private readonly string _hmsApiKey;
    private readonly bool _hmsEnabled;

    public HmsIntegrationService(
        PharmacyApiDbContext context,
        ILogger<HmsIntegrationService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;

        _hmsEnabled = configuration.GetValue<bool>("ExternalApis:Hms:Enabled", false);
        _hmsBaseUrl = configuration["ExternalApis:Hms:BaseUrl"] ?? "https://hms-api.example.com";
        _hmsApiKey = configuration["ExternalApis:Hms:ApiKey"] ?? "";

        _httpClient.BaseAddress = new Uri(_hmsBaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _hmsApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<HmsSyncResult> SyncPatientToHmsAsync(Guid userId)
    {
        try
        {
            if (!_hmsEnabled)
            {
                _logger.LogWarning("HMS integration is disabled. Patient sync skipped for user {UserId}", userId);
                return new HmsSyncResult
                {
                    Success = false,
                    ErrorMessage = "HMS integration is disabled",
                    SyncedAt = DateTime.UtcNow
                };
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return new HmsSyncResult
                {
                    Success = false,
                    ErrorMessage = "User not found",
                    SyncedAt = DateTime.UtcNow
                };
            }

            var payload = new
            {
                externalPatientId = user.Id.ToString(),
                firstName = user.FirstName,
                lastName = user.LastName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                dateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                syncedAt = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync("/api/patients/sync", payload);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                var hmsPatientId = result?.GetValueOrDefault("patientId")?.ToString();

                _logger.LogInformation("Patient {UserId} synced to HMS with ID {HmsPatientId}", userId, hmsPatientId);

                return new HmsSyncResult
                {
                    Success = true,
                    HmsRecordId = hmsPatientId,
                    SyncedAt = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>
                    {
                        { "syncType", "patient" },
                        { "userId", userId.ToString() }
                    }
                };
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to sync patient {UserId} to HMS: {Error}", userId, errorContent);

            return new HmsSyncResult
            {
                Success = false,
                ErrorMessage = $"HMS API returned {response.StatusCode}: {errorContent}",
                SyncedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing patient {UserId} to HMS", userId);
            return new HmsSyncResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SyncedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<HmsSyncResult> SyncAppointmentToHmsAsync(Guid appointmentId)
    {
        try
        {
            if (!_hmsEnabled)
            {
                return new HmsSyncResult { Success = false, ErrorMessage = "HMS integration is disabled", SyncedAt = DateTime.UtcNow };
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.Hospital)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null)
            {
                return new HmsSyncResult { Success = false, ErrorMessage = "Appointment not found", SyncedAt = DateTime.UtcNow };
            }

            var payload = new
            {
                externalAppointmentId = appointment.Id.ToString(),
                patientId = appointment.PatientId.ToString(),
                doctorId = appointment.DoctorId.ToString(),
                hospitalId = appointment.HospitalId.ToString(),
                scheduledAt = appointment.ScheduledAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                duration = appointment.DurationMinutes,
                status = appointment.Status.ToString(),
                reason = appointment.Reason,
                createdAt = appointment.CreatedAt
            };

            var response = await _httpClient.PostAsJsonAsync("/api/appointments/sync", payload);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                var hmsAppointmentId = result?.GetValueOrDefault("appointmentId")?.ToString();

                _logger.LogInformation("Appointment {AppointmentId} synced to HMS", appointmentId);

                return new HmsSyncResult
                {
                    Success = true,
                    HmsRecordId = hmsAppointmentId,
                    SyncedAt = DateTime.UtcNow
                };
            }

            return new HmsSyncResult
            {
                Success = false,
                ErrorMessage = $"HMS API returned {response.StatusCode}",
                SyncedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing appointment {AppointmentId} to HMS", appointmentId);
            return new HmsSyncResult { Success = false, ErrorMessage = ex.Message, SyncedAt = DateTime.UtcNow };
        }
    }

    public async Task<HmsPatientRecord> ImportPatientRecordAsync(string hmsPatientId)
    {
        try
        {
            if (!_hmsEnabled)
            {
                throw new InvalidOperationException("HMS integration is disabled");
            }

            var response = await _httpClient.GetAsync($"/api/patients/{hmsPatientId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var record = JsonSerializer.Deserialize<HmsPatientRecord>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Imported patient record from HMS: {HmsPatientId}", hmsPatientId);

            return record ?? throw new InvalidOperationException("Failed to deserialize HMS patient record");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing patient record from HMS: {HmsPatientId}", hmsPatientId);
            throw;
        }
    }

    public async Task<List<HmsLabResult>> ImportLabResultsAsync(Guid userId, DateTime? fromDate = null)
    {
        try
        {
            if (!_hmsEnabled)
            {
                return [];
            }

            var dateFilter = fromDate?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd");
            var response = await _httpClient.GetAsync($"/api/lab-results?patientId={userId}&fromDate={dateFilter}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to import lab results for user {UserId}: {StatusCode}", userId, response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<List<HmsLabResult>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Imported {Count} lab results from HMS for user {UserId}", results?.Count ?? 0, userId);

            return results ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing lab results from HMS for user {UserId}", userId);
            return [];
        }
    }

    public async Task<HmsBillingResult> SyncBillingToHmsAsync(Guid appointmentId, decimal amount, string description)
    {
        try
        {
            if (!_hmsEnabled)
            {
                return new HmsBillingResult
                {
                    Success = false,
                    ErrorMessage = "HMS integration is disabled",
                    BillingDate = DateTime.UtcNow
                };
            }

            var payload = new
            {
                appointmentId = appointmentId.ToString(),
                amount,
                currency = "AZN",
                description,
                billingDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            var response = await _httpClient.PostAsJsonAsync("/api/billing/sync", payload);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);

                return new HmsBillingResult
                {
                    Success = true,
                    InvoiceId = result?.GetValueOrDefault("invoiceId")?.ToString(),
                    TotalAmount = amount,
                    BillingDate = DateTime.UtcNow,
                    PaymentStatus = "Pending"
                };
            }

            return new HmsBillingResult
            {
                Success = false,
                ErrorMessage = $"HMS API returned {response.StatusCode}",
                BillingDate = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing billing to HMS for appointment {AppointmentId}", appointmentId);
            return new HmsBillingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                BillingDate = DateTime.UtcNow
            };
        }
    }

    public async Task<HmsVitalsRecord> GetPatientVitalsAsync(string hmsPatientId)
    {
        try
        {
            if (!_hmsEnabled)
            {
                throw new InvalidOperationException("HMS integration is disabled");
            }

            var response = await _httpClient.GetAsync($"/api/patients/{hmsPatientId}/vitals/latest");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var vitals = JsonSerializer.Deserialize<HmsVitalsRecord>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Retrieved vitals from HMS for patient {HmsPatientId}", hmsPatientId);

            return vitals ?? throw new InvalidOperationException("Failed to deserialize HMS vitals record");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vitals from HMS for patient {HmsPatientId}", hmsPatientId);
            throw;
        }
    }

    public async Task<HmsSyncResult> UpdateAdmissionStatusAsync(Guid userId, HmsAdmissionStatus status, Guid? hospitalId = null)
    {
        try
        {
            if (!_hmsEnabled)
            {
                return new HmsSyncResult { Success = false, ErrorMessage = "HMS integration is disabled", SyncedAt = DateTime.UtcNow };
            }

            var payload = new
            {
                patientId = userId.ToString(),
                admissionStatus = status.ToString(),
                hospitalId = hospitalId?.ToString(),
                updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            var response = await _httpClient.PostAsJsonAsync("/api/patients/admission-status", payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Updated admission status for user {UserId} to {Status}", userId, status);

                return new HmsSyncResult
                {
                    Success = true,
                    SyncedAt = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>
                    {
                        { "admissionStatus", status.ToString() },
                        { "hospitalId", hospitalId?.ToString() ?? "N/A" }
                    }
                };
            }

            return new HmsSyncResult
            {
                Success = false,
                ErrorMessage = $"HMS API returned {response.StatusCode}",
                SyncedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating admission status for user {UserId}", userId);
            return new HmsSyncResult { Success = false, ErrorMessage = ex.Message, SyncedAt = DateTime.UtcNow };
        }
    }

    public async Task<HmsHealthStatus> GetHmsHealthStatusAsync()
    {
        try
        {
            if (!_hmsEnabled)
            {
                return new HmsHealthStatus
                {
                    IsOnline = false,
                    ErrorMessage = "HMS integration is disabled",
                    LastSyncAt = DateTime.UtcNow
                };
            }

            var response = await _httpClient.GetAsync("/api/health");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<HmsHealthStatus>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return status ?? new HmsHealthStatus
                {
                    IsOnline = true,
                    Version = "Unknown",
                    LastSyncAt = DateTime.UtcNow,
                    PendingSyncCount = 0
                };
            }

            return new HmsHealthStatus
            {
                IsOnline = false,
                ErrorMessage = $"HMS API returned {response.StatusCode}",
                LastSyncAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking HMS health status");
            return new HmsHealthStatus
            {
                IsOnline = false,
                ErrorMessage = ex.Message,
                LastSyncAt = DateTime.UtcNow
            };
        }
    }
}
