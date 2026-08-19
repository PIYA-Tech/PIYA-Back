using System.Globalization;
using System.Text;
using System.Text.Json;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Service.Class;

public class AzerbaijanPharmaceuticalRegistryService(
    PharmacyApiDbContext context,
    IHttpClientFactory httpClientFactory,
    ILogger<AzerbaijanPharmaceuticalRegistryService> logger) : IAzerbaijanPharmaceuticalRegistryService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<AzerbaijanPharmaceuticalRegistryService> _logger = logger;
    
    private const string ApiBaseUrl = "https://admin.opendata.az/api/3/action";
    private const string DatasetId = "derman-vasitelerinin-dovlet-reyestri";
    private const string SyncStateKey = "azerbaijan-pharmaceutical-registry";

    public async Task<RegistryMetadata?> GetRegistryMetadataAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"{ApiBaseUrl}/package_show?id={DatasetId}";
            
            _logger.LogInformation($"Fetching registry metadata from: {url}");
            
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<ApiResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (apiResponse?.Success == true && apiResponse.Result?.Resources?.Count > 0)
            {
                var resource = apiResponse.Result.Resources[0];
                
                return new RegistryMetadata
                {
                    Id = resource.Id,
                    Title = resource.NameTranslated?.En ?? resource.Name,
                    LastModified = DateTime.TryParse(resource.LastModified, out var lastMod) 
                        ? lastMod 
                        : DateTime.UtcNow,
                    FileSize = resource.Size,
                    DownloadUrl = resource.Url,
                    Format = resource.Format
                };
            }

            _logger.LogWarning("Registry metadata fetch returned no resources");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch registry metadata");
            return null;
        }
    }

    public async Task<Stream?> DownloadMedicationCsvAsync()
    {
        try
        {
            var metadata = await GetRegistryMetadataAsync();
            if (metadata == null || string.IsNullOrEmpty(metadata.DownloadUrl))
            {
                _logger.LogError("Cannot download CSV: No metadata or download URL");
                return null;
            }

            _logger.LogInformation($"Downloading CSV from: {metadata.DownloadUrl} (Size: {metadata.FileSize / 1024}KB)");
            
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5); // Large file download
            
            var response = await client.GetAsync(metadata.DownloadUrl);
            response.EnsureSuccessStatusCode();
            
            var stream = await response.Content.ReadAsStreamAsync();
            _logger.LogInformation("CSV download completed successfully");
            
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download medication CSV");
            return null;
        }
    }

    public async Task<int> ImportMedicationsFromCsvAsync(Stream csvStream)
    {
        // Real CSV column indices (confirmed from live data — 2025-12-04):
        // 0  №                                   row number         (skip)
        // 1  Dərmanın adı                         brand name
        // 2  BPA                                  generic + strength e.g. "Ibuprofen - 200 mg/5 ml"
        // 3  Köməkçi maddə                        excipients         (skip)
        // 4  Buraxılış forması və qablaşdırma     form + packaging   — fields contain embedded newlines
        // 5  İstehsalçı firma                     manufacturer
        // 6  Ərizəçi firma                        applicant          (skip)
        // 7  Buraxılış qaydası                    Rx flag            "Reseptsiz buraxılır" = OTC
        // 8  ATC                                  ATC code
        // 9+ registration details                 (skip)
        const int brandNameIdx    = 1;
        const int bpaIdx          = 2;
        const int formIdx         = 4;
        const int manufacturerIdx = 5;
        const int releaseOrderIdx = 7;
        const int atcIdx          = 8;

        var importedCount = 0;
        var errorCount    = 0;

        try
        {
            // Read entire stream into memory so the RFC-4180 tokeniser can handle
            // quoted fields that span multiple physical lines.
            var content = await new StreamReader(csvStream, detectEncodingFromByteOrderMarks: true)
                              .ReadToEndAsync();

            var rows = ParseCsvRows(content);
            if (rows.Count < 2)
            {
                _logger.LogError("CSV file is empty or has only a header");
                return 0;
            }

            _logger.LogInformation("CSV import started — {Total} data rows", rows.Count - 1);

            // Bulk-load existing keys to avoid per-row DB round-trips
            var existingKeys = await _context.Medications
                .Select(m => m.BrandName + "||" + (m.Manufacturer ?? ""))
                .ToHashSetAsync();

            var toAdd      = new List<Medication>();
            var rowNumber  = 0;

            foreach (var v in rows.Skip(1))   // skip header row
            {
                rowNumber++;
                try
                {
                    var brandName = GetCol(v, brandNameIdx);
                    if (string.IsNullOrWhiteSpace(brandName)) continue;

                    // BPA: "GenericName - Strength"  OR just "GenericName"
                    var bpaRaw = GetCol(v, bpaIdx);
                    string genericName, strength;
                    var dashIdx = bpaRaw.IndexOf(" - ", StringComparison.Ordinal);
                    if (dashIdx > 0)
                    {
                        genericName = bpaRaw[..dashIdx].Trim();
                        strength    = bpaRaw[(dashIdx + 3)..].Trim();
                        // Strength may have a second "ingredient - dose" part; keep only first segment
                        var semicolonIdx = strength.IndexOf(';');
                        if (semicolonIdx > 0) strength = strength[..semicolonIdx].Trim();
                    }
                    else
                    {
                        genericName = bpaRaw;
                        strength    = string.Empty;
                    }

                    // Form: first segment before newline / ';' / ','  (strip packaging info)
                    var formRaw = GetCol(v, formIdx);
                    var form    = Normalise(formRaw.Split(['\n', ';', ','])[0]);

                    // Manufacturer: first segment before newline / ';'
                    var mfrRaw       = GetCol(v, manufacturerIdx);
                    var manufacturer = Normalise(mfrRaw.Split(['\n', ';'])[0]);
                    // Strip trailing comma artefacts like "Berlin-Chemie AG,,"
                    manufacturer = manufacturer.TrimEnd(',', ' ');
                    if (string.IsNullOrWhiteSpace(manufacturer)) manufacturer = null;

                    // Rx flag
                    var releaseOrder = GetCol(v, releaseOrderIdx);
                    var requiresRx   = !releaseOrder.Contains("Reseptsiz", StringComparison.OrdinalIgnoreCase);

                    // ATC: first value before ';'
                    var atcCode = Normalise(GetCol(v, atcIdx).Split(';')[0]);

                    // Deduplication key
                    var key = brandName + "||" + (manufacturer ?? "");
                    if (existingKeys.Contains(key)) continue;
                    existingKeys.Add(key);

                    toAdd.Add(new Medication
                    {
                        Id                   = Guid.NewGuid(),
                        BrandName            = brandName,
                        GenericName          = string.IsNullOrWhiteSpace(genericName) ? brandName : genericName,
                        Strength             = strength,
                        Form                 = string.IsNullOrWhiteSpace(form) ? "Unknown" : form,
                        Manufacturer         = manufacturer,
                        AtcCode              = string.IsNullOrWhiteSpace(atcCode) ? null : atcCode,
                        RequiresPrescription = requiresRx,
                        ActiveIngredients    = string.IsNullOrWhiteSpace(genericName) ? [brandName] : [genericName],
                        GenericAlternatives  = [],
                        IsAvailable          = true,
                        Country              = "Azerbaijan",
                        CreatedAt            = DateTime.UtcNow,
                        UpdatedAt            = DateTime.UtcNow,
                    });

                    importedCount++;

                    if (toAdd.Count >= 500)
                    {
                        await _context.Medications.AddRangeAsync(toAdd);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("[Registry] Saved batch (running total: {Total})...", importedCount);
                        toAdd.Clear();
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogWarning("Error on CSV row {Row}: {Error}", rowNumber, ex.Message);
                }
            }

            if (toAdd.Count > 0)
            {
                await _context.Medications.AddRangeAsync(toAdd);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("[Registry] Import complete — {Imported} medications added ({Errors} parse errors)",
                importedCount, errorCount);

            return importedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import medications from CSV");
            throw;
        }
    }

    /// <summary>
    /// Collapse whitespace (including embedded newlines from multi-line fields) into a single space.
    /// </summary>
    private static string Normalise(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ").Trim();

    private static string GetCol(List<string> row, int idx) =>
        idx < row.Count ? row[idx].Trim() : string.Empty;

    public async Task<MedicationSyncResult> SyncMedicationsAsync()
    {
        var result = new MedicationSyncResult
        {
            SyncStartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting medication sync from Azerbaijan Pharmaceutical Registry...");

            // Check if update is available
            var metadata = await GetRegistryMetadataAsync();
            if (metadata == null)
            {
                result.Success = false;
                result.Errors.Add("Failed to fetch registry metadata");
                result.SyncCompletedAt = DateTime.UtcNow;
                return result;
            }

            _logger.LogInformation($"Registry last updated: {metadata.LastModified}");
            _logger.LogInformation($"File size: {metadata.FileSize / 1024 / 1024:F2} MB");

            // Download CSV
            var csvStream = await DownloadMedicationCsvAsync();
            if (csvStream == null)
            {
                result.Success = false;
                result.Errors.Add("Failed to download CSV file");
                result.SyncCompletedAt = DateTime.UtcNow;
                return result;
            }

            // Import medications
            var countBefore = await _context.Medications.CountAsync();
            result.TotalRecords = await ImportMedicationsFromCsvAsync(csvStream);
            var countAfter = await _context.Medications.CountAsync();

            result.NewRecords = countAfter - countBefore;
            result.UpdatedRecords = result.TotalRecords - result.NewRecords;
            result.Success = true;
            result.SyncCompletedAt = DateTime.UtcNow;

            var syncState = await _context.IntegrationSyncStates.FindAsync(SyncStateKey);
            if (syncState == null)
            {
                syncState = new IntegrationSyncState { Key = SyncStateKey };
                _context.IntegrationSyncStates.Add(syncState);
            }

            syncState.SourceLastModifiedAt = metadata.LastModified.ToUniversalTime();
            syncState.LastSuccessfulSyncAt = result.SyncCompletedAt;
            syncState.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Sync completed successfully in {DurationSeconds:F2} seconds", result.Duration.TotalSeconds);
            _logger.LogInformation(
                "Total records: {TotalRecords}, New: {NewRecords}, Updated: {UpdatedRecords}",
                result.TotalRecords, result.NewRecords, result.UpdatedRecords);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Medication sync failed");
            result.Success = false;
            result.Errors.Add($"Sync failed: {ex.Message}");
            result.SyncCompletedAt = DateTime.UtcNow;
            return result;
        }
    }

    public async Task<bool> IsUpdateAvailableAsync()
    {
        try
        {
            var metadata = await GetRegistryMetadataAsync();
            if (metadata == null) return false;

            var lastSync = await GetLastSyncDateAsync();
            if (lastSync == null) return true; // Never synced

            return metadata.LastModified > lastSync.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return false;
        }
    }

    public async Task<DateTime?> GetLastSyncDateAsync()
    {
        return await _context.IntegrationSyncStates
            .Where(item => item.Key == SyncStateKey)
            .Select(item => (DateTime?)item.SourceLastModifiedAt)
            .SingleOrDefaultAsync();
    }

    #region Helper Methods

    /// <summary>
    /// RFC 4180-compliant CSV parser. Handles:
    ///   - Quoted fields containing commas, double-quotes ("") and embedded newlines.
    /// Returns all rows (including the header as row 0).
    /// </summary>
    private static List<List<string>> ParseCsvRows(string content)
    {
        var rows    = new List<List<string>>();
        var current = new List<string>();
        var field   = new StringBuilder();
        var inQuote = false;
        var i       = 0;

        while (i < content.Length)
        {
            var c = content[i];

            if (inQuote)
            {
                if (c == '"')
                {
                    // Peek: escaped quote "" → emit one "
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuote = false;   // closing quote
                }
                else
                {
                    field.Append(c);   // content inside quotes (including \n)
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuote = true;
                }
                else if (c == ',')
                {
                    current.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    current.Add(field.ToString());
                    field.Clear();
                    if (current.Count > 0)
                        rows.Add(current);
                    current = [];
                }
                else if (c != '\r')
                {
                    field.Append(c);
                }
            }
            i++;
        }

        // Last field / row (file may not end with newline)
        current.Add(field.ToString());
        if (current.Any(f => f.Length > 0))
            rows.Add(current);

        return rows;
    }

    private static int FindHeaderIndex(List<string> headers, params string[] possibleNames)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            foreach (var name in possibleNames)
            {
                if (headers[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return -1; // Not found
    }

    private static string? GetValue(List<string> values, int index)
    {
        if (index < 0 || index >= values.Count)
            return null;
        
        var value = values[index].Trim('"', ' ');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    #endregion

    #region API Response DTOs

    private class ApiResponse
    {
        public bool Success { get; set; }
        public ResultData? Result { get; set; }
    }

    private class ResultData
    {
        public List<ResourceData>? Resources { get; set; }
    }

    private class ResourceData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public TranslatedName? NameTranslated { get; set; }
        public string Format { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Url { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
    }

    private class TranslatedName
    {
        public string? Az { get; set; }
        public string? En { get; set; }
        public string? Ru { get; set; }
    }

    #endregion
}
