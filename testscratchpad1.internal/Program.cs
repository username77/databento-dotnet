using System.Text.Json;
using Databento.Client.Models.Reference;

// Reproduce SecurityMaster.GetLastAsync JSON deserialization errors
//
// BUG 1: vote_per_sec
//   - API returns: Number or null (can be 1.0)
//   - Model has: int?
//   - Error: "The JSON value could not be converted to System.Nullable`1[System.Int32]"
//
// BUG 2: shares_outstanding
//   - API returns: String or null (e.g., "15204137000")
//   - Model has: long?
//   - Error: "Cannot get the value of a token type 'String' as a number"

// Simulates realistic response from:
// curl -X POST 'https://hist.databento.com/v0/security_master.get_last' \
//   -u $DATABENTO_API_KEY: \
//   -d symbols=AAPL \
//   -d countries=US
var simulatedApiResponse = """
{
    "ts_record": "2025-01-15T12:00:00Z",
    "ts_effective": "2025-01-15T00:00:00Z",
    "listing_id": "1000001",
    "listing_group_id": "100001",
    "security_id": "50001",
    "issuer_id": "10001",
    "listing_status": "A",
    "listing_source": "M",
    "listing_created_date": "1980-12-12",
    "listing_date": "1980-12-12",
    "delisting_date": null,
    "issuer_name": "Apple Inc.",
    "security_type": "EQS",
    "security_description": "Ordinary Shares",
    "primary_exchange": "XNAS",
    "exchange": "XNAS",
    "operating_mic": "XNAS",
    "symbol": "AAPL",
    "nasdaq_symbol": "AAPL",
    "local_code": "AAPL",
    "isin": "US0378331005",
    "us_code": "037833100",
    "bbg_comp_id": "BBG000B9XRY4",
    "bbg_comp_ticker": "AAPL US",
    "figi": "BBG000B9Y5X2",
    "figi_ticker": "AAPL UW",
    "fisn": "APPLE INC/SH",
    "lei": "HWUPKR0MPOU8FGXBT394",
    "sic": "3571",
    "cik": "0000320193",
    "gics": "45202030",
    "naics": "334111",
    "cic": null,
    "cfi": "ESVUFR",
    "incorporation_country": "US",
    "listing_country": "US",
    "register_country": "US",
    "trading_currency": "USD",
    "multi_currency": false,
    "segment_mic_name": "NASDAQ/NGS (GLOBAL SELECT MARKET)",
    "segment_mic": "XNGS",
    "structure": "ORD",
    "lot_size": 1,
    "par_value": 0.00001,
    "par_value_currency": "USD",
    "voting": "V",
    "vote_per_sec": 1.0,
    "shares_outstanding": "15204137000",
    "shares_outstanding_date": "2024-10-31",
    "ts_created": "2025-01-15T08:00:00Z"
}
""";

Console.WriteLine("Attempting to deserialize realistic security_master.get_last response for AAPL...\n");
Console.WriteLine($"JSON payload:\n{simulatedApiResponse}\n");

try
{
    var record = JsonSerializer.Deserialize<SecurityMasterRecord>(simulatedApiResponse);
    Console.WriteLine($"SUCCESS: Deserialized record for {record?.Symbol}");
    Console.WriteLine($"  IssuerName: {record?.IssuerName}");
    Console.WriteLine($"  SecurityType: {record?.SecurityType}");
    Console.WriteLine($"  ISIN: {record?.Isin}");
    Console.WriteLine($"  VotePerSec: {record?.VotePerSec}");
}
catch (JsonException ex)
{
    Console.WriteLine("FAILED: JsonException thrown");
    Console.WriteLine($"  Message: {ex.Message}");
    Console.WriteLine($"  Path: {ex.Path}");
    Console.WriteLine($"  LineNumber: {ex.LineNumber}");
    throw; // Re-throw to show full stack trace
}
