using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContextFactory<AdmissionDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddHostedService<ExpirySweeper>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.UseExceptionHandler(handler => handler.Run(context =>
    ClassEndpoints.Error(500, "INTERNAL", "An unexpected error occurred.").ExecuteAsync(context)));
app.MapClassEndpoints();
app.MapHoldEndpoints();

app.Run();
