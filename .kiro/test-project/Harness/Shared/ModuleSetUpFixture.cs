using NUnit.Framework;

namespace MigrationTests.Playwright;

/// <summary>
/// Assembly-level set-up fixture for every test project (module projects and Harness.Tests).
/// NUnit only discovers set-up fixtures inside the test assembly, so this file is linked
/// into each test project and excluded from the Harness compile (see MigrationTests.Harness.csproj).
/// </summary>
[SetUpFixture]
public class ModuleSetUpFixture
{
    [OneTimeSetUp]
    public Task SetUp() => GlobalSetup.SetUpAsync();

    [OneTimeTearDown]
    public Task TearDown() => GlobalSetup.TearDownAsync();
}
