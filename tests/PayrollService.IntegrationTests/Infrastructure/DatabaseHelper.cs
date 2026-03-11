using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
using System.Text.Json;

namespace PayrollService.IntegrationTests.Infrastructure;

public class DatabaseHelper : IDisposable
{
    private readonly IMongoDatabase _transferDb;
    private readonly string _mysqlConnectionString;

    public DatabaseHelper()
    {
        var client = new MongoClient(ServiceEndpoints.MongoConnectionString);
        _transferDb = client.GetDatabase(ServiceEndpoints.MongoTransferDatabaseName);
        _mysqlConnectionString = ServiceEndpoints.MySqlConnectionString;
    }

    // MongoDB: Dapr transfer state store
    public async Task<JsonDocument?> GetDaprTransferStateAsync(Guid transferId)
    {
        var collection = _transferDb.GetCollection<BsonDocument>("dapr_transfer_state");
        var filter = Builders<BsonDocument>.Filter.Regex("_id", $".*transfer-{transferId}$");
        var doc = await collection.Find(filter).FirstOrDefaultAsync();

        if (doc == null) return null;

        var value = doc.GetValue("value", BsonNull.Value);
        if (value.IsBsonNull) return null;

        // Value can be a BsonDocument (object) or a string
        if (value.IsBsonDocument)
            return JsonDocument.Parse(value.AsBsonDocument.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson }));

        return JsonDocument.Parse(value.AsString);
    }

    // MongoDB: Clean transfers for a specific employee
    public async Task CleanTransfersAsync(Guid? employeeId = null)
    {
        var transferState = _transferDb.GetCollection<BsonDocument>("dapr_transfer_state");
        var transfers = _transferDb.GetCollection<BsonDocument>("transfers");

        if (employeeId == null)
        {
            await transferState.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await transfers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }
        else
        {
            // Dapr state keys contain the transfer ID, not employee ID directly.
            // For targeted cleanup, we'd need to find transfers by employee first.
            // For simplicity, clean all transfer state.
            await transferState.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await transfers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }
    }

    // MySQL: Query transfer records from ListenerApi
    public async Task<List<MySqlTransferRecord>> GetMySqlTransfersAsync(Guid employeeId)
    {
        var records = new List<MySqlTransferRecord>();
        await using var conn = new MySqlConnection(_mysqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EmployeeId, Amount, Status, ExternalReferenceId, CurrentBalance, FailureReason FROM TransferRecords WHERE EmployeeId = @eid ORDER BY UpdatedAt";
        cmd.Parameters.AddWithValue("@eid", employeeId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new MySqlTransferRecord
            {
                Id = reader.GetGuid(0),
                EmployeeId = reader.GetGuid(1),
                Amount = reader.GetDecimal(2),
                Status = reader.GetString(3),
                ExternalReferenceId = reader.IsDBNull(4) ? null : reader.GetString(4),
                CurrentBalance = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                FailureReason = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return records;
    }

    // MySQL: Clean transfer records for a specific employee
    public async Task CleanMySqlTransfersAsync(Guid? employeeId = null)
    {
        await using var conn = new MySqlConnection(_mysqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        if (employeeId == null)
            cmd.CommandText = "DELETE FROM TransferRecords";
        else
        {
            cmd.CommandText = "DELETE FROM TransferRecords WHERE EmployeeId = @eid";
            cmd.Parameters.AddWithValue("@eid", employeeId.ToString());
        }

        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() { }
}

public class MySqlTransferRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ExternalReferenceId { get; set; }
    public decimal? CurrentBalance { get; set; }
    public string? FailureReason { get; set; }
}
