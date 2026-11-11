using System.Text.Json;

namespace SchoolAdmission.Api.Services;

public static class ClassEndpoints
{
    public static void MapClassEndpoints(this WebApplication app)
    {
        app.MapGet("/classes", async (string? session, ClassService classes, CancellationToken token) =>
            Results.Ok(await classes.ListAsync(session, token)));

        app.MapGet("/classes/{classId}", async (string classId, ClassService classes, CancellationToken token) =>
            await classes.GetAsync(classId, token) is { } item
                ? Results.Ok(item)
                : Error(404, "NOT_FOUND", "Class not found."));

        app.MapPost("/classes", CreateAsync);
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, IConfiguration configuration, ClassService classes, CancellationToken token)
    {
        if (!request.HasJsonContentType())
            return Error(400, "VALIDATION_ERROR", "Provide a JSON request body.");

        CreateClassRequest? input;
        try
        {
            input = await request.ReadFromJsonAsync<CreateClassRequest>(token);
        }
        catch (JsonException)
        {
            return Error(400, "VALIDATION_ERROR", "Provide valid class details as JSON.");
        }

        var name = input?.Name?.Trim();
        if (name is null || name.Length is < 1 or > 120 || name.Contains('\0'))
            return Error(400, "VALIDATION_ERROR", "Class name must contain 1 to 120 characters.");

        if (input!.TotalSeats is < 1 or > 100000)
            return Error(400, "VALIDATION_ERROR", "Total seats must be an integer from 1 to 100000.");

        var defaultSession = configuration["DEFAULT_SESSION"]?.Trim();
        var session = input.Session?.Trim() ?? (string.IsNullOrEmpty(defaultSession) ? "2027" : defaultSession);
        if (session.Length is < 1 or > 32 || session.Contains('\0'))
            return Error(400, "VALIDATION_ERROR", "Session must contain 1 to 32 characters.");

        var (created, existing) = await classes.CreateAsync(name, session, input.TotalSeats, token);
        if (existing is not null)
            return Results.Conflict(new
            {
                error = new { code = "CLASS_EXISTS", message = "This class already exists in the session.", existing }
            });

        return Results.Created($"/classes/{created!.Id}",
            new { created.Id, created.Name, created.TotalSeats, created.Session, created.BlockedSeats });
    }

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);

    private sealed record CreateClassRequest(string? Name, int TotalSeats, string? Session);
}
