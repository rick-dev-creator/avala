var builder = DistributedApplication.CreateBuilder(args);

var fleet = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("fleet");

builder.AddProject<Projects.Avala_Server>("server")
    .WithReference(fleet)
    .WaitFor(fleet);

await builder.Build().RunAsync();
