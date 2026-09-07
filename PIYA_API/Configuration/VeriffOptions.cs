namespace PIYA_API.Configuration;

public sealed class VeriffOptions
{
    public const string Section = "Verification:Veriff";
    public bool Enabled { get; set; }
    // Sandbox decisions must never become real verified-patient badges.
    public bool LiveMode { get; set; }
    public string BaseUrl { get; set; } = "https://stationapi.veriff.com/";
    public string ApiKey { get; set; } = "";
    public string SharedSecret { get; set; } = "";
}
