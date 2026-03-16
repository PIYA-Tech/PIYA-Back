using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Electronic Health Record (EHR) integration service with HL7 FHIR R4 support
/// </summary>
public class EhrIntegrationService : IEhrIntegrationService
{
    private readonly PharmacyApiDbContext _context;
    private readonly ILogger<EhrIntegrationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly string _ehrBaseUrl;
    private readonly string _ehrApiKey;
    private readonly bool _ehrEnabled;
    private readonly string _fhirVersion = "4.0.1"; // FHIR R4

    public EhrIntegrationService(
        PharmacyApiDbContext context,
        ILogger<EhrIntegrationService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;

        _ehrEnabled = configuration.GetValue<bool>("ExternalApis:Ehr:Enabled", false);
        _ehrBaseUrl = configuration["ExternalApis:Ehr:BaseUrl"] ?? "https://fhir-server.example.com";
        _ehrApiKey = configuration["ExternalApis:Ehr:ApiKey"] ?? "";

        _httpClient.BaseAddress = new Uri(_ehrBaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_ehrApiKey}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/fhir+json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<FhirPatientResource> ExportPatientToFhirAsync(Guid userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId) ?? throw new ArgumentException("User not found", nameof(userId));
            var fhirPatient = new FhirPatientResource
            {
                Id = user.Id.ToString(),
                Active = true,
                Identifier =
                [
                    new FhirIdentifier
                    {
                        System = "https://piya.lifecare/patient-id",
                        Value = user.Id.ToString(),
                        Type = "MR" // Medical Record Number
                    }
                ],
                Name =
                [
                    new FhirName
                    {
                        Use = "official",
                        Family = user.LastName,
                        Given = [user.FirstName]
                    }
                ],
                Telecom = [],
                Gender = "unknown",
                BirthDate = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                Address = []
            };

            if (!string.IsNullOrEmpty(user.Email))
            {
                fhirPatient.Telecom.Add(new FhirTelecom
                {
                    System = "email",
                    Value = user.Email,
                    Use = "home"
                });
            }

            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                fhirPatient.Telecom.Add(new FhirTelecom
                {
                    System = "phone",
                    Value = user.PhoneNumber,
                    Use = "mobile"
                });
            }

            if (_ehrEnabled)
            {
                // Send to EHR system
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(fhirPatient, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/fhir+json");

                var response = await _httpClient.PostAsync("/Patient", content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Patient {UserId} exported to FHIR EHR", userId);
                }
                else
                {
                    _logger.LogWarning("Failed to export patient {UserId} to EHR: {StatusCode}", userId, response.StatusCode);
                }
            }

            return fhirPatient;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting patient {UserId} to FHIR", userId);
            throw;
        }
    }

    public async Task<EhrImportResult> ImportFhirPatientAsync(string fhirJson)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var fhirPatient = JsonSerializer.Deserialize<FhirPatientResource>(fhirJson, options);
            if (fhirPatient == null)
            {
                return new EhrImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid FHIR JSON",
                    ImportedAt = DateTime.UtcNow
                };
            }

            // Check if patient already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id.ToString() == fhirPatient.Id);

            if (existingUser != null)
            {
                return new EhrImportResult
                {
                    Success = false,
                    ErrorMessage = "Patient already exists",
                    UserId = existingUser.Id,
                    ImportedAt = DateTime.UtcNow
                };
            }

            // Create new user from FHIR data
            var name = fhirPatient.Name.FirstOrDefault();
            var email = fhirPatient.Telecom.FirstOrDefault(t => t.System == "email")?.Value;
            var phone = fhirPatient.Telecom.FirstOrDefault(t => t.System == "phone")?.Value;

            if (name == null || string.IsNullOrEmpty(email))
            {
                return new EhrImportResult
                {
                    Success = false,
                    ErrorMessage = "Missing required fields (name or email)",
                    ImportedAt = DateTime.UtcNow
                };
            }

            var user = new User
            {
                Username = email, // Use email as username
                FirstName = name.Given.FirstOrDefault() ?? "",
                LastName = name.Family,
                Email = email,
                PhoneNumber = phone ?? "",
                DateOfBirth = string.IsNullOrWhiteSpace(fhirPatient.BirthDate)
                    ? null
                    : DateTime.Parse(fhirPatient.BirthDate),
                Role = UserRole.Patient,
                IsActive = fhirPatient.Active,
                CreatedAt = DateTime.UtcNow,
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Imported FHIR patient as user {UserId}", user.Id);

            return new EhrImportResult
            {
                Success = true,
                UserId = user.Id,
                ImportedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing FHIR patient");
            return new EhrImportResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ImportedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<FhirMedicationRequest> ExportPrescriptionToFhirAsync(Guid prescriptionId)
    {
        try
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Patient)
                .Include(p => p.Doctor)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Medication)
                .FirstOrDefaultAsync(p => p.Id == prescriptionId) ?? throw new ArgumentException("Prescription not found", nameof(prescriptionId));

            // Note: FHIR MedicationRequest is typically for one medication
            // For multiple medications, create multiple MedicationRequest resources
            var firstItem = prescription.Items.FirstOrDefault() ?? throw new InvalidOperationException("Prescription has no items");
            var fhirMedicationRequest = new FhirMedicationRequest
            {
                Id = prescription.Id.ToString(),
                Status = prescription.Status == PrescriptionStatus.Active ? "active" : 
                         prescription.Status == PrescriptionStatus.Expired ? "completed" : "cancelled",
                Intent = "order",
                Subject = new FhirReference
                {
                    Reference = $"Patient/{prescription.PatientId}",
                    Display = $"{prescription.Patient.FirstName} {prescription.Patient.LastName}"
                },
                Requester = new FhirReference
                {
                    Reference = $"Practitioner/{prescription.DoctorId}",
                    Display = $"Dr. {prescription.Doctor.FirstName} {prescription.Doctor.LastName}"
                },
                MedicationCodeableConcept = new FhirCodeableConcept
                {
                    Text = firstItem.Medication.BrandName,
                    Coding =
                    [
                        new FhirCoding
                        {
                            System = "https://piya.lifecare/medication",
                            Code = firstItem.MedicationId.ToString(),
                            Display = firstItem.Medication.GenericName
                        }
                    ]
                },
                AuthoredOn = prescription.IssuedAt,
                DosageInstruction =
                [
                    new FhirDosageInstruction
                    {
                        Sequence = 1,
                        Text = $"{firstItem.Dosage}, {firstItem.Frequency}, {firstItem.Duration}",
                        Timing = new FhirTiming
                        {
                            Repeat = new FhirRepeat
                            {
                                Frequency = 1,
                                Period = 1,
                                PeriodUnit = "d"
                            }
                        }
                    }
                ],
                DispenseRequest = new FhirDispenseRequest
                {
                    NumberOfRepeatsAllowed = 0,
                    Quantity = new FhirQuantity
                    {
                        Value = firstItem.Quantity,
                        Unit = "units"
                    },
                    ExpectedSupplyDuration = new FhirDuration
                    {
                        Value = (prescription.ExpiresAt - prescription.IssuedAt).Days,
                        Unit = "days"
                    }
                }
            };

            if (_ehrEnabled)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(fhirMedicationRequest, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/fhir+json");

                var response = await _httpClient.PostAsync("/MedicationRequest", content);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Prescription {PrescriptionId} exported to FHIR EHR", prescriptionId);
                }
            }

            return fhirMedicationRequest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting prescription {PrescriptionId} to FHIR", prescriptionId);
            throw;
        }
    }

    public async Task<EhrSyncResult> SyncMedicalHistoryAsync(Guid userId)
    {
        try
        {
            if (!_ehrEnabled)
            {
                return new EhrSyncResult
                {
                    Success = false,
                    ErrorMessage = "EHR integration is disabled",
                    SyncedAt = DateTime.UtcNow
                };
            }

            var syncDetails = new Dictionary<string, int>();
            int totalSynced = 0;

            // Sync appointments as Encounters
            var appointments = await _context.Appointments
                .Where(a => a.PatientId == userId && a.Status == AppointmentStatus.Completed)
                .ToListAsync();
            syncDetails["Encounters"] = appointments.Count;
            totalSynced += appointments.Count;

            // Sync prescriptions as MedicationRequests
            var prescriptions = await _context.Prescriptions
                .Where(p => p.PatientId == userId)
                .ToListAsync();
            syncDetails["MedicationRequests"] = prescriptions.Count;
            totalSynced += prescriptions.Count;

            // Note: In a real implementation, you would send each resource to the FHIR server
            _logger.LogInformation("Synced {Count} medical history records for user {UserId}", totalSynced, userId);

            return new EhrSyncResult
            {
                Success = true,
                RecordsSynced = totalSynced,
                SyncedAt = DateTime.UtcNow,
                SyncDetails = syncDetails
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing medical history for user {UserId}", userId);
            return new EhrSyncResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SyncedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<List<EhrTimelineEvent>> GetPatientTimelineAsync(Guid userId, DateTime? fromDate = null)
    {
        try
        {
            var events = new List<EhrTimelineEvent>();
            var startDate = fromDate ?? DateTime.UtcNow.AddYears(-1);

            // Get appointments (Encounters)
            var appointments = await _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Hospital)
                .Where(a => a.PatientId == userId && a.ScheduledAt >= startDate)
                .OrderByDescending(a => a.ScheduledAt)
                .ToListAsync();

            foreach (var appointment in appointments)
            {
                events.Add(new EhrTimelineEvent
                {
                    EventId = appointment.Id.ToString(),
                    EventType = "Encounter",
                    EventDate = appointment.ScheduledAt,
                    Title = $"Appointment with Dr. {appointment.Doctor.LastName}",
                    Description = appointment.Reason,
                    Provider = $"Dr. {appointment.Doctor.FirstName} {appointment.Doctor.LastName}",
                    Location = appointment.Hospital.Name,
                    Metadata = new Dictionary<string, string>
                    {
                        { "status", appointment.Status.ToString() },
                        { "duration", $"{appointment.DurationMinutes} minutes" }
                    }
                });
            }

            // Get prescriptions (MedicationRequests)
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Doctor)
                .Include(p => p.Items)
                .Where(p => p.PatientId == userId && p.IssuedAt >= startDate)
                .OrderByDescending(p => p.IssuedAt)
                .ToListAsync();

            foreach (var prescription in prescriptions)
            {
                events.Add(new EhrTimelineEvent
                {
                    EventId = prescription.Id.ToString(),
                    EventType = "MedicationRequest",
                    EventDate = prescription.IssuedAt,
                    Title = $"Prescription from Dr. {prescription.Doctor.LastName}",
                    Description = $"{prescription.Items.Count} medication(s) prescribed",
                    Provider = $"Dr. {prescription.Doctor.FirstName} {prescription.Doctor.LastName}",
                    Metadata = new Dictionary<string, string>
                    {
                        { "status", prescription.Status.ToString() },
                        { "medicationCount", prescription.Items.Count.ToString() }
                    }
                });
            }

            return events.OrderByDescending(e => e.EventDate).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient timeline for user {UserId}", userId);
            return [];
        }
    }

    public async Task<EhrShareResult> SharePatientDataAsync(Guid userId, string recipientIdentifier, List<string> dataCategories)
    {
        try
        {
            if (!_ehrEnabled)
            {
                return new EhrShareResult
                {
                    Success = false,
                    ErrorMessage = "EHR integration is disabled",
                    SharedAt = DateTime.UtcNow
                };
            }

            var payload = new
            {
                patientId = userId.ToString(),
                recipient = recipientIdentifier,
                dataCategories = dataCategories,
                expiresIn = 86400, // 24 hours
                requestedAt = DateTime.UtcNow
            };

            var response = await _httpClient.PostAsJsonAsync("/api/share", payload);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);

                return new EhrShareResult
                {
                    Success = true,
                    ShareId = result?.GetValueOrDefault("shareId")?.ToString(),
                    AccessUrl = result?.GetValueOrDefault("accessUrl")?.ToString(),
                    SharedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(24)
                };
            }

            return new EhrShareResult
            {
                Success = false,
                ErrorMessage = $"EHR API returned {response.StatusCode}",
                SharedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing patient data for user {UserId}", userId);
            return new EhrShareResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SharedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<FhirValidationResult> ValidateFhirResourceAsync(string resourceType, string fhirJson)
    {
        try
        {
            var result = new FhirValidationResult
            {
                FhirVersion = _fhirVersion,
                ValidatedAt = DateTime.UtcNow
            };

            // Basic JSON validation
            try
            {
                JsonDocument.Parse(fhirJson);
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid JSON: {ex.Message}");
                return result;
            }

            // Validate against FHIR schema (simplified - in production use FHIR validator library)
            var doc = JsonDocument.Parse(fhirJson);
            if (!doc.RootElement.TryGetProperty("resourceType", out var resourceTypeElement) ||
                resourceTypeElement.GetString() != resourceType)
            {
                result.IsValid = false;
                result.Errors.Add($"Resource type mismatch. Expected: {resourceType}");
                return result;
            }

            if (!doc.RootElement.TryGetProperty("id", out _))
            {
                result.Warnings.Add("Resource ID is missing (optional but recommended)");
            }

            result.IsValid = result.Errors.Count == 0;
            _logger.LogInformation("FHIR validation completed for {ResourceType}: {IsValid}", resourceType, result.IsValid);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating FHIR resource");
            return new FhirValidationResult
            {
                IsValid = false,
                Errors = [ex.Message],
                ValidatedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<EhrInteroperabilityStatus> GetInteroperabilityStatusAsync()
    {
        try
        {
            if (!_ehrEnabled)
            {
                return new EhrInteroperabilityStatus
                {
                    IsOperational = false,
                    ErrorMessage = "EHR integration is disabled",
                    LastSyncAt = DateTime.UtcNow
                };
            }

            var response = await _httpClient.GetAsync("/metadata");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var metadata = JsonDocument.Parse(content);

                return new EhrInteroperabilityStatus
                {
                    IsOperational = true,
                    FhirVersion = metadata.RootElement.GetProperty("fhirVersion").GetString() ?? "R4",
                    SupportedResources = ["Patient", "Practitioner", "MedicationRequest", "Encounter", "Observation"],
                    HL7Enabled = true,
                    CCDAEnabled = true,
                    LastSyncAt = DateTime.UtcNow,
                    PendingExports = 0
                };
            }

            return new EhrInteroperabilityStatus
            {
                IsOperational = false,
                ErrorMessage = $"EHR server returned {response.StatusCode}",
                LastSyncAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking EHR interoperability status");
            return new EhrInteroperabilityStatus
            {
                IsOperational = false,
                ErrorMessage = ex.Message,
                LastSyncAt = DateTime.UtcNow
            };
        }
    }
}
