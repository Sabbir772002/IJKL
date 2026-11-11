using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;
using System.Text.Json;
using SchoolAdmission.Api.Config;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(services => AppOptions.Read(services.GetRequiredService<IConfiguration>()));
builder.Services.AddDbContextFactory<AdmissionDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.")));
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddHostedService<ExpirySweeper>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.UseExceptionHandler(handler => handler.Run(context =>
    ClassEndpoints.Error(500, "INTERNAL", "An unexpected error occurred.").ExecuteAsync(context)));
app.MapClassEndpoints();
app.MapHoldEndpoints();

app.Run();

public partial class Program { }
