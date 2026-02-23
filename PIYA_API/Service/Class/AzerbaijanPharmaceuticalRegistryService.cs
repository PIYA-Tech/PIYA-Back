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
        var importedCount = 0;
        var errors = new List<string>();
        
        try
        {
            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            
            // Read header
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(headerLine))
            {
                _logger.LogError("CSV file is empty or has no header");
                return 0;
            }

            var headers = ParseCsvLine(headerLine);
            _logger.LogInformation($"CSV Headers: {string.Join(", ", headers)}");
            
            // Expected columns (adjust based on actual CSV structure)
            var tradeNameIdx = FindHeaderIndex(headers, "Ticarət adı", "Trade Name", "TradeName");
            var genericNameIdx = FindHeaderIndex(headers, "Beynəlxalq qeyri-patent adı", "Generic Name", "GenericName");
            var manufacturerIdx = FindHeaderIndex(headers, "İstehsalçı", "Manufacturer");
            var formIdx = FindHeaderIndex(headers, "Dərman forması", "Dosage Form", "Form");
            var dosageIdx = FindHeaderIndex(headers, "Dozası", "Dosage", "Strength");

            var lineNumber = 1;
            while (!reader.EndOfStream)
            {
                lineNumber++;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var values = ParseCsvLine(line);
                    
                    var medication = new Medication
                    {
                        Id = Guid.NewGuid(),
                        BrandName = GetValue(values, tradeNameIdx) ?? "Unknown",
                        GenericName = GetValue(values, genericNameIdx) ?? "Unknown",
                        Manufacturer = GetValue(values, manufacturerIdx),
                        Form = GetValue(values, formIdx) ?? "Unknown",
                        Strength = GetValue(values, dosageIdx) ?? "Unknown",
                        
                        // Default values (can be enhanced with more CSV columns)
                        ActiveIngredients = [GetValue(values, genericNameIdx) ?? "Unknown"],
                        RequiresPrescription = true, // Conservative default
                        AtcCode = string.Empty,
                        GenericAlternatives = [],
                        IsAvailable = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Check if medication already exists (by brand name + manufacturer)
                    var existing = await _context.Medications
                        .FirstOrDefaultAsync(m => 
                            m.BrandName == medication.BrandName && 
                            m.Manufacturer == medication.Manufacturer);

                    if (existing == null)
                    {
                        await _context.Medications.AddAsync(medication);
                        importedCount++;
                    }
                    else
                    {
                        // Update existing medication
                        existing.GenericName = medication.GenericName;
                        existing.Form = medication.Form;
                        existing.Strength = medication.Strength;
                        existing.UpdatedAt = DateTime.UtcNow;
                        _context.Medications.Update(existing);
                    }

                    // Batch save every 100 records
                    if (importedCount % 100 == 0)
                    {
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Imported {importedCount} medications...");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Line {lineNumber}: {ex.Message}");
                    _logger.LogWarning($"Error parsing line {lineNumber}: {ex.Message}");
                }
            }

            // Final save
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Import completed: {importedCount} medications imported/updated");
            if (errors.Count > 0)
            {
                _logger.LogWarning($"Import had {errors.Count} errors");
            }

            return importedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import medications from CSV");
            throw;
        }
    }

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

            // Store last sync date (would need a configuration/settings table)
            _logger.LogInformation($"Sync completed successfully in {result.Duration.TotalSeconds:F2} seconds");
            _logger.LogInformation($"Total records: {result.TotalRecords}, New: {result.NewRecords}, Updated: {result.UpdatedRecords}");

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
        // TODO: Implement configuration/settings table to store last sync date
        // For now, return null (will trigger full sync on first run)
        await Task.CompletedTask;
        return null;
    }

    #region Helper Methods

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var inQuotes = false;
        var currentValue = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentValue.ToString().Trim());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }

        // Add last value
        values.Add(currentValue.ToString().Trim());

        return values;
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
