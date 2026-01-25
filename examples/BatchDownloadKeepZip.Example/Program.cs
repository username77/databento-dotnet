// BatchDownloadKeepZip.Example - Demonstrates the keepZip option for batch downloads
//
// This example shows how to use the keepZip parameter in BatchDownloadAsync
// to download batch job results as a zip archive instead of extracting the files.

using Databento.Client.Builders;
using Databento.Client.Models;

Console.WriteLine("=== Databento Batch Download with keepZip Example ===");
Console.WriteLine();
Console.WriteLine("This example demonstrates the keepZip option for BatchDownloadAsync.");
Console.WriteLine("When keepZip=true, files are downloaded and packaged into a zip archive.");
Console.WriteLine();

var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY")
    ?? throw new InvalidOperationException(
        "DATABENTO_API_KEY environment variable is not set.");

await using var client = new HistoricalClientBuilder()
    .WithApiKey(apiKey)
    .Build();

Console.WriteLine("Connected to Databento API");
Console.WriteLine();

// ============================================================================
// Step 1: Find or create a completed batch job
// ============================================================================

Console.WriteLine("Step 1: Finding a completed batch job...");
Console.WriteLine("-----------------------------------------");

var completedJobs = await client.BatchListJobsAsync(
    new[] { JobState.Done },
    DateTimeOffset.UtcNow.AddDays(-90));

string selectedJobId;

if (completedJobs.Count == 0)
{
    Console.WriteLine("No completed batch jobs found.");
    Console.WriteLine();
    Console.WriteLine("Would you like to submit a small batch job?");
    Console.WriteLine("This will incur a small API cost.");
    Console.WriteLine();
    Console.Write("Submit batch job? (y/n): ");

    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (response != "y" && response != "yes")
    {
        Console.WriteLine("Exiting.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("Submitting batch job...");

    var submittedJob = await client.BatchSubmitJobAsync(
        dataset: "EQUS.MINI",
        symbols: new[] { "AAPL" },
        schema: Schema.Trades,
        startTime: new DateTimeOffset(2024, 11, 1, 14, 30, 0, TimeSpan.Zero),
        endTime: new DateTimeOffset(2024, 11, 1, 14, 35, 0, TimeSpan.Zero));

    Console.WriteLine($"Job submitted: {submittedJob.Id}");
    Console.WriteLine($"Estimated cost: ${submittedJob.CostUsd:F4}");
    Console.WriteLine();
    Console.WriteLine("Waiting for job to complete...");

    selectedJobId = submittedJob.Id;
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var jobs = await client.BatchListJobsAsync();
        var currentJob = jobs.FirstOrDefault(j => j.Id == selectedJobId);

        if (currentJob == null)
        {
            Console.WriteLine("Job not found!");
            return;
        }

        Console.WriteLine($"  Status: {currentJob.State}");

        if (currentJob.State == JobState.Done)
        {
            Console.WriteLine("Job completed!");
            break;
        }

        if (currentJob.State == JobState.Expired)
        {
            Console.WriteLine("Job expired!");
            return;
        }
    }
}
else
{
    var job = completedJobs.First();
    selectedJobId = job.Id;
    Console.WriteLine($"Found {completedJobs.Count} completed job(s)");
    Console.WriteLine($"Using: {job.Id} ({job.Dataset}, {string.Join(", ", job.Symbols.Take(3))})");
}

Console.WriteLine();

// ============================================================================
// Step 2: Compare download methods
// ============================================================================

Console.WriteLine("Step 2: Comparing download methods");
Console.WriteLine("-----------------------------------");
Console.WriteLine();

var extractedDir = Path.Combine(Path.GetTempPath(), $"databento_extracted_{DateTime.Now:yyyyMMdd_HHmmss}");
var zipDir = Path.Combine(Path.GetTempPath(), $"databento_zip_{DateTime.Now:yyyyMMdd_HHmmss}");
Directory.CreateDirectory(extractedDir);
Directory.CreateDirectory(zipDir);

try
{
    // -------------------------------------------------------------------------
    // Method A: Standard download (extracts files)
    // -------------------------------------------------------------------------

    Console.WriteLine("Method A: BatchDownloadAsync (standard - extracts files)");
    Console.WriteLine($"  Output: {extractedDir}");
    Console.WriteLine("  Downloading...");

    var extractedPaths = await client.BatchDownloadAsync(extractedDir, selectedJobId);

    Console.WriteLine($"  Result: {extractedPaths.Count} file(s) extracted");
    foreach (var path in extractedPaths)
    {
        var info = new FileInfo(path);
        Console.WriteLine($"    - {info.Name} ({FormatBytes((ulong)info.Length)})");
    }
    Console.WriteLine();

    // -------------------------------------------------------------------------
    // Method B: Download with keepZip=true (creates zip archive)
    // -------------------------------------------------------------------------

    Console.WriteLine("Method B: BatchDownloadAsync with keepZip=true (creates zip)");
    Console.WriteLine($"  Output: {zipDir}");
    Console.WriteLine("  Downloading...");

    var zipPaths = await client.BatchDownloadAsync(zipDir, selectedJobId, keepZip: true);

    Console.WriteLine($"  Result: {zipPaths.Count} file(s)");
    foreach (var path in zipPaths)
    {
        var info = new FileInfo(path);
        Console.WriteLine($"    - {info.Name} ({FormatBytes((ulong)info.Length)})");
    }
    Console.WriteLine();

    // -------------------------------------------------------------------------
    // Summary
    // -------------------------------------------------------------------------

    Console.WriteLine("=== Summary ===");
    Console.WriteLine();
    Console.WriteLine("Method A (standard):");
    Console.WriteLine($"  - Downloads and extracts all files");
    Console.WriteLine($"  - Returns {extractedPaths.Count} individual file path(s)");
    Console.WriteLine($"  - Best for: Immediate processing of data files");
    Console.WriteLine();
    Console.WriteLine("Method B (keepZip=true):");
    Console.WriteLine($"  - Downloads files and packages them into a zip archive");
    Console.WriteLine($"  - Returns 1 path to the zip file");
    Console.WriteLine($"  - Best for: Archival, transfer to other systems, or deferred processing");
    Console.WriteLine();

    // -------------------------------------------------------------------------
    // Code Examples
    // -------------------------------------------------------------------------

    Console.WriteLine("=== Code Examples ===");
    Console.WriteLine();
    Console.WriteLine("// Standard download (extracts files):");
    Console.WriteLine("var files = await client.BatchDownloadAsync(outputDir, jobId);");
    Console.WriteLine("foreach (var file in files) { /* process each file */ }");
    Console.WriteLine();
    Console.WriteLine("// Keep as zip archive:");
    Console.WriteLine("var paths = await client.BatchDownloadAsync(outputDir, jobId, keepZip: true);");
    Console.WriteLine("var zipPath = paths[0];  // Single zip file path");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Output Locations ===");
Console.WriteLine($"Extracted files: {extractedDir}");
Console.WriteLine($"Zip file:        {zipDir}");
Console.WriteLine();
Console.WriteLine("=== Example Complete ===");

static string FormatBytes(ulong bytes)
{
    string[] sizes = ["B", "KB", "MB", "GB", "TB"];
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len /= 1024;
    }
    return $"{len:0.##} {sizes[order]}";
}
