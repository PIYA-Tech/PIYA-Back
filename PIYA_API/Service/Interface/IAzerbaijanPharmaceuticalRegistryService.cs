using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for integrating with Azerbaijan Ministry of Health's official pharmaceutical registry
/// Data source: https://admin.opendata.az
/// </summary>
public interface IAzerbaijanPharmaceuticalRegistryService
{
    /// <summary>
    /// Fetches the latest medication dataset metadata from the registry
    /// </summary>
    Task<RegistryMetadata?> GetRegistryMetadataAsync();
    
    /// <summary>
    /// Downloads the CSV file containing all registered medications
    /// </summary>
    Task<Stream?> DownloadMedicationCsvAsync();
    
    /// <summary>
    /// Parses CSV data and imports medications into the database
    /// </summary>
    Task<int> ImportMedicationsFromCsvAsync(Stream csvStream);
    
    /// <summary>
    /// Full sync: Download latest CSV and update database
    /// </summary>
    Task<MedicationSyncResult> SyncMedicationsAsync();
    
    /// <summary>
    /// Check if local database needs updating (compares last modified dates)
    /// </summary>
    Task<bool> IsUpdateAvailableAsync();
    
    /// <summary>
    /// Get the last successful sync timestamp
    /// </summary>
    Task<DateTime?> GetLastSyncDateAsync();
}

/// <summary>
/// Metadata about the pharmaceutical registry dataset
/// </summary>
public class RegistryMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
}

/// <summary>
/// Result of medication synchronization operation
/// </summary>
public class MedicationSyncResult
{
    public bool Success { get; set; }
    public int TotalRecords { get; set; }
    public int NewRecords { get; set; }
    public int UpdatedRecords { get; set; }
    public int FailedRecords { get; set; }
    public DateTime SyncStartedAt { get; set; }
    public DateTime SyncCompletedAt { get; set; }
    public List<string> Errors { get; set; } = [];
    
    public TimeSpan Duration => SyncCompletedAt - SyncStartedAt;
}
