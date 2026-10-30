var builder = DistributedApplication.CreateBuilder(args);

var administratorKey = builder.AddParameter(
    "administrator-key",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);

var fleet = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("fleet");

builder.AddProject<Projects.Avala_Server>("server")
    .WithReference(fleet)
    .WithEnvironment("Authentication__AdministratorKey", administratorKey)
    .WaitFor(fleet);

await builder.Build().RunAsync();
