using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using System.Net.Http.Json;
using Xunit;

namespace PIYA_API.Tests.Load;

/// <summary>
/// Load tests for critical API endpoints using NBomber
/// Run with: dotnet test --filter "Category=LoadTest"
/// </summary>
public class LoadTests
{
    private const string BaseUrl = "http://localhost:5000";

    [Fact]
    [Trait("Category", "LoadTest")]
    public void LoadTest_LoginEndpoint_HandlesConcurrentUsers()
    {
        // Scenario: Simulate 100 concurrent users logging in
        var scenario = Scenario.Create("login_load_test", async context =>
        {
            var email = $"loadtest-{Guid.NewGuid()}@example.com";
            var password = "LoadTest@123";

            // First register the user
            using var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var registerRequest = new
            {
                email,
                password,
                firstName = "Load",
                lastName = "Test",
                phoneNumber = "+994500000000",
                dateOfBirth = "1990-01-01",
                role = "Patient"
            };
            await httpClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Then login
            var loginRequest = new { email, password };
            var response = await httpClient.PostAsJsonAsync("/api/auth/login", loginRequest);

            return response.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Assert performance criteria
        var scen = stats.ScenarioStats[0];
        Assert.True(scen.Ok.Request.RPS > 5, "Should handle at least 5 requests per second");
        Assert.True(scen.Ok.Latency.Percent95 < 1000, "95th percentile latency should be under 1 second");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public void LoadTest_PharmacySearch_HandlesConcurrentSearches()
    {
        var scenario = Scenario.Create("pharmacy_search_load_test", async context =>
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            
            // Search pharmacies by radius
            var latitude = 40.4093;
            var longitude = 49.8671;
            var radius = 5000;

            var response = await httpClient.GetAsync(
                $"/api/pharmacy/searchByRadius?coordinates.Latitude={latitude}&coordinates.Longitude={longitude}&radius={radius}");

            return response.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(60))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        var scen = stats.ScenarioStats[0];
        Assert.True(scen.Ok.Request.RPS > 10, "Should handle at least 10 searches per second");
        Assert.True(scen.Ok.Latency.Percent99 < 2000, "99th percentile latency should be under 2 seconds");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public void StressTest_AppointmentBooking_FindBreakingPoint()
    {
        var scenario = Scenario.Create("appointment_stress_test", async context =>
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            
            // Register and login
            var email = $"stress-{Guid.NewGuid()}@example.com";
            var password = "Stress@123";

            var registerRequest = new
            {
                email,
                password,
                firstName = "Stress",
                lastName = "Test",
                phoneNumber = "+994500000000",
                dateOfBirth = "1990-01-01",
                role = "Patient"
            };
            
            var registerResponse = await httpClient.PostAsJsonAsync("/api/auth/register", registerRequest);
            
            if (!registerResponse.IsSuccessStatusCode)
            {
                return Response.Fail();
            }

            // Login to get token
            var loginRequest = new { email, password };
            var loginResponse = await httpClient.PostAsJsonAsync("/api/auth/login", loginRequest);
            
            return loginResponse.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Ramp up from 0 to 100 users over 2 minutes
            Simulation.RampingInject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2)),
            // Keep 100 users for 3 minutes
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(3)),
            // Ramp down
            Simulation.RampingInject(rate: 0, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-results")
            .Run();

        // Analyze results
        var scen = stats.ScenarioStats[0];
        Console.WriteLine($"Total requests: {scen.Ok.Request.Count}");
        Console.WriteLine($"Failed requests: {scen.Fail.Request.Count}");
        Console.WriteLine($"RPS: {scen.Ok.Request.RPS}");
        Console.WriteLine($"Mean latency: {scen.Ok.Latency.MeanMs}ms");
        Console.WriteLine($"95th percentile: {scen.Ok.Latency.Percent95}ms");
    }

    [Fact(Skip = "Run manually - endurance test")]
    [Trait("Category", "LoadTest")]
    public void EnduranceTest_ApiStability_LongRunning()
    {
        // Test system stability under moderate load over extended period
        var scenario = Scenario.Create("endurance_test", async context =>
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            
            // Mix of different endpoints
            var endpoints = new[]
            {
                "/health",
                "/api/pharmacy/searchByCountry?coordinates.Latitude=40.4&coordinates.Longitude=49.8",
                "/health/ready"
            };

            var randomEndpoint = endpoints[Random.Shared.Next(endpoints.Length)];
            var response = await httpClient.GetAsync(randomEndpoint);

            return response.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Steady load: 10 RPS for 30 minutes
            Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(30))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("endurance-test-results")
            .Run();

        var scen = stats.ScenarioStats[0];
        
        // Check for memory leaks or degradation
        var errorRate = (double)scen.Fail.Request.Count / (scen.Ok.Request.Count + scen.Fail.Request.Count) * 100;
        Assert.True(errorRate < 1, "Error rate should be less than 1% in endurance test");
    }
}
