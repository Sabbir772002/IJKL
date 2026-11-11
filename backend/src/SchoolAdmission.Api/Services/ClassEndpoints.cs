using System.Text.Json;
using SchoolAdmission.Api.Config;

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
        app.MapPatch("/classes/{classId}", EditAsync);
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, AppOptions options, ClassService classes, CancellationToken token)
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

        var session = input.Session?.Trim() ?? options.DefaultSession;
        if (session.Length is < 1 or > 32 || session.Contains('\0'))
            return Error(400, "VALIDATION_ERROR", "Session must contain 1 to 32 characters.");

        var (created, existing) = await classes.CreateAsync(name, session, input.TotalSeats, token);
        if (existing is not null)
            return Duplicate(existing);

        return Results.Created($"/classes/{created!.Id}",
            new { created.Id, created.Name, created.TotalSeats, created.Session, created.BlockedSeats });
    }

    private static async Task<IResult> EditAsync(
        string classId, HttpRequest request, ClassService classes, CancellationToken token)
    {
        if (!request.HasJsonContentType())
            return Error(400, "VALIDATION_ERROR", "Provide a JSON request body.");

        EditClassRequest? input;
        try
        {
            input = await request.ReadFromJsonAsync<EditClassRequest>(token);
        }
        catch (JsonException)
        {
            return Error(400, "VALIDATION_ERROR", "Provide valid class details as JSON.");
        }

        if (input is null || (input.Name is null && input.TotalSeats is null))
            return Error(400, "VALIDATION_ERROR", "Provide a class name or seat count.");

        var name = input.Name?.Trim();
        if (name is not null && (name.Length is < 1 or > 120 || name.Contains('\0')))
            return Error(400, "VALIDATION_ERROR", "Class name must contain 1 to 120 characters.");
        if (input.TotalSeats is < 1 or > 100000)
            return Error(400, "VALIDATION_ERROR", "Total seats must be an integer from 1 to 100000.");

        return await classes.EditAsync(classId, name, input.TotalSeats, token);
    }

    internal static IResult Duplicate(ClassSeatRow existing) => Results.Conflict(new
    {
        error = new { code = "CLASS_EXISTS", message = "This class already exists in the session.", existing }
    });

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);

    private sealed record CreateClassRequest(string? Name, int TotalSeats, string? Session);
    private sealed record EditClassRequest(string? Name, int? TotalSeats);
}
