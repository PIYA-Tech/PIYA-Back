namespace PIYA_API.Model;

/// <summary>Durable watermark for an external data source.</summary>
public sealed class IntegrationSyncState
{
    public required string Key { get; set; }
    public DateTime SourceLastModifiedAt { get; set; }
    public DateTime LastSuccessfulSyncAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
