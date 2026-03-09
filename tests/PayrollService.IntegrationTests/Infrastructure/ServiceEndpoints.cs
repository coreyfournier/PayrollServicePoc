namespace PayrollService.IntegrationTests.Infrastructure;

public static class ServiceEndpoints
{
    public static string PayrollApi => Environment.GetEnvironmentVariable("PAYROLL_API_URL") ?? "http://localhost:5000";
    public static string ListenerApi => Environment.GetEnvironmentVariable("LISTENER_API_URL") ?? "http://localhost:5001";
    public static string MongoConnectionString => Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING") ?? "mongodb://localhost:27017/?directConnection=true";
    public static string MongoDatabaseName => "payroll_db";
    public static string MySqlConnectionString => Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING") ?? "Server=localhost;Port=3306;Database=listener_db;User=listener_user;Password=listener_password;";
    public static string KsqlDbUrl => Environment.GetEnvironmentVariable("KSQLDB_URL") ?? "http://localhost:8088";
}
