using Xunit;

namespace PIYA_API.Tests.Load;

public sealed class OptInLoadTestAttribute : FactAttribute
{
    public OptInLoadTestAttribute()
    {
        if (Environment.GetEnvironmentVariable("PIYA_RUN_LOAD_TESTS") != "1")
            Skip = "Opt-in load test: set PIYA_RUN_LOAD_TESTS=1 and PIYA_LOAD_TEST_BASE_URL for an isolated API backed by a disposable test database.";
    }
}
