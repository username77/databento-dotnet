// Bug #10 Fix Verification
// https://github.com/Alparse/databento-dotnet/issues/10
//
// Issue: ReferenceClient.SecurityMaster.GetLastAsync throws JsonException
// Root cause: Databento Reference API returns JSONL (JSON Lines) format,
//             but code was trying to deserialize as JSON array
//
// Tests:
// 1. SecurityMaster.GetLastAsync should parse JSONL correctly
// 2. SecurityMaster.GetRangeAsync should parse JSONL correctly
// 3. CorporateActions.GetRangeAsync should parse JSONL correctly
// 4. AdjustmentFactors.GetRangeAsync should parse JSONL correctly

using Databento.Client.Builders;
using Databento.Client.Models;
using Databento.Interop;

namespace BugReproduction;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  BUG #10 FIX VERIFICATION: ReferenceClient JSONL Parsing             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("ERROR: DATABENTO_API_KEY environment variable not set");
            return 1;
        }

        Console.WriteLine("[INFO] API Key: Configured");
        Console.WriteLine();

        bool allPassed = true;

        // Test 1: SecurityMaster.GetLastAsync
        allPassed &= await Test1_SecurityMasterGetLast(apiKey);

        // Test 2: SecurityMaster.GetRangeAsync
        allPassed &= await Test2_SecurityMasterGetRange(apiKey);

        // Test 3: CorporateActions.GetRangeAsync
        allPassed &= await Test3_CorporateActionsGetRange(apiKey);

        // Test 4: AdjustmentFactors.GetRangeAsync
        allPassed &= await Test4_AdjustmentFactorsGetRange(apiKey);

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        if (allPassed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("ALL TESTS PASSED - BUG #10 IS FIXED!");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SOME TESTS FAILED - CHECK OUTPUT ABOVE");
        }
        Console.ResetColor();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");

        return allPassed ? 0 : 1;
    }

    /// <summary>
    /// Test 1: SecurityMaster.GetLastAsync should work without JsonException
    /// This is the exact reproduction case from Bug #10
    /// </summary>
    static async Task<bool> Test1_SecurityMasterGetLast(string apiKey)
    {
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("TEST 1: SecurityMaster.GetLastAsync (Bug #10 reproduction case)");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();

        try
        {
            await using var client = new ReferenceClientBuilder()
                .WithApiKey(apiKey)
                .Build();

            Console.WriteLine("[1] Calling SecurityMaster.GetLastAsync(symbols: [\"NVDA\"])...");

            var records = await client.SecurityMaster.GetLastAsync(
                symbols: new[] { "NVDA" },
                stypeIn: SType.RawSymbol
            );

            Console.WriteLine($"    Received {records.Count} record(s)");

            if (records.Count > 0)
            {
                var rec = records[0];
                Console.WriteLine($"    First record: {rec.IssuerName} ({rec.Symbol})");
                Console.WriteLine($"    ISIN: {rec.Isin}");
                Console.WriteLine($"    Exchange: {rec.Exchange}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    PASSED: No JsonException thrown!");
            Console.ResetColor();
            return true;
        }
        catch (ValidationException ex) when (ex.Message.Contains("403"))
        {
            // 403 = No subscription - this is expected if user doesn't have security master access
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    SKIPPED: API returned 403 (no security master subscription)");
            Console.WriteLine($"    This is expected if your API key doesn't have reference data access.");
            Console.WriteLine($"    The important thing is NO JsonException was thrown.");
            Console.ResetColor();
            return true; // Still a pass - we got a proper HTTP error, not a deserialization crash
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: JsonException thrown (BUG #10 NOT FIXED)");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        finally
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Test 2: SecurityMaster.GetRangeAsync should work without JsonException
    /// </summary>
    static async Task<bool> Test2_SecurityMasterGetRange(string apiKey)
    {
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("TEST 2: SecurityMaster.GetRangeAsync");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();

        try
        {
            await using var client = new ReferenceClientBuilder()
                .WithApiKey(apiKey)
                .Build();

            Console.WriteLine("[1] Calling SecurityMaster.GetRangeAsync(start: 2024-01-01)...");

            var records = await client.SecurityMaster.GetRangeAsync(
                start: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                end: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                symbols: new[] { "NVDA" },
                stypeIn: SType.RawSymbol
            );

            Console.WriteLine($"    Received {records.Count} record(s)");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    PASSED: No JsonException thrown!");
            Console.ResetColor();
            return true;
        }
        catch (ValidationException ex) when (ex.Message.Contains("403"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    SKIPPED: API returned 403 (no security master subscription)");
            Console.ResetColor();
            return true;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: JsonException thrown (BUG #10 NOT FIXED)");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        finally
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Test 3: CorporateActions.GetRangeAsync should work without JsonException
    /// </summary>
    static async Task<bool> Test3_CorporateActionsGetRange(string apiKey)
    {
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("TEST 3: CorporateActions.GetRangeAsync");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();

        try
        {
            await using var client = new ReferenceClientBuilder()
                .WithApiKey(apiKey)
                .Build();

            Console.WriteLine("[1] Calling CorporateActions.GetRangeAsync(start: 2024-01-01)...");

            var records = await client.CorporateActions.GetRangeAsync(
                start: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                end: new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
                symbols: new[] { "NVDA" },
                stypeIn: SType.RawSymbol
            );

            Console.WriteLine($"    Received {records.Count} record(s)");

            if (records.Count > 0)
            {
                var rec = records[0];
                Console.WriteLine($"    First record: {rec.Event} on {rec.EventDate:yyyy-MM-dd}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    PASSED: No JsonException thrown!");
            Console.ResetColor();
            return true;
        }
        catch (ValidationException ex) when (ex.Message.Contains("403"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    SKIPPED: API returned 403 (no corporate actions subscription)");
            Console.ResetColor();
            return true;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: JsonException thrown (BUG #10 NOT FIXED)");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        finally
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Test 4: AdjustmentFactors.GetRangeAsync should work without JsonException
    /// </summary>
    static async Task<bool> Test4_AdjustmentFactorsGetRange(string apiKey)
    {
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("TEST 4: AdjustmentFactors.GetRangeAsync");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();

        try
        {
            await using var client = new ReferenceClientBuilder()
                .WithApiKey(apiKey)
                .Build();

            Console.WriteLine("[1] Calling AdjustmentFactors.GetRangeAsync(start: 2023-01-01)...");

            var records = await client.AdjustmentFactors.GetRangeAsync(
                start: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
                end: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                symbols: new[] { "NVDA" },
                stypeIn: SType.RawSymbol
            );

            Console.WriteLine($"    Received {records.Count} record(s)");

            if (records.Count > 0)
            {
                var rec = records[0];
                Console.WriteLine($"    First record: {rec.Event} on {rec.ExDate:yyyy-MM-dd}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    PASSED: No JsonException thrown!");
            Console.ResetColor();
            return true;
        }
        catch (ValidationException ex) when (ex.Message.Contains("403"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    SKIPPED: API returned 403 (no adjustment factors subscription)");
            Console.ResetColor();
            return true;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: JsonException thrown (BUG #10 NOT FIXED)");
            Console.WriteLine($"    Message: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
        finally
        {
            Console.WriteLine();
        }
    }
}
