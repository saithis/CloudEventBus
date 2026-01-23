var builder = DistributedApplication.CreateBuilder(args);

// Create password parameters with default value "guest"
var postgresPassword = builder.AddParameter("postgres-password", "guest", secret: false);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: false);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("postgres-ceb");

var notesDb = postgres.AddDatabase("notesdb");

// Add RabbitMQ message broker
var rabbitmq = builder.AddRabbitMQ("rabbitmq", password: rabbitmqPassword)
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("rabbitmq-ceb");

// Add the PlaygroundApi with references to the database and message broker
var api = builder.AddProject("playgroundapi", "../PlaygroundApi/PlaygroundApi.csproj")
    .WithReference(notesDb).WaitFor(notesDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq);

builder.Build().Run();
