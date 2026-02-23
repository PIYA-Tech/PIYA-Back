using System.Text.Json;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Google Maps API service implementation
/// </summary>
public class GoogleMapsService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GoogleMapsService> logger) : IGoogleMapsService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<GoogleMapsService> _logger = logger;
    private readonly bool _isEnabled = configuration.GetValue<bool>("ExternalApis:GoogleMaps:Enabled");
    private readonly string _apiKey = configuration["ExternalApis:GoogleMaps:ApiKey"] ?? string.Empty;
    private readonly string _geocodeEndpoint = configuration["ExternalApis:GoogleMaps:GeocodeEndpoint"]
            ?? "https://maps.googleapis.com/maps/api/geocode/json";
    private readonly string _distanceMatrixEndpoint = configuration["ExternalApis:GoogleMaps:DistanceMatrixEndpoint"]
            ?? "https://maps.googleapis.com/maps/api/distancematrix/json";

    public async Task<GeocodeResult?> GeocodeAddressAsync(string address)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return null;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("Address is null or empty");
            return null;
        }

        try
        {
            var url = $"{_geocodeEndpoint}?address={Uri.EscapeDataString(address)}&key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(json);

            if (result?.Status == "OK" && result.Results?.Count > 0)
            {
                var firstResult = result.Results[0];
                var location = firstResult.Geometry?.Location;

                if (location == null)
                    return null;

                var geocodeResult = new GeocodeResult
                {
                    Latitude = location.Lat,
                    Longitude = location.Lng,
                    FormattedAddress = firstResult.FormattedAddress ?? address
                };

                // Extract city and country from address components
                if (firstResult.AddressComponents != null)
                {
                    foreach (var component in firstResult.AddressComponents)
                    {
                        if (component.Types?.Contains("locality") == true)
                            geocodeResult.City = component.LongName;
                        else if (component.Types?.Contains("country") == true)
                            geocodeResult.Country = component.LongName;
                        else if (component.Types?.Contains("postal_code") == true)
                            geocodeResult.PostalCode = component.LongName;
                    }
                }

                _logger.LogInformation("Geocoded address '{Address}' to {Lat},{Lng}", 
                    address, geocodeResult.Latitude, geocodeResult.Longitude);

                return geocodeResult;
            }

            _logger.LogWarning("Geocoding failed with status: {Status}", result?.Status);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to geocode address: {Address}", address);
            return null;
        }
    }

    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return null;
        }

        try
        {
            var url = $"{_geocodeEndpoint}?latlng={latitude},{longitude}&key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(json);

            if (result?.Status == "OK" && result.Results?.Count > 0)
            {
                var address = result.Results[0].FormattedAddress;
                _logger.LogInformation("Reverse geocoded {Lat},{Lng} to '{Address}'", 
                    latitude, longitude, address);
                return address;
            }

            _logger.LogWarning("Reverse geocoding failed with status: {Status}", result?.Status);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reverse geocode coordinates: {Lat},{Lng}", latitude, longitude);
            return null;
        }
    }

    public async Task<string?> GetCountryFromCoordinatesAsync(double latitude, double longitude)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return null;
        }

        try
        {
            var url = $"{_geocodeEndpoint}?latlng={latitude},{longitude}&key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(json);

            if (result?.Status == "OK" && result.Results?.Count > 0)
            {
                foreach (var geocodeResult in result.Results)
                {
                    if (geocodeResult.AddressComponents != null)
                    {
                        foreach (var component in geocodeResult.AddressComponents)
                        {
                            if (component.Types?.Contains("country") == true)
                            {
                                _logger.LogInformation("Found country '{Country}' for {Lat},{Lng}", 
                                    component.LongName, latitude, longitude);
                                return component.LongName;
                            }
                        }
                    }
                }
            }

            _logger.LogWarning("Country not found for coordinates: {Lat},{Lng}", latitude, longitude);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get country from coordinates: {Lat},{Lng}", latitude, longitude);
            return null;
        }
    }

    public async Task<string?> GetCityFromCoordinatesAsync(double latitude, double longitude)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return null;
        }

        try
        {
            var url = $"{_geocodeEndpoint}?latlng={latitude},{longitude}&key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(json);

            if (result?.Status == "OK" && result.Results?.Count > 0)
            {
                foreach (var geocodeResult in result.Results)
                {
                    if (geocodeResult.AddressComponents != null)
                    {
                        foreach (var component in geocodeResult.AddressComponents)
                        {
                            if (component.Types?.Contains("locality") == true)
                            {
                                _logger.LogInformation("Found city '{City}' for {Lat},{Lng}", 
                                    component.LongName, latitude, longitude);
                                return component.LongName;
                            }
                        }
                    }
                }
            }

            _logger.LogWarning("City not found for coordinates: {Lat},{Lng}", latitude, longitude);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get city from coordinates: {Lat},{Lng}", latitude, longitude);
            return null;
        }
    }

    public async Task<DistanceResult?> CalculateDistanceAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        string mode = "driving")
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return null;
        }

        try
        {
            var origins = $"{originLat},{originLng}";
            var destinations = $"{destLat},{destLng}";
            var url = $"{_distanceMatrixEndpoint}?origins={origins}&destinations={destinations}&mode={mode}&key={_apiKey}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleDistanceMatrixResponse>(json);

            if (result?.Status == "OK" && 
                result.Rows?.Count > 0 && 
                result.Rows[0].Elements?.Count > 0)
            {
                var element = result.Rows[0].Elements![0];
                if (element.Status == "OK" && element.Distance != null && element.Duration != null)
                {
                    var distanceResult = new DistanceResult
                    {
                        DistanceMeters = element.Distance.Value,
                        DurationSeconds = element.Duration.Value,
                        DistanceText = element.Distance.Text ?? string.Empty,
                        DurationText = element.Duration.Text ?? string.Empty
                    };

                    _logger.LogInformation(
                        "Calculated distance from ({OrigLat},{OrigLng}) to ({DestLat},{DestLng}): {Distance} km, {Duration} min",
                        originLat, originLng, destLat, destLng, 
                        distanceResult.DistanceKilometers, distanceResult.DurationMinutes);

                    return distanceResult;
                }
            }

            _logger.LogWarning("Distance calculation failed with status: {Status}", result?.Status);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate distance between ({OrigLat},{OrigLng}) and ({DestLat},{DestLng})", 
                originLat, originLng, destLat, destLng);
            return null;
        }
    }

    public async Task<List<DistanceResult>> CalculateDistancesAsync(
        double originLat, double originLng,
        List<(double lat, double lng)> destinations,
        string mode = "driving")
    {
        var results = new List<DistanceResult>();

        if (!_isEnabled)
        {
            _logger.LogWarning("Google Maps service is disabled");
            return results;
        }

        if (destinations == null || destinations.Count == 0)
        {
            _logger.LogWarning("No destinations provided");
            return results;
        }

        try
        {
            // Google Distance Matrix API supports multiple destinations in one call
            var origins = $"{originLat},{originLng}";
            var destString = string.Join("|", destinations.Select(d => $"{d.lat},{d.lng}"));
            var url = $"{_distanceMatrixEndpoint}?origins={origins}&destinations={destString}&mode={mode}&key={_apiKey}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GoogleDistanceMatrixResponse>(json);

            if (result?.Status == "OK" && result.Rows?.Count > 0)
            {
                var elements = result.Rows[0].Elements;
                if (elements != null)
                {
                    for (int i = 0; i < elements.Count; i++)
                    {
                        var element = elements[i];
                        if (element.Status == "OK" && element.Distance != null && element.Duration != null)
                        {
                            results.Add(new DistanceResult
                            {
                                DistanceMeters = element.Distance.Value,
                                DurationSeconds = element.Duration.Value,
                                DistanceText = element.Distance.Text ?? string.Empty,
                                DurationText = element.Duration.Text ?? string.Empty
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("Calculated distances from origin to {Count} destinations", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate distances to multiple destinations");
            return results;
        }
    }
}

#region Google API Response Models

internal class GoogleGeocodeResponse
{
    public string Status { get; set; } = string.Empty;
    public List<GoogleGeocodeResult>? Results { get; set; }
}

internal class GoogleGeocodeResult
{
    public string? FormattedAddress { get; set; }
    public GoogleGeometry? Geometry { get; set; }
    public List<GoogleAddressComponent>? AddressComponents { get; set; }
}

internal class GoogleGeometry
{
    public GoogleLocation? Location { get; set; }
}

internal class GoogleLocation
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

internal class GoogleAddressComponent
{
    public string? LongName { get; set; }
    public string? ShortName { get; set; }
    public List<string>? Types { get; set; }
}

internal class GoogleDistanceMatrixResponse
{
    public string Status { get; set; } = string.Empty;
    public List<GoogleDistanceMatrixRow>? Rows { get; set; }
}

internal class GoogleDistanceMatrixRow
{
    public List<GoogleDistanceMatrixElement>? Elements { get; set; }
}

internal class GoogleDistanceMatrixElement
{
    public string Status { get; set; } = string.Empty;
    public GoogleDistanceValue? Distance { get; set; }
    public GoogleDurationValue? Duration { get; set; }
}

internal class GoogleDistanceValue
{
    public double Value { get; set; }
    public string? Text { get; set; }
}

internal class GoogleDurationValue
{
    public int Value { get; set; }
    public string? Text { get; set; }
}

#endregion
