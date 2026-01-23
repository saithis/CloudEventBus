var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("postgres-ceb");

var notesDb = postgres.AddDatabase("notesdb");

// Add RabbitMQ message broker
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("rabbitmq-ceb");

// Add the PlaygroundApi with references to the database and message broker
var api = builder.AddProject("playgroundapi", "../PlaygroundApi/PlaygroundApi.csproj")
    .WithReference(notesDb).WaitFor(notesDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq);

builder.Build().Run();
