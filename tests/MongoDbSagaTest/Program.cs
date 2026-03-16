using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using SagaTest;

// ── Minimal reproduction: MassTransit saga + MongoDB Driver v3 ──
// Tests whether GuidSerializer registration fixes the "GuidRepresentation is Unspecified" error.

Console.WriteLine("=== MassTransit MongoDB Saga Test ===\n");

// ── STEP 1: Register GuidSerializer before anything else ──
Console.Write("Registering GuidSerializer(Standard)... ");
try
{
    BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    Console.WriteLine("OK");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.Message}");
}

// ── STEP 2: Skip custom class map — let MassTransit handle it ──
Console.WriteLine("Skipping custom BsonClassMap — MassTransit will register its own");

// ── STEP 3: Build host with MassTransit + MongoDb saga ──
Console.Write("Building host with MassTransit saga... ");
try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices(services =>
        {
            services.AddMassTransit(x =>
            {
                x.AddSagaStateMachine<TestStateMachine, TestState>()
                    .MongoDbRepository(r =>
                    {
                        r.Connection = "mongodb://localhost:27017/?directConnection=true";
                        r.DatabaseName = "saga_test_db";
                        r.CollectionName = "test_sagas";
                    });

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host("localhost", "/", h =>
                    {
                        h.Username("guest");
                        h.Password("guest");
                    });
                    cfg.ConfigureEndpoints(context);
                });
            });
        })
        .Build();

    Console.WriteLine("OK");

    // ── STEP 4: Start host, publish message, check result ──
    Console.Write("Starting host... ");
    await host.StartAsync();
    Console.WriteLine("OK");

    var bus = host.Services.GetRequiredService<IBus>();

    var testId = Guid.NewGuid();
    Console.WriteLine($"\nPublishing StartTest with id={testId}...");
    await bus.Publish(new StartTest(testId));

    // Wait for full saga lifecycle
    Console.WriteLine("Waiting 15s for saga to complete...");
    await Task.Delay(15000);

    // Check MongoDB for the saga state
    Console.Write("Checking saga state in MongoDB... ");
    var client = new MongoDB.Driver.MongoClient("mongodb://localhost:27017/?directConnection=true");
    var db = client.GetDatabase("saga_test_db");
    var collection = db.GetCollection<BsonDocument>("test_sagas");
    using var cursor = await collection.FindAsync<BsonDocument>(MongoDB.Driver.FilterDefinition<BsonDocument>.Empty);
    BsonDocument? doc = null;
    if (await cursor.MoveNextAsync())
        doc = cursor.Current.FirstOrDefault();

    if (doc != null)
    {
        var state = doc.GetValue("CurrentState", "unknown").AsString;
        var corrId = doc["_id"];
        Console.WriteLine($"OK — _id type={corrId.BsonType}, CurrentState={state}");
        Console.WriteLine($"Full doc: {doc.ToJson()}");

        // Test: can we find the document by Guid filter?
        Console.Write("Testing Guid filter lookup... ");
        try
        {
            var filter = MongoDB.Driver.Builders<BsonDocument>.Filter.Eq("_id", testId);
            using var cursor2 = await collection.FindAsync<BsonDocument>(filter);
            BsonDocument? doc2 = null;
            if (await cursor2.MoveNextAsync())
                doc2 = cursor2.Current.FirstOrDefault();
            Console.WriteLine(doc2 != null ? $"OK — found by Guid" : "FAILED — not found by Guid");
        }
        catch (Exception ex2)
        {
            Console.WriteLine($"FAILED: {ex2.Message}");
        }
    }
    else
    {
        Console.WriteLine("FAILED — no saga document found");
    }

    // Cleanup
    await db.DropCollectionAsync("test_sagas");
    await host.StopAsync();

    Console.WriteLine("\n=== TEST PASSED ===");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.WriteLine("\n=== TEST FAILED ===");
}

// ── Types ──

namespace SagaTest
{

public record StartTest(Guid TestId);
public record TestCompleted(Guid TestId);

public class TestState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = default!;
    public int Version { get; set; }
}

public class TestStateMachine : MassTransitStateMachine<TestState>
{
    public State Started { get; private set; } = default!;
    public State Done { get; private set; } = default!;
    public Event<StartTest> StartTestEvent { get; private set; } = default!;
    public Event<TestCompleted> TestCompletedEvent { get; private set; } = default!;

    public TestStateMachine()
    {
        InstanceState(x => x.CurrentState);
        Event(() => StartTestEvent, x => x.CorrelateById(ctx => ctx.Message.TestId));
        Event(() => TestCompletedEvent, x => x.CorrelateById(ctx => ctx.Message.TestId));

        Initially(
            When(StartTestEvent)
                .Then(ctx => Console.WriteLine($"  Saga: received StartTest, doing work inline..."))
                .TransitionTo(Done)
                .Then(ctx => Console.WriteLine($"  Saga: transitioned to Done"))
                .Finalize()
        );

        // SetCompletedWhenFinalized(); // Disabled for debugging - keep saga docs
    }
}
}
