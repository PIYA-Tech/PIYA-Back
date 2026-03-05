using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Electronic Health Record (EHR) integration service supporting HL7 FHIR standards
/// </summary>
public interface IEhrIntegrationService
{
    /// <summary>
    /// Export patient data in FHIR format
    /// </summary>
    Task<FhirPatientResource> ExportPatientToFhirAsync(Guid userId);

    /// <summary>
    /// Import FHIR patient resource
    /// </summary>
    Task<EhrImportResult> ImportFhirPatientAsync(string fhirJson);

    /// <summary>
    /// Export prescription in FHIR MedicationRequest format
    /// </summary>
    Task<FhirMedicationRequest> ExportPrescriptionToFhirAsync(Guid prescriptionId);

    /// <summary>
    /// Sync patient medical history to EHR
    /// </summary>
    Task<EhrSyncResult> SyncMedicalHistoryAsync(Guid userId);

    /// <summary>
    /// Get patient timeline from EHR (encounters, procedures, observations)
    /// </summary>
    Task<List<EhrTimelineEvent>> GetPatientTimelineAsync(Guid userId, DateTime? fromDate = null);

    /// <summary>
    /// Share patient data with external provider via C-CDA
    /// </summary>
    Task<EhrShareResult> SharePatientDataAsync(Guid userId, string recipientIdentifier, List<string> dataCategories);

    /// <summary>
    /// Validate FHIR resource compliance
    /// </summary>
    Task<FhirValidationResult> ValidateFhirResourceAsync(string resourceType, string fhirJson);

    /// <summary>
    /// Get EHR interoperability status
    /// </summary>
    Task<EhrInteroperabilityStatus> GetInteroperabilityStatusAsync();
}

#region DTOs

public class FhirPatientResource
{
    public string ResourceType { get; set; } = "Patient";
    public string Id { get; set; } = string.Empty;
    public List<FhirIdentifier> Identifier { get; set; } = [];
    public bool Active { get; set; } = true;
    public List<FhirName> Name { get; set; } = [];
    public List<FhirTelecom> Telecom { get; set; } = [];
    public string Gender { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public List<FhirAddress> Address { get; set; } = [];
    public string? MaritalStatus { get; set; }
    public List<FhirCommunication> Communication { get; set; } = [];
}

public class FhirIdentifier
{
    public string System { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Type { get; set; }
}

public class FhirName
{
    public string Use { get; set; } = "official";
    public string Family { get; set; } = string.Empty;
    public List<string> Given { get; set; } = [];
    public string? Prefix { get; set; }
}

public class FhirTelecom
{
    public string System { get; set; } = string.Empty; // phone, email, fax
    public string Value { get; set; } = string.Empty;
    public string? Use { get; set; } // home, work, mobile
}

public class FhirAddress
{
    public string Use { get; set; } = "home";
    public string Type { get; set; } = "physical";
    public string? Line { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class FhirCommunication
{
    public FhirCodeableConcept Language { get; set; } = new();
    public bool Preferred { get; set; }
}

public class FhirCodeableConcept
{
    public List<FhirCoding> Coding { get; set; } = [];
    public string? Text { get; set; }
}

public class FhirCoding
{
    public string System { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Display { get; set; }
}

public class FhirMedicationRequest
{
    public string ResourceType { get; set; } = "MedicationRequest";
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string Intent { get; set; } = "order";
    public FhirReference Subject { get; set; } = new();
    public FhirReference Requester { get; set; } = new();
    public FhirCodeableConcept MedicationCodeableConcept { get; set; } = new();
    public DateTime AuthoredOn { get; set; }
    public List<FhirDosageInstruction> DosageInstruction { get; set; } = [];
    public FhirDispenseRequest? DispenseRequest { get; set; }
}

public class FhirReference
{
    public string Reference { get; set; } = string.Empty;
    public string? Display { get; set; }
}

public class FhirDosageInstruction
{
    public int Sequence { get; set; }
    public string? Text { get; set; }
    public FhirTiming? Timing { get; set; }
    public FhirDoseAndRate? DoseAndRate { get; set; }
}

public class FhirTiming
{
    public FhirRepeat? Repeat { get; set; }
}

public class FhirRepeat
{
    public int Frequency { get; set; }
    public decimal Period { get; set; }
    public string PeriodUnit { get; set; } = "d"; // s, min, h, d, wk, mo, a
}

public class FhirDoseAndRate
{
    public string? Type { get; set; }
    public FhirQuantity? DoseQuantity { get; set; }
}

public class FhirQuantity
{
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? System { get; set; }
    public string? Code { get; set; }
}

public class FhirDispenseRequest
{
    public int NumberOfRepeatsAllowed { get; set; }
    public FhirQuantity? Quantity { get; set; }
    public FhirDuration? ExpectedSupplyDuration { get; set; }
}

public class FhirDuration
{
    public decimal Value { get; set; }
    public string Unit { get; set; } = "days";
    public string System { get; set; } = "http://unitsofmeasure.org";
    public string Code { get; set; } = "d";
}

public class EhrImportResult
{
    public bool Success { get; set; }
    public Guid? UserId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = [];
    public DateTime ImportedAt { get; set; }
}

public class EhrSyncResult
{
    public bool Success { get; set; }
    public int RecordsSynced { get; set; }
    public DateTime SyncedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, int> SyncDetails { get; set; } = [];
}

public class EhrTimelineEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // Encounter, Procedure, Observation, Immunization
    public DateTime EventDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Provider { get; set; }
    public string? Location { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class EhrShareResult
{
    public bool Success { get; set; }
    public string? ShareId { get; set; }
    public string? AccessUrl { get; set; }
    public DateTime SharedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class FhirValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? FhirVersion { get; set; }
    public DateTime ValidatedAt { get; set; }
}

public class EhrInteroperabilityStatus
{
    public bool IsOperational { get; set; }
    public string FhirVersion { get; set; } = "R4";
    public List<string> SupportedResources { get; set; } = [];
    public bool HL7Enabled { get; set; }
    public bool CCDAEnabled { get; set; }
    public DateTime LastSyncAt { get; set; }
    public int PendingExports { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
