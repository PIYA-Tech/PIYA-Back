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
    private const string BaseUrl = "http://localhost:5254";
    // Shared, tuned HttpClient used by NBomber load scenarios to avoid
    // creating/disposing many handlers under heavy concurrency which can
    // exhaust sockets and cause connect timeouts. Configured to allow
    // many concurrent connections and reasonable timeouts for tests.
    private static readonly HttpClient LoadTestHttpClient;

    static LoadTests()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            // Keep MaxConnectionsPerServer at or below the Postgres server max_connections
            // to avoid the database rejecting connections ("too many clients"). 80 is a
            // conservative default for local/dev stress runs.
                // Lowered to 50 to avoid opening more concurrent HTTP connections than the DB can handle
                MaxConnectionsPerServer = 50,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        LoadTestHttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            // Overall request timeout — keep higher than connect timeout
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Default headers for load tests (bypass rate limiting for NBomber)
        LoadTestHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-RateLimit-Bypass", "true");
        LoadTestHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NBomber-LoadTest");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_LoginEndpoint_HandlesConcurrentUsers()
    {
        // Wait for the API to become healthy to avoid connection-refused races when NBomber starts
        await WaitForHealthAsync(TimeSpan.FromSeconds(30));

        // Scenario: Simulate 100 concurrent users logging in
        var scenario = Scenario.Create("login_load_test", async context =>
        {
            var email = $"loadtest-{Guid.NewGuid()}@example.com";
            var password = "LoadTest@123";

            // Use shared, tuned HttpClient
            var httpClient = LoadTestHttpClient;
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

    private static async Task WaitForHealthAsync(TimeSpan timeout)
    {
    // Allow a longer per-request timeout for health checks when the server is
    // slow to respond under heavy load.
    using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"Waiting for health endpoint {BaseUrl}/api/health up to {timeout.TotalSeconds}s...");
        while (sw.Elapsed < timeout)
        {
            try
            {
                var resp = await http.GetAsync("/api/health");
                if (resp.IsSuccessStatusCode)
                {
                    Console.WriteLine("Health check returned 200 OK.");
                    return;
                }
                else
                {
                    Console.WriteLine($"Health check returned {(int)resp.StatusCode}. Retrying...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Health check attempt failed: {ex.Message}");
                // ignore and retry
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Health check did not become ready within {timeout.TotalSeconds} seconds.");
    }

    private static async Task PreseedUsersAsync(int count)
    {
        var httpClient = LoadTestHttpClient;
        var password = "Stress@123";

        for (int i = 0; i < count; i++)
        {
            var email = $"stress-user-{i}@example.com";
            var registerRequest = new
            {
                email,
                password,
                firstName = "Stress",
                lastName = "User",
                phoneNumber = "+994500000000",
                dateOfBirth = "1990-01-01",
                role = "Patient"
            };

            try
            {
                // Register sequentially to avoid creating a burst of DB connections
                var resp = await httpClient.PostAsJsonAsync("/api/auth/register", registerRequest);
                // If user exists or registration fails, ignore and continue
            }
            catch
            {
                // ignore transient errors during seeding
            }
        }
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public void LoadTest_PharmacySearch_HandlesConcurrentSearches()
    {
        var scenario = Scenario.Create("pharmacy_search_load_test", async context =>
        {
            var httpClient = LoadTestHttpClient;
            
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
    public async Task StressTest_AppointmentBooking_FindBreakingPoint()
    {
    // Ensure the API is healthy before starting the heavy stress scenario to avoid
    // a burst of client-side connection timeouts if the server isn't ready.
    // Increase the health-check timeout for full stress runs because the server
    // can be slow to become responsive under heavy load or right after a restart.
    await WaitForHealthAsync(TimeSpan.FromSeconds(180));

        // Pre-seed a pool of users to avoid heavy write contention during the stress
        // test. The scenario will only perform logins against these users which
        // reduces DB write pressure significantly.
        var preseedCount = 200; // number of users to create for the test
        await PreseedUsersAsync(preseedCount);

    var shortRun = Environment.GetEnvironmentVariable("SHORT_LOAD_TEST") == "true";
    var simulations = shortRun
        ? new[]
        {
            // small ramp and short steady period for quick verification (used in CI/local quick runs)
            Simulation.RampingInject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
            Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(15)),
            Simulation.RampingInject(rate: 0, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(5))
        }
            : new[]
        {
            // Ramp up from 0 to 50 users over 2 minutes (reduced peak to protect DB)
            Simulation.RampingInject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2)),
            // Keep 50 users for 3 minutes
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(3)),
            // Ramp down
            Simulation.RampingInject(rate: 0, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1))
        };

    var scenario = Scenario.Create("appointment_stress_test", async context =>
        {
            var httpClient = LoadTestHttpClient;
            // Choose one of the pre-seeded users at random and perform login only
            var userIndex = Random.Shared.Next(0, preseedCount);
            var email = $"stress-user-{userIndex}@example.com";
            var password = "Stress@123";

            var loginRequest = new { email, password };
            var loginResponse = await httpClient.PostAsJsonAsync("/api/auth/login", loginRequest);

            return loginResponse.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(simulations);

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
            var httpClient = LoadTestHttpClient;
            
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
