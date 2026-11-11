using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<AdmissionDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddHostedService<MigrationHostedService>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
