# Archives

This folder contains downloaded historical data files (DBN format) for backtesting.

**This folder is git-ignored** - files here are not committed to the repository.

## Usage

Download data using `HistoricalClient.GetRangeToFileAsync()`:

```csharp
var archivesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "archives"));
var outputPath = Path.Combine(archivesDir, "my_data.dbn");

await historicalClient.GetRangeToFileAsync(outputPath, "EQUS.MINI", Schema.Trades, symbols, start, end);
```

Then use with `BacktestingClientBuilder`:

```csharp
await using var client = new BacktestingClientBuilder()
    .WithFileSource(outputPath)
    .Build();
```
