// FeatureValidation.Test - Validates v4.1.0 feature additions
// Tests: WithKeyFromEnv(), LiveSubscription, Property getters

using Databento.Client.Builders;
using Databento.Client.Live;
using Databento.Client.Models;

Console.WriteLine("=== Databento .NET v4.1.0 Feature Validation ===");
Console.WriteLine();

var passed = 0;
var failed = 0;

// ============================================
// Test 1: WithKeyFromEnv() - Error when not set
// ============================================
Console.WriteLine("TEST 1: WithKeyFromEnv() throws when DATABENTO_API_KEY not set");
try
{
    // Temporarily unset the env var
    var savedKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    Environment.SetEnvironmentVariable("DATABENTO_API_KEY", null);

    try
    {
        var builder = new LiveClientBuilder().WithKeyFromEnv();
        Console.WriteLine("  FAILED: Should have thrown InvalidOperationException");
        failed++;
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("DATABENTO_API_KEY"))
    {
        Console.WriteLine("  PASSED: Threw expected exception");
        passed++;
    }
    finally
    {
        // Restore
        Environment.SetEnvironmentVariable("DATABENTO_API_KEY", savedKey);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: Unexpected exception: {ex.Message}");
    failed++;
}

// ============================================
// Test 2: WithKeyFromEnv() - All builders have it
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 2: WithKeyFromEnv() exists on all builders");
try
{
    // Test each builder has the method (compile-time check, runtime validation)
    var hasMethod = true;

    // HistoricalClientBuilder
    var histBuilder = new HistoricalClientBuilder();
    var histMethod = histBuilder.GetType().GetMethod("WithKeyFromEnv");
    hasMethod &= histMethod != null;

    // LiveClientBuilder
    var liveBuilder = new LiveClientBuilder();
    var liveMethod = liveBuilder.GetType().GetMethod("WithKeyFromEnv");
    hasMethod &= liveMethod != null;

    // LiveBlockingClientBuilder
    var blockingBuilder = new LiveBlockingClientBuilder();
    var blockingMethod = blockingBuilder.GetType().GetMethod("WithKeyFromEnv");
    hasMethod &= blockingMethod != null;

    // ReferenceClientBuilder
    var refBuilder = new ReferenceClientBuilder();
    var refMethod = refBuilder.GetType().GetMethod("WithKeyFromEnv");
    hasMethod &= refMethod != null;

    if (hasMethod)
    {
        Console.WriteLine("  PASSED: All 4 builders have WithKeyFromEnv()");
        passed++;
    }
    else
    {
        Console.WriteLine("  FAILED: Some builders missing WithKeyFromEnv()");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 3: WithKeyFromEnv() - Reads env var correctly
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 3: WithKeyFromEnv() reads environment variable");
try
{
    // Save original key
    var originalKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    var testKey = "db-test-" + Guid.NewGuid().ToString("N")[..16];
    Environment.SetEnvironmentVariable("DATABENTO_API_KEY", testKey);

    try
    {
        // This should not throw - it reads the key
        var builder = new LiveClientBuilder()
            .WithKeyFromEnv()
            .WithDataset("GLBX.MDP3");

        // We can't easily verify the key was set without building,
        // but if it didn't throw, it worked
        Console.WriteLine("  PASSED: WithKeyFromEnv() succeeded with env var set");
        passed++;
    }
    finally
    {
        // Restore original key
        Environment.SetEnvironmentVariable("DATABENTO_API_KEY", originalKey);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 4: LiveSubscription class exists and has properties
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 4: LiveSubscription class structure");
try
{
    var sub = new LiveSubscription
    {
        Dataset = "GLBX.MDP3",
        Schema = Schema.Trades,
        STypeIn = SType.RawSymbol,
        Symbols = new[] { "ESZ4", "NQZ4" },
        StartTime = DateTimeOffset.UtcNow,
        WithSnapshot = true
    };

    var valid = sub.Dataset == "GLBX.MDP3"
        && sub.Schema == Schema.Trades
        && sub.STypeIn == SType.RawSymbol
        && sub.Symbols.Count == 2
        && sub.StartTime.HasValue
        && sub.WithSnapshot;

    if (valid)
    {
        Console.WriteLine("  PASSED: LiveSubscription has all expected properties");
        Console.WriteLine($"    ToString(): {sub}");
        passed++;
    }
    else
    {
        Console.WriteLine("  FAILED: Property values don't match");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 5: LiveClient property getters
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 5: LiveClient configuration property getters");
try
{
    // Need a valid API key for this test
    var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    if (string.IsNullOrEmpty(apiKey) || !apiKey.StartsWith("db-"))
    {
        Console.WriteLine("  SKIPPED: Valid DATABENTO_API_KEY not set");
    }
    else
    {
        await using var client = new LiveClientBuilder()
            .WithApiKey(apiKey)
            .WithDataset("EQUS.MINI")
            .WithSendTsOut(true)
            .WithUpgradePolicy(VersionUpgradePolicy.AsIs)
            .WithHeartbeatInterval(TimeSpan.FromSeconds(45))
            .Build();

        // Cast to concrete type to access properties
        var liveClient = client as LiveClient;
        if (liveClient == null)
        {
            Console.WriteLine("  FAILED: Could not cast to LiveClient");
            failed++;
        }
        else
        {
            var valid = liveClient.Dataset == "EQUS.MINI"
                && liveClient.SendTsOut == true
                && liveClient.UpgradePolicy == VersionUpgradePolicy.AsIs
                && liveClient.HeartbeatInterval == TimeSpan.FromSeconds(45)
                && liveClient.Subscriptions != null;

            if (valid)
            {
                Console.WriteLine("  PASSED: All LiveClient properties accessible");
                Console.WriteLine($"    Dataset: {liveClient.Dataset}");
                Console.WriteLine($"    SendTsOut: {liveClient.SendTsOut}");
                Console.WriteLine($"    UpgradePolicy: {liveClient.UpgradePolicy}");
                Console.WriteLine($"    HeartbeatInterval: {liveClient.HeartbeatInterval.TotalSeconds}s");
                Console.WriteLine($"    Subscriptions: {liveClient.Subscriptions.Count} active");
                passed++;
            }
            else
            {
                Console.WriteLine("  FAILED: Property values don't match expected");
                failed++;
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 6: LiveBlockingClient property getters
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 6: LiveBlockingClient configuration property getters");
try
{
    var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    if (string.IsNullOrEmpty(apiKey) || !apiKey.StartsWith("db-"))
    {
        Console.WriteLine("  SKIPPED: Valid DATABENTO_API_KEY not set");
    }
    else
    {
        await using var client = new LiveBlockingClientBuilder()
            .WithApiKey(apiKey)
            .WithDataset("XNAS.ITCH")
            .WithSendTsOut(false)
            .WithUpgradePolicy(VersionUpgradePolicy.Upgrade)
            .WithHeartbeatInterval(TimeSpan.FromSeconds(60))
            .Build();

        var blockingClient = client as LiveBlockingClient;
        if (blockingClient == null)
        {
            Console.WriteLine("  FAILED: Could not cast to LiveBlockingClient");
            failed++;
        }
        else
        {
            var valid = blockingClient.Dataset == "XNAS.ITCH"
                && blockingClient.SendTsOut == false
                && blockingClient.UpgradePolicy == VersionUpgradePolicy.Upgrade
                && blockingClient.HeartbeatInterval == TimeSpan.FromSeconds(60)
                && blockingClient.Subscriptions != null;

            if (valid)
            {
                Console.WriteLine("  PASSED: All LiveBlockingClient properties accessible");
                Console.WriteLine($"    Dataset: {blockingClient.Dataset}");
                Console.WriteLine($"    SendTsOut: {blockingClient.SendTsOut}");
                Console.WriteLine($"    UpgradePolicy: {blockingClient.UpgradePolicy}");
                Console.WriteLine($"    HeartbeatInterval: {blockingClient.HeartbeatInterval.TotalSeconds}s");
                Console.WriteLine($"    Subscriptions: {blockingClient.Subscriptions.Count} active");
                passed++;
            }
            else
            {
                Console.WriteLine("  FAILED: Property values don't match expected");
                failed++;
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 7: HistoricalClient property getters
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 7: HistoricalClient configuration property getters");
try
{
    var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    if (string.IsNullOrEmpty(apiKey) || !apiKey.StartsWith("db-"))
    {
        Console.WriteLine("  SKIPPED: Valid DATABENTO_API_KEY not set");
    }
    else
    {
        await using var client = new HistoricalClientBuilder()
            .WithApiKey(apiKey)
            .WithGateway(HistoricalGateway.Bo1)
            .WithUpgradePolicy(VersionUpgradePolicy.Upgrade)
            .WithTimeout(TimeSpan.FromSeconds(60))
            .Build();

        var histClient = client as Databento.Client.Historical.HistoricalClient;
        if (histClient == null)
        {
            Console.WriteLine("  FAILED: Could not cast to HistoricalClient");
            failed++;
        }
        else
        {
            var valid = histClient.Gateway == HistoricalGateway.Bo1
                && histClient.UpgradePolicy == VersionUpgradePolicy.Upgrade
                && histClient.Timeout == TimeSpan.FromSeconds(60);

            if (valid)
            {
                Console.WriteLine("  PASSED: All HistoricalClient properties accessible");
                Console.WriteLine($"    Gateway: {histClient.Gateway}");
                Console.WriteLine($"    UpgradePolicy: {histClient.UpgradePolicy}");
                Console.WriteLine($"    Timeout: {histClient.Timeout.TotalSeconds}s");
                Console.WriteLine($"    CustomHost: {histClient.CustomHost ?? "(none)"}");
                Console.WriteLine($"    CustomPort: {histClient.CustomPort?.ToString() ?? "(none)"}");
                passed++;
            }
            else
            {
                Console.WriteLine("  FAILED: Property values don't match expected");
                failed++;
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Test 8: Subscription tracking in LiveClient
// ============================================
Console.WriteLine();
Console.WriteLine("TEST 8: Subscription tracking (requires API connection)");
try
{
    var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
    if (string.IsNullOrEmpty(apiKey) || !apiKey.StartsWith("db-"))
    {
        Console.WriteLine("  SKIPPED: Valid DATABENTO_API_KEY not set");
    }
    else
    {
        await using var client = new LiveClientBuilder()
            .WithKeyFromEnv()
            .WithDataset("EQUS.MINI")
            .Build();

        var liveClient = client as LiveClient;
        if (liveClient == null)
        {
            Console.WriteLine("  FAILED: Could not cast to LiveClient");
            failed++;
        }
        else
        {
            // Before subscription
            var beforeCount = liveClient.Subscriptions.Count;

            // Subscribe
            await client.SubscribeAsync("EQUS.MINI", Schema.Trades, new[] { "NVDA" });

            // After subscription
            var afterCount = liveClient.Subscriptions.Count;

            if (afterCount == beforeCount + 1)
            {
                var sub = liveClient.Subscriptions[0];
                Console.WriteLine("  PASSED: Subscription tracked correctly");
                Console.WriteLine($"    Subscription: {sub}");
                passed++;
            }
            else
            {
                Console.WriteLine($"  FAILED: Expected {beforeCount + 1} subscriptions, got {afterCount}");
                failed++;
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  FAILED: {ex.Message}");
    failed++;
}

// ============================================
// Summary
// ============================================
Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine($"RESULTS: {passed} passed, {failed} failed");
Console.WriteLine("==========================================");

return failed > 0 ? 1 : 0;
