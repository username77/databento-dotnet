# Architecture 2: Data Source Abstraction - Implementation Plan

> **Status**: Draft for Review
> **Version**: 1.1
> **Target**: Databento.Client v5.0
> **Breaking Changes**: None - All changes are additive

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Module Overview](#3-module-overview)
4. [Phase 1: Core Abstractions](#4-phase-1-core-abstractions)
5. [Phase 2: Extract LiveDataSource](#5-phase-2-extract-livedatasource)
6. [Phase 3: HistoricalDataSource](#6-phase-3-historicaldatasource)
7. [Phase 4: Caching Layer](#7-phase-4-caching-layer)
8. [Phase 5: Playback Control](#8-phase-5-playback-control)
9. [Phase 6: Builder Integration](#9-phase-6-builder-integration)
10. [Phase 7: FileDataSource](#10-phase-7-filedatasource)
11. [Testing Strategy](#11-testing-strategy)
12. [Migration Guide](#12-migration-guide)
13. [File Structure](#13-file-structure)
14. [Risk Assessment](#14-risk-assessment)
15. [Open Questions](#15-open-questions)

---

## 1. Executive Summary

### What We're Building

A data source abstraction layer that allows `LiveClient` to receive records from multiple sources (live gateway, historical API, files) without code changes. This enables:

- **Backtesting**: Run trading strategies against historical data
- **Caching**: Avoid repeated API costs for the same queries
- **Playback Control**: Pause, resume, seek through historical data
- **Code Parity**: Identical user code for live and backtest modes

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Single client class | Keep `LiveClient` as sole implementation | Code identity, simpler API |
| Abstraction point | `IDataSource` interface | Clean separation, testable |
| Playback exposure | `IPlaybackControllable` interface | Type-safe, explicit, discoverable |
| Caching strategy | Optional, pluggable | Not everyone needs it |
| Symbol mapping | Synthetic `SymbolMappingMessage` in historical/file | Behavior parity with live |
| File source | Included (Phase 7) | Offline testing, CI/CD, cost savings |
| Breaking changes | **None** - all changes additive | Preserve backward compatibility |

### Success Criteria

- [ ] Existing `LiveClient` tests pass without modification
- [ ] User code works identically for live and backtest
- [ ] Live mode has no measurable performance regression
- [ ] Backtest can replay cached data without API calls
- [ ] Pause/resume works for backtest mode

---

## 2. Goals and Non-Goals

### Goals

1. **Code Identity**: User writes strategy once, runs in both modes
2. **Zero Live Overhead**: Abstraction must not slow down live streaming
3. **Cost Efficiency**: Cache historical data to avoid repeated API charges
4. **Developer Experience**: Simple builder API for mode switching
5. **Backward Compatibility**: Existing code continues to work unchanged
6. **Testability**: Easy to mock data sources for unit testing

### Non-Goals

1. **Multi-source streaming**: Not combining live + historical in same session
2. **Distributed caching**: No Redis/shared cache support (local only)
3. **Record modification**: Sources don't transform records, only provide them
4. **Time synchronization**: No clock sync between backtest and external systems
5. **LiveBlockingClient refactor**: Focus on `LiveClient` first (blocking client follows same pattern)

---

## 3. Module Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                           Module Dependency Graph                    │
└─────────────────────────────────────────────────────────────────────┘

                    ┌──────────────────────┐
                    │   LiveClientBuilder  │
                    │      (Phase 6)       │
                    └──────────┬───────────┘
                               │ uses
                               ▼
                    ┌──────────────────────┐
                    │     LiveClient       │
                    │   (Phase 2 refactor) │
                    └──────────┬───────────┘
                               │ depends on
                               ▼
                    ┌──────────────────────┐
                    │     IDataSource      │
                    │      (Phase 1)       │
                    └──────────┬───────────┘
                               │ implemented by
          ┌────────────────────┼────────────────────┐
          ▼                    ▼                    ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│  LiveDataSource  │ │HistoricalData    │ │  FileDataSource  │
│    (Phase 2)     │ │   Source         │ │    (Phase 7)     │
│                  │ │   (Phase 3)      │ │                  │
└──────────────────┘ └────────┬─────────┘ └──────────────────┘
                              │ optionally uses
                    ┌─────────┴─────────┐
                    ▼                   ▼
          ┌──────────────────┐ ┌──────────────────┐
          │   IRecordCache   │ │PlaybackController│
          │    (Phase 4)     │ │    (Phase 5)     │
          └────────┬─────────┘ └──────────────────┘
                   │ implemented by
          ┌────────┴────────┐
          ▼                 ▼
┌──────────────────┐ ┌──────────────────┐
│ DiskRecordCache  │ │MemoryRecordCache │
│    (Phase 4)     │ │    (Phase 4)     │
└──────────────────┘ └──────────────────┘
```

### Module Descriptions

| Module | Purpose | Dependencies |
|--------|---------|--------------|
| **IDataSource** | Core abstraction for record sources | None (leaf) |
| **LiveDataSource** | Wraps native gateway connection | IDataSource |
| **HistoricalDataSource** | Fetches from Historical API | IDataSource, IHistoricalClient, IRecordCache?, PlaybackController |
| **IRecordCache** | Abstraction for caching records | None (leaf) |
| **DiskRecordCache** | Persists to DBN files | IRecordCache, DbnFileReader/Writer |
| **MemoryRecordCache** | In-memory record storage | IRecordCache |
| **PlaybackController** | Pause/resume/seek state machine | None (leaf) |
| **FileDataSource** | Reads from DBN files directly | IDataSource |
| **LiveClient** | Refactored to use IDataSource | IDataSource |
| **LiveClientBuilder** | Extended with backtest options | All sources, caches |

---

## 4. Phase 1: Core Abstractions

### Objective
Define the interfaces that all subsequent phases implement.

### Deliverables

#### 4.1 IDataSource Interface

**Location**: `src/Databento.Client/DataSources/IDataSource.cs`

**Responsibilities**:
- Accept subscription configurations
- Connect and return metadata
- Stream records as `IAsyncEnumerable<Record>`
- Report capabilities
- Handle disconnection

**Key Methods**:

| Method | Purpose |
|--------|---------|
| `AddSubscription(LiveSubscription)` | Queue a subscription before connect |
| `ConnectAsync()` | Establish connection, return `DbnMetadata` |
| `StreamAsync()` | Yield records (must include `SymbolMappingMessage` for parity) |
| `DisconnectAsync()` | Clean shutdown |
| `ReconnectAsync()` | Re-establish connection (if supported) |

**Key Properties**:

| Property | Type | Purpose |
|----------|------|---------|
| `Capabilities` | `DataSourceCapabilities` | What this source supports |
| `State` | `ConnectionState` | Current connection state |

**Events**:

| Event | Purpose |
|-------|---------|
| `ErrorOccurred` | Report errors to client |

#### 4.2 DataSourceCapabilities Record

**Location**: `src/Databento.Client/DataSources/DataSourceCapabilities.cs`

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `SupportsReconnect` | `bool` | Can recover from disconnection |
| `SupportsSnapshot` | `bool` | Can request MBO snapshot |
| `SupportsReplay` | `bool` | Can replay from start |
| `IsRealTime` | `bool` | Data arrives in real-time |
| `SupportsPlaybackSpeed` | `bool` | Can control pacing |
| `SupportsPauseResume` | `bool` | Can pause/resume streaming |

#### 4.3 LiveSubscription Record

**Location**: `src/Databento.Client/DataSources/LiveSubscription.cs`

**Note**: This may already exist in current codebase. Verify and reuse or extend.

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Dataset` | `string` | Dataset name |
| `Schema` | `Schema` | Schema type |
| `Symbols` | `IReadOnlyList<string>` | Symbol list |
| `StartTime` | `DateTimeOffset?` | For intraday replay |
| `UseSnapshot` | `bool` | Request snapshot |

#### 4.4 DataSourceErrorEventArgs

**Location**: `src/Databento.Client/DataSources/DataSourceErrorEventArgs.cs`

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Exception` | `Exception` | The error |
| `IsRecoverable` | `bool` | Hint for retry logic |

### Acceptance Criteria

- [ ] Interfaces compile with no implementation
- [ ] XML documentation complete
- [ ] No dependencies on concrete classes

### Estimated Scope
- **Files**: 4 new files
- **Risk**: Low (no behavior changes)

---

## 5. Phase 2: Extract LiveDataSource

### Objective
Extract the current `LiveClient` gateway logic into `LiveDataSource` without changing external behavior.

### Approach

1. **Identify extraction boundary**: All native interop and gateway communication
2. **Create LiveDataSource**: Move gateway logic into new class implementing `IDataSource`
3. **Refactor LiveClient**: Delegate to `IDataSource` instead of direct native calls
4. **Preserve API**: All public `LiveClient` methods unchanged
5. **Inject default**: Builder creates `LiveDataSource` by default

### Deliverables

#### 5.1 LiveDataSource Class

**Location**: `src/Databento.Client/DataSources/LiveDataSource.cs`

**Constructor Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `apiKey` | `string` | Databento API key |
| `gateway` | `string?` | Optional gateway override |
| `sendTsOut` | `bool` | Include ts_out in records |
| `upgradePolicy` | `VersionUpgradePolicy` | DBN version handling |

**Internal Components** (moved from LiveClient):
- Native handle management
- Gateway authentication
- Subscription wire protocol
- Record streaming from native
- Heartbeat handling

**Capabilities**:
```
SupportsReconnect = true
SupportsSnapshot = true
SupportsReplay = true (intraday)
IsRealTime = true
SupportsPlaybackSpeed = false
SupportsPauseResume = false
```

#### 5.2 LiveClient Refactor

**Changes**:

| Before | After |
|--------|-------|
| Direct native interop calls | Delegate to `_dataSource` field |
| Creates native handle in constructor | Receives `IDataSource` in constructor |
| Manages connection internally | Calls `_dataSource.ConnectAsync()` |

**New Constructor** (internal):
```
LiveClient(IDataSource dataSource, ILogger?, ResilienceOptions)
```

**Preserved Behavior**:
- All `ILiveClient` methods work identically
- Events fire at same times
- Error handling unchanged
- Resilience (auto-reconnect) still works

#### 5.3 LiveClientBuilder Changes

**New Internal Behavior**:
- `Build()` creates `LiveDataSource` with configured parameters
- Passes `LiveDataSource` to `LiveClient` constructor

**No Public API Changes** in this phase.

### Migration Safety

| Risk | Mitigation |
|------|------------|
| Breaking native interop | Comprehensive integration tests |
| Changing timing behavior | Benchmark before/after |
| Event ordering changes | Test event sequences |

### Acceptance Criteria

- [ ] All existing `LiveClient` unit tests pass
- [ ] All existing integration tests pass
- [ ] No public API changes
- [ ] Performance benchmark shows no regression

### Estimated Scope
- **Files**: 2 new, 2 modified
- **Risk**: Medium (refactoring working code)

---

## 6. Phase 3: HistoricalDataSource

### Objective
Create a data source that streams historical data matching live behavior.

### Key Challenge: Symbol Mapping Parity

**Problem**:
- Live gateway sends `SymbolMappingMessage` automatically
- Historical API requires separate `SymbologyResolveAsync()` call
- User code expects `SymbolMappingMessage` in both modes

**Solution**:
1. Call `SymbologyResolveAsync()` during `ConnectAsync()`
2. Build instrument ID → symbol mapping
3. Emit synthetic `SymbolMappingMessage` records at start of `StreamAsync()`
4. Then emit historical records

### Deliverables

#### 6.1 HistoricalDataSource Class

**Location**: `src/Databento.Client/DataSources/HistoricalDataSource.cs`

**Constructor Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `apiKey` | `string` | Databento API key |
| `startTime` | `DateTimeOffset` | Backtest start |
| `endTime` | `DateTimeOffset` | Backtest end |
| `playbackSpeed` | `PlaybackSpeed?` | Pacing control |
| `cache` | `IRecordCache?` | Optional cache |

**Key Internal State**:

| Field | Type | Purpose |
|-------|------|---------|
| `_historicalClient` | `IHistoricalClient` | For API calls |
| `_subscriptions` | `List<LiveSubscription>` | Queued subscriptions |
| `_symbolMap` | `Dictionary<uint, string>` | Resolved symbols |
| `_cache` | `IRecordCache?` | Optional cache |
| `_playback` | `PlaybackController` | Pause/resume state |

**Capabilities**:
```
SupportsReconnect = (cache != null)
SupportsSnapshot = false
SupportsReplay = true
IsRealTime = false
SupportsPlaybackSpeed = true
SupportsPauseResume = true
```

#### 6.2 ConnectAsync Implementation

**Sequence**:
1. Create `HistoricalClient` internally
2. For each subscription, call `SymbologyResolveAsync()`
3. Build `_symbolMap` from resolution results
4. Check if cache exists and is valid
5. Return synthetic `DbnMetadata`

#### 6.3 StreamAsync Implementation

**Sequence**:
1. Yield synthetic `SymbolMappingMessage` for each entry in `_symbolMap`
2. Determine record source:
   - If cached: read from cache
   - If not cached but cache configured: fetch, write-through, yield
   - If no cache: fetch and yield directly
3. For each record:
   - Check `PlaybackController` for pause
   - Apply playback speed delay (if not Maximum)
   - Update position tracking
   - Yield record

#### 6.4 Synthetic SymbolMappingMessage Creation

**Factory Method**: `CreateSymbolMappingMessage(uint instrumentId, string symbol)`

**Field Population**:

| Field | Value |
|-------|-------|
| `InstrumentId` | From symbology resolution |
| `STypeInSymbol` | Original subscription symbol |
| `STypeOutSymbol` | Resolved symbol (same for raw) |
| `StartTs` | `_startTime` as nanos |
| `EndTs` | `_endTime` as nanos |

#### 6.5 PlaybackSpeed Struct

**Location**: `src/Databento.Client/DataSources/PlaybackSpeed.cs`

**Static Factories**:

| Factory | Multiplier | Description |
|---------|------------|-------------|
| `Maximum` | `∞` | No delays, as fast as possible |
| `RealTime` | `1.0` | Match original timestamps |
| `Times(n)` | `n` | n× speed |

**Delay Calculation**:
```
delay = (currentRecordTs - previousRecordTs) / multiplier
```

### Acceptance Criteria

- [ ] Streams records in timestamp order
- [ ] Emits `SymbolMappingMessage` before data records
- [ ] Symbol mapping matches live behavior
- [ ] Playback speed correctly paces records
- [ ] Works without cache (direct streaming)

### Estimated Scope
- **Files**: 2 new files
- **Risk**: Medium (new functionality)

---

## 7. Phase 4: Caching Layer

### Objective
Enable caching of historical data for repeated replay without API costs.

### Deliverables

#### 7.1 IRecordCache Interface

**Location**: `src/Databento.Client/DataSources/Caching/IRecordCache.cs`

**Methods**:

| Method | Purpose |
|--------|---------|
| `ExistsAsync()` | Check if cache has data |
| `WriteAsync(IAsyncEnumerable<Record>)` | Populate cache |
| `ReadAsync()` | Read all records |
| `ReadFromIndexAsync(long)` | Read from position (for resume) |
| `InvalidateAsync()` | Delete cache |

**Properties**:

| Property | Type | Purpose |
|----------|------|---------|
| `CacheKey` | `string` | Unique identifier |
| `RecordCount` | `long?` | Total records (if known) |

#### 7.2 DiskRecordCache Class

**Location**: `src/Databento.Client/DataSources/Caching/DiskRecordCache.cs`

**Storage Format**: Standard DBN files

**Cache Key Generation**:
```
SHA256(dataset + schema + sorted(symbols) + start + end)[0:16]
```

**File Location**:
```
{cacheDirectory}/{cacheKey}.dbn
```

**Default Cache Directory**:
- Windows: `%LOCALAPPDATA%\Databento\Cache`
- Linux/macOS: `~/.local/share/Databento/Cache`

**Write Strategy**:
1. Write to `{key}.dbn.tmp`
2. Atomic rename to `{key}.dbn` on completion
3. Delete temp file on failure

**Read Strategy**:
1. Open with `DbnFileReader`
2. Yield records via `ReadRecordsAsync()`
3. For indexed read, skip records until index reached

#### 7.3 MemoryRecordCache Class

**Location**: `src/Databento.Client/DataSources/Caching/MemoryRecordCache.cs`

**Storage**: `List<Record>` in memory

**Use Cases**:
- Small datasets
- Fast iteration during development
- When disk I/O is bottleneck

**Limitations**:
- Lost on process exit
- Limited by available RAM
- No persistence

#### 7.4 CachePolicy Enum

**Location**: `src/Databento.Client/DataSources/Caching/CachePolicy.cs`

**Values**:

| Value | Description |
|-------|-------------|
| `None` | No caching, always fetch from API |
| `Memory` | Cache in RAM |
| `Disk` | Cache to DBN files |

### Cache Invalidation

**Manual**: Call `cache.InvalidateAsync()`

**Automatic** (future consideration):
- TTL-based expiration
- Size-based eviction
- Version-based invalidation

### Acceptance Criteria

- [ ] Disk cache survives process restart
- [ ] Memory cache allows re-iteration
- [ ] Cache key uniquely identifies query
- [ ] Atomic writes prevent corruption
- [ ] `ReadFromIndexAsync` works for resume

### Estimated Scope
- **Files**: 4 new files
- **Risk**: Low (isolated module)

---

## 8. Phase 5: Playback Control

### Objective
Enable pause, resume, seek, and reset operations during backtesting.

### Deliverables

#### 8.1 PlaybackController Class

**Location**: `src/Databento.Client/DataSources/PlaybackController.cs`

**State**:

| Field | Type | Description |
|-------|------|-------------|
| `_isPaused` | `volatile bool` | Currently paused |
| `_isStopped` | `volatile bool` | Stopped completely |
| `_currentIndex` | `long` | Current record position |
| `_currentTimestamp` | `DateTimeOffset?` | Current record time |
| `_pauseSemaphore` | `SemaphoreSlim` | Blocking mechanism |

**Public Properties**:

| Property | Type | Description |
|----------|------|-------------|
| `IsPaused` | `bool` | Pause state |
| `IsStopped` | `bool` | Stop state |
| `CurrentIndex` | `long` | Position (0-based) |
| `CurrentTimestamp` | `DateTimeOffset?` | Last record time |

**Public Methods**:

| Method | Description |
|--------|-------------|
| `Pause()` | Pause playback |
| `Resume()` | Resume playback |
| `Stop()` | Stop completely |
| `SeekToIndex(long)` | Jump to position |
| `Reset()` | Return to beginning |

**Events**:

| Event | Fired When |
|-------|------------|
| `Paused` | Playback paused |
| `Resumed` | Playback resumed |
| `PositionChanged` | Record position updated |

**Internal Methods** (called by HistoricalDataSource):

| Method | Description |
|--------|-------------|
| `WaitIfPausedAsync(ct)` | Block if paused, return false if stopped |
| `UpdatePosition(index, timestamp)` | Track current position |

#### 8.2 Integration with HistoricalDataSource

**StreamAsync Loop**:
```
for each record:
    if not await _playback.WaitIfPausedAsync(ct):
        yield break

    apply playback speed delay
    _playback.UpdatePosition(index, timestamp)
    yield record
```

#### 8.3 Exposing PlaybackController to Users

**Decision**: Use dedicated interface (Option C) - explicit, type-safe, no casting required.

```csharp
// User code - clean pattern matching
if (client is IPlaybackControllable controllable)
{
    controllable.Playback.Pause();
    Console.WriteLine($"Paused at index {controllable.Playback.CurrentIndex}");

    // Later...
    controllable.Playback.Resume();
}

// Or with null-conditional
(client as IPlaybackControllable)?.Playback.Pause();
```

#### 8.4 IPlaybackControllable Interface

**Location**: `src/Databento.Client/Live/IPlaybackControllable.cs`

**Definition**:

```csharp
/// <summary>
/// Interface for clients that support playback control (pause, resume, seek).
/// Implemented by LiveClient when using a data source that supports playback.
/// </summary>
public interface IPlaybackControllable
{
    /// <summary>
    /// The playback controller for pause/resume/seek operations.
    /// </summary>
    PlaybackController Playback { get; }
}
```

**Implementation in LiveClient**:

```csharp
public sealed class LiveClient : ILiveClient, IPlaybackControllable
{
    private readonly IDataSource _dataSource;
    private readonly PlaybackController? _playbackController;

    // IPlaybackControllable implementation
    public PlaybackController Playback =>
        _playbackController ?? throw new NotSupportedException(
            "Playback control is only available in backtesting mode. " +
            "Use WithBacktesting() or WithFileSource() in the builder.");

    internal LiveClient(IDataSource dataSource, ...)
    {
        _dataSource = dataSource;

        // Only expose playback if data source supports it
        if (dataSource.Capabilities.SupportsPauseResume)
        {
            _playbackController = dataSource switch
            {
                HistoricalDataSource hds => hds.Playback,
                FileDataSource fds => fds.Playback,
                _ => null
            };
        }
    }
}
```

**Usage Patterns**:

| Pattern | Use Case |
|---------|----------|
| `client is IPlaybackControllable` | Check if playback is available |
| `(client as IPlaybackControllable)?.Playback` | Safe access with null propagation |
| `((IPlaybackControllable)client).Playback` | Direct cast when you know it's backtest |

**Why This Approach**:

| Benefit | Description |
|---------|-------------|
| Type-safe | Compiler catches misuse |
| Discoverable | IntelliSense shows interface |
| Non-breaking | Existing code unaffected |
| Explicit | User must acknowledge playback mode |

### Acceptance Criteria

- [ ] Pause blocks `StreamAsync` enumeration
- [ ] Resume continues from paused position
- [ ] Seek jumps to correct record
- [ ] Reset allows full replay
- [ ] Position tracking is accurate
- [ ] Thread-safe operations

### Estimated Scope
- **Files**: 2 new files, 1 modified
- **Risk**: Medium (concurrency)

---

## 9. Phase 6: Builder Integration

### Objective
Extend `LiveClientBuilder` with backtesting configuration options.

### Deliverables

#### 9.1 New Builder Methods

| Method | Description |
|--------|-------------|
| `WithBacktesting(start, end)` | Enable backtest mode |
| `WithDiskCache(directory?)` | Enable disk caching |
| `WithMemoryCache()` | Enable memory caching |
| `WithPlaybackSpeed(speed)` | Set playback speed |
| `WithDataSource(source)` | Inject custom source (advanced) |

#### 9.2 Builder State

**New Fields**:

| Field | Type | Default |
|-------|------|---------|
| `_backtestStart` | `DateTimeOffset?` | `null` |
| `_backtestEnd` | `DateTimeOffset?` | `null` |
| `_cachePolicy` | `CachePolicy` | `None` |
| `_cacheDirectory` | `string?` | Platform default |
| `_playbackSpeed` | `PlaybackSpeed?` | `Maximum` |
| `_customDataSource` | `IDataSource?` | `null` |

#### 9.3 Build() Logic

**Decision Tree**:
```
if _customDataSource is set:
    use _customDataSource
else if _backtestStart and _backtestEnd are set:
    create HistoricalDataSource with:
        - time range
        - cache (if configured)
        - playback speed
else:
    create LiveDataSource (current behavior)

pass data source to LiveClient constructor
```

#### 9.4 Validation Rules

| Rule | Error |
|------|-------|
| Backtest without API key | "API key required" |
| Backtest end before start | "End time must be after start time" |
| Cache without backtest | Warning (ignored) |
| Playback speed without backtest | Warning (ignored) |

#### 9.5 LiveBlockingClientBuilder

**Same pattern**: Add identical methods to `LiveBlockingClientBuilder`

**Consideration**: Extract common builder base class or use composition

### Acceptance Criteria

- [ ] Default behavior unchanged (live mode)
- [ ] Backtest mode creates correct data source
- [ ] Cache configuration works
- [ ] Invalid configurations throw clear errors
- [ ] Builder is fluent and discoverable

### Estimated Scope
- **Files**: 2 modified
- **Risk**: Low (additive changes)

---

## 10. Phase 7: FileDataSource

### Objective
Enable streaming from local DBN files without any API calls.

### Use Cases

| Use Case | Description |
|----------|-------------|
| Offline analysis | Run backtests without internet |
| Pre-downloaded datasets | Use batch-downloaded data |
| CI/CD testing | Test without API credentials |
| Team collaboration | Share datasets via files |
| Cost optimization | Download once, replay many times |
| Reproducibility | Exact same data for every run |

### Deliverables

#### 10.1 FileDataSource Class

**Location**: `src/Databento.Client/DataSources/FileDataSource.cs`

**Constructor Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `filePath` | `string` | Path to DBN file |
| `playbackSpeed` | `PlaybackSpeed?` | Pacing control (default: Maximum) |

**Capabilities**:
```
SupportsReconnect = true    // Re-read file from start
SupportsSnapshot = false
SupportsReplay = true       // Can replay unlimited times
IsRealTime = false
SupportsPlaybackSpeed = true
SupportsPauseResume = true
```

**Key Internal State**:

| Field | Type | Purpose |
|-------|------|---------|
| `_filePath` | `string` | Path to DBN file |
| `_reader` | `DbnFileReader?` | File reader instance |
| `_metadata` | `DbnMetadata?` | Cached file metadata |
| `_playback` | `PlaybackController` | Pause/resume state |
| `_playbackSpeed` | `PlaybackSpeed` | Pacing control |

**Public Properties**:

| Property | Type | Description |
|----------|------|-------------|
| `Playback` | `PlaybackController` | For pause/resume/seek |
| `FilePath` | `string` | The source file path |

#### 10.2 ConnectAsync Implementation

**Sequence**:
1. Validate file exists
2. Open file with `DbnFileReader`
3. Read and cache metadata
4. Extract symbol mappings from metadata (if available)
5. Reset playback controller
6. Return metadata

**Error Handling**:

| Error | Exception |
|-------|-----------|
| File not found | `FileNotFoundException` with helpful message |
| Invalid DBN format | `InvalidDataException` with details |
| Corrupted file | `InvalidDataException` with details |

#### 10.3 StreamAsync Implementation

**Sequence**:
1. Emit `SymbolMappingMessage` records from metadata (if available)
2. For each record in file:
   - Check `PlaybackController.WaitIfPausedAsync()`
   - Apply playback speed delay (if not Maximum)
   - Update position tracking
   - Yield record

**Symbol Mapping Handling**:

DBN files may contain symbol mapping information in metadata. If present:
- Extract mappings from `DbnMetadata.Mappings`
- Emit synthetic `SymbolMappingMessage` at stream start

If not present:
- Log warning
- User must handle `InstrumentId` → symbol mapping externally

#### 10.4 ReconnectAsync Implementation

**Behavior**: Reset to file beginning for replay

**Sequence**:
1. Close current reader (if open)
2. Reset playback controller
3. Re-open file
4. Ready for new `StreamAsync()` call

#### 10.5 Builder Integration

**New Method on LiveClientBuilder**:

```csharp
/// <summary>
/// Stream data from a local DBN file instead of live or historical API.
/// Enables offline backtesting and replay of pre-downloaded data.
/// </summary>
/// <param name="filePath">Path to the DBN file</param>
public LiveClientBuilder WithFileSource(string filePath)
{
    _fileSourcePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    return this;
}
```

**Build() Logic Update**:

```csharp
private IDataSource CreateDataSource()
{
    // File source takes precedence
    if (!string.IsNullOrEmpty(_fileSourcePath))
    {
        return new FileDataSource(_fileSourcePath, _playbackSpeed);
    }

    // Then backtesting
    if (_backtestStart.HasValue && _backtestEnd.HasValue)
    {
        return new HistoricalDataSource(...);
    }

    // Default: live
    return new LiveDataSource(...);
}
```

**Validation**:

| Rule | Behavior |
|------|----------|
| File source + backtest | File source wins, log warning |
| File source + API key | API key ignored, no warning |
| Non-existent file | Throw at `Build()` time |

#### 10.6 Usage Examples

**Basic File Replay**:
```csharp
await using var client = new LiveClientBuilder()
    .WithFileSource("/path/to/data.dbn")
    .Build();

// No SubscribeAsync needed - file defines the data
await client.StartAsync();

await foreach (var record in client.StreamAsync())
{
    ProcessRecord(record);
}
```

**File Replay with Playback Control**:
```csharp
await using var client = new LiveClientBuilder()
    .WithFileSource("/path/to/data.dbn")
    .WithPlaybackSpeed(PlaybackSpeed.RealTime)
    .Build();

await client.StartAsync();

// Access playback controller
if (client is IPlaybackControllable controllable)
{
    // Start streaming in background
    var streamTask = Task.Run(async () =>
    {
        await foreach (var record in client.StreamAsync())
        {
            ProcessRecord(record);
        }
    });

    // Pause after 10 seconds
    await Task.Delay(10000);
    controllable.Playback.Pause();

    // Inspect state
    Console.WriteLine($"Paused at: {controllable.Playback.CurrentTimestamp}");

    // Resume
    controllable.Playback.Resume();
    await streamTask;
}
```

**Multiple Replays from Same File**:
```csharp
await using var client = new LiveClientBuilder()
    .WithFileSource("/path/to/data.dbn")
    .Build();

// First run
await client.StartAsync();
await foreach (var record in client.StreamAsync())
{
    RunStrategy1(record);
}

// Replay
await client.ReconnectAsync();
await foreach (var record in client.StreamAsync())
{
    RunStrategy2(record);
}
```

#### 10.7 Comparison: FileDataSource vs HistoricalDataSource with DiskCache

| Aspect | FileDataSource | HistoricalDataSource + DiskCache |
|--------|----------------|----------------------------------|
| **API Key** | Not required | Required |
| **Initial fetch** | None (file exists) | First run fetches from API |
| **Symbol resolution** | From file metadata | Via SymbologyResolveAsync |
| **Subscription** | Ignored (file defines data) | Required |
| **Use case** | Pre-existing files | Cache API results |

### Acceptance Criteria

- [ ] Streams records from valid DBN files
- [ ] Metadata extracted and returned correctly
- [ ] Symbol mappings emitted (if in metadata)
- [ ] Playback controls (pause/resume/seek) work
- [ ] `ReconnectAsync()` replays from beginning
- [ ] Clear errors for missing/invalid files
- [ ] Builder method validates file exists
- [ ] Works without API key

### Estimated Scope
- **Files**: 1 new, 2 modified (builder + tests)
- **Risk**: Low (leverages existing DbnFileReader)

---

## 11. Testing Strategy

### Unit Tests

#### Phase 1 Tests
- Interface contracts (compile-time)
- Capability combinations

#### Phase 2 Tests
- `LiveDataSource` isolation tests (mocked native)
- `LiveClient` with mocked `IDataSource`
- Event forwarding
- Error propagation

#### Phase 3 Tests
- Symbol resolution mock
- Synthetic `SymbolMappingMessage` generation
- Record ordering
- Playback speed delays (mocked time)

#### Phase 4 Tests
- Cache key generation determinism
- Disk cache write/read round-trip
- Memory cache behavior
- Indexed reads for resume
- Atomic write safety

#### Phase 5 Tests
- Pause/resume state machine
- Concurrent pause/resume calls
- Position tracking accuracy
- Seek bounds checking

#### Phase 6 Tests
- Builder validation
- Mode selection logic
- Default behaviors preserved

#### Phase 7 Tests
- File existence validation
- DBN file reading integration
- Metadata extraction
- Symbol mapping from file metadata
- Playback controls (shares with Phase 5)
- `ReconnectAsync()` replays from start
- Error handling for invalid/corrupted files
- Works without API key

### Integration Tests

| Test | Description |
|------|-------------|
| Live smoke test | Connect to real gateway, receive records |
| Historical smoke test | Fetch real historical data |
| File smoke test | Stream from real DBN file |
| Cache round-trip | Fetch → cache → replay |
| Mode switching | Same code, different configs |
| Symbol mapping parity | Compare live vs historical vs file mappings |
| Playback control | Pause/resume across source types |
| File replay | Multiple runs from same file |

### Performance Tests

| Test | Metric | Target |
|------|--------|--------|
| Live throughput | Records/second | No regression from baseline |
| Live latency | Event delay | No regression from baseline |
| Cache write | MB/second | > 100 MB/s |
| Cache read | Records/second | > 1M records/s |
| File read | Records/second | > 1M records/s |
| Memory usage | Peak RSS | Bounded by cache policy |
| File source memory | Peak RSS | Constant (streaming, no buffering) |

### Test Data

**Mock Data Sources**: Create `MockDataSource : IDataSource` for unit tests

**Test DBN Files**: Include small DBN files in test assets

**Recorded Sessions**: Capture live sessions for replay testing

---

## 12. Migration Guide

### For Library Maintainers

#### Breaking Changes (Internal Only)

| Change | Impact |
|--------|--------|
| `LiveClient` constructor signature | Internal only, builder unchanged |
| Native interop moved to `LiveDataSource` | Internal only |

#### Non-Breaking Changes

| Change | Impact |
|--------|--------|
| New builder methods | Additive, optional |
| New namespaces | Additive |
| New interfaces | Additive |

### For Library Users

**No action required** for existing code. All current usage patterns continue to work.

**To adopt backtesting**:

```csharp
// Before (live only)
var client = new LiveClientBuilder()
    .WithKeyFromEnv()
    .Build();

// After (backtest mode)
var client = new LiveClientBuilder()
    .WithKeyFromEnv()
    .WithBacktesting(start, end)  // Add this line
    .WithDiskCache()               // Optional
    .Build();

// Rest of code unchanged
```

---

## 13. File Structure

```
src/Databento.Client/
├── DataSources/                          # NEW FOLDER (Phase 1+)
│   ├── IDataSource.cs                    # Phase 1 - Core abstraction
│   ├── DataSourceCapabilities.cs         # Phase 1 - Capability flags
│   ├── DataSourceErrorEventArgs.cs       # Phase 1 - Error events
│   ├── LiveSubscription.cs               # Phase 1 - Subscription model
│   ├── LiveDataSource.cs                 # Phase 2 - Gateway connection
│   ├── HistoricalDataSource.cs           # Phase 3 - HTTP API source
│   ├── FileDataSource.cs                 # Phase 7 - DBN file source
│   ├── PlaybackSpeed.cs                  # Phase 3 - Speed control
│   ├── PlaybackController.cs             # Phase 5 - Pause/resume/seek
│   └── Caching/                          # Phase 4 - Cache layer
│       ├── IRecordCache.cs               # Cache abstraction
│       ├── CachePolicy.cs                # Cache policy enum
│       ├── DiskRecordCache.cs            # DBN file cache
│       └── MemoryRecordCache.cs          # In-memory cache
├── Live/
│   ├── ILiveClient.cs                    # Unchanged
│   ├── ILiveBlockingClient.cs            # Unchanged
│   ├── IPlaybackControllable.cs          # Phase 5 (NEW) - Playback interface
│   ├── LiveClient.cs                     # Phase 2 (MODIFIED) - Uses IDataSource
│   └── LiveBlockingClient.cs             # Future (MODIFIED)
├── Builders/
│   ├── LiveClientBuilder.cs              # Phase 6 (MODIFIED) - Backtest options
│   └── LiveBlockingClientBuilder.cs      # Phase 6 (MODIFIED) - Backtest options
└── ... (unchanged)

tests/Databento.Client.Tests/
├── DataSources/                          # NEW FOLDER
│   ├── LiveDataSourceTests.cs            # Phase 2
│   ├── HistoricalDataSourceTests.cs      # Phase 3
│   ├── FileDataSourceTests.cs            # Phase 7
│   ├── PlaybackControllerTests.cs        # Phase 5
│   └── Caching/
│       ├── DiskRecordCacheTests.cs       # Phase 4
│       └── MemoryRecordCacheTests.cs     # Phase 4
├── Live/
│   └── PlaybackControllableTests.cs      # Phase 5 (NEW)
├── Builders/
│   └── LiveClientBuilderBacktestTests.cs # Phase 6 (NEW)
└── ... (existing tests unchanged)
```

### New Files Summary

| Phase | New Files | Modified Files |
|-------|-----------|----------------|
| 1 | 4 | 0 |
| 2 | 1 | 1 |
| 3 | 2 | 0 |
| 4 | 4 | 0 |
| 5 | 2 | 1 |
| 6 | 0 | 2 |
| 7 | 1 | 1 |
| **Total** | **14** | **5** |

---

## 14. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Performance regression in live mode | Low | High | Benchmark before/after Phase 2 |
| Breaking existing tests | Medium | Medium | Run full test suite each phase |
| Symbol mapping mismatch | Medium | High | Compare live vs historical in integration tests |
| Cache corruption | Low | Medium | Atomic writes, validation on read |
| Thread safety issues in PlaybackController | Medium | Medium | Thorough concurrency testing |
| Native interop breakage during extraction | Medium | High | Incremental extraction, integration tests |

---

## 15. Open Questions

### Resolved Decisions

| Question | Decision | Rationale |
|----------|----------|-----------|
| How to expose PlaybackController? | **Option C: `IPlaybackControllable` interface** | Type-safe, explicit, discoverable |
| Include FileDataSource? | **Yes, Phase 7 is required** | Enables offline testing, CI/CD, cost optimization |
| Breaking changes? | **None** | All changes are additive |

### Design Questions (Still Open)

1. **Should `IDataSource` be public API?**
   - Pro: Enables custom sources (e.g., mock for testing)
   - Con: Larger API surface to maintain
   - **Recommendation**: Start internal, expose if requested

2. **Should cache be shared across client instances?**
   - Current design: One cache per data source
   - Alternative: Shared cache manager singleton
   - **Recommendation**: Start simple, add sharing later if needed

3. **Support for `LiveBlockingClient`?**
   - Same pattern applies (IDataSource abstraction)
   - Implement in parallel or after `LiveClient`?
   - **Recommendation**: After `LiveClient`, same phases

### Implementation Questions (Still Open)

4. **Where does SymbolMappingMessage struct live?**
   - Need to verify it can be constructed programmatically
   - May need factory method or internal constructor access
   - **Action**: Investigate during Phase 3

5. **How to handle multi-subscription scenarios?**
   - Multiple `SubscribeAsync` calls before `StartAsync`
   - Historical: Multiple `GetRangeAsync` calls? Single combined call?
   - **Action**: Verify behavior matches live gateway during Phase 3

6. **Cache versioning strategy?**
   - What if DBN format version changes?
   - What if library version changes record interpretation?
   - **Recommendation**: Include library version + DBN version in cache metadata
   - **Action**: Define cache invalidation rules during Phase 4

7. **FileDataSource subscription behavior?**
   - File defines data - what if user calls `SubscribeAsync`?
   - **Recommendation**: Ignore subscriptions, log warning
   - **Action**: Document behavior clearly during Phase 7

---

## Appendix A: Phase Dependencies

```
Phase 1 (Interfaces)
    │
    ├──► Phase 2 (LiveDataSource + LiveClient refactor)
    │        │
    │        └──────────────────────────────┐
    │                                       │
    ├──► Phase 3 (HistoricalDataSource) ────┼──► Phase 6 (Builder)
    │        │                              │         │
    │        ├──► Phase 4 (Caching)         │         └──► Ready for release
    │        │                              │
    │        └──► Phase 5 (Playback) ───────┤
    │              (IPlaybackControllable)  │
    │                                       │
    └──► Phase 7 (FileDataSource) ──────────┘
              (shares PlaybackController)
```

### Dependency Details

| Phase | Depends On | Blocks |
|-------|------------|--------|
| 1 | None | 2, 3, 7 |
| 2 | 1 | 6 |
| 3 | 1 | 4, 5, 6 |
| 4 | 3 | 6 |
| 5 | 3 | 6, 7 |
| 6 | 2, 3, 4, 5, 7 | Release |
| 7 | 1, 5 | 6 |

### Implementation Order

**Recommended**: 1 → 2 → 3 → 5 → 4 → 7 → 6

**Rationale**:
- Phase 5 (PlaybackController) before Phase 4 (Caching) because caching uses position tracking
- Phase 7 before Phase 6 because builder needs to support all sources
- Phase 6 last because it integrates all components

---

## Appendix B: Effort Estimates

| Phase | Effort | Description |
|-------|--------|-------------|
| Phase 1 | Small | Interface definitions only, no implementation |
| Phase 2 | Large | Refactoring existing LiveClient, most risk |
| Phase 3 | Medium | New class, integrates with HistoricalClient |
| Phase 4 | Medium | Cache implementations, file I/O |
| Phase 5 | Small | State machine, concurrency |
| Phase 6 | Small | Builder method additions |
| Phase 7 | Small | Leverages existing DbnFileReader |

### Effort Definitions

| Size | Meaning |
|------|---------|
| Small | 1-2 files, straightforward implementation |
| Medium | 3-5 files, some complexity or integration |
| Large | Significant refactoring, high test coverage needed |

**Recommended Order**: 1 → 2 → 3 → 5 → 4 → 7 → 6

**Critical Path**: 1 → 2 → 3 → 5 → 6 (playback before caching)

---

## Appendix C: Non-Breaking Change Verification

All changes in this implementation are **additive and non-breaking**:

| Change Type | Examples | Breaking? |
|-------------|----------|-----------|
| New interfaces | `IDataSource`, `IPlaybackControllable`, `IRecordCache` | No |
| New classes | `LiveDataSource`, `HistoricalDataSource`, `FileDataSource` | No |
| New builder methods | `WithBacktesting()`, `WithFileSource()`, `WithDiskCache()` | No |
| Internal refactoring | `LiveClient` delegates to `IDataSource` | No |
| New namespaces | `Databento.Client.DataSources`, `...Caching` | No |

**Verification Checklist**:

- [ ] All existing public APIs unchanged
- [ ] All existing tests pass without modification
- [ ] Default behavior (no new builder methods) produces identical `LiveClient`
- [ ] No removed or renamed public members
- [ ] No changed method signatures on public types

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-06 | AI Assistant | Initial draft |
| 1.1 | 2025-12-06 | AI Assistant | Resolved: IPlaybackControllable (Option C), FileDataSource required, confirmed non-breaking |

---

**Next Steps**: Review this document and provide feedback on:
1. Any missing requirements
2. Remaining open questions (Section 15)
3. Implementation order preferences
4. Scope adjustments
