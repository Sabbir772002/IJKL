using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;
using System.Text.Json;
using SchoolAdmission.Api.Config;

var builder = WebApplication.CreateBuilder(args);
var origins = (builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(origins)
    .WithHeaders("Content-Type", "X-Student-Id")
    .WithExposedHeaders("Date")
    .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")));

builder.Services.AddSingleton(services => AppOptions.Read(services.GetRequiredService<IConfiguration>()));
builder.Services.AddDbContextFactory<AdmissionDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.")));
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddHostedService<ExpirySweeper>();
builder.Services.AddSingleton<ClassStream>();
builder.Services.AddHostedService(services => services.GetRequiredService<ClassStream>());
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.UseExceptionHandler(handler => handler.Run(context =>
    ClassEndpoints.Error(500, "INTERNAL", "An unexpected error occurred.").ExecuteAsync(context)));
app.MapGet("/classes/stream", (HttpContext context, ClassStream stream, ClassService classes) =>
    stream.WriteAsync(context, classes));
app.MapClassEndpoints();
app.MapHoldEndpoints();
app.MapAdminEndpoints();

app.Run();

public partial class Program { }
