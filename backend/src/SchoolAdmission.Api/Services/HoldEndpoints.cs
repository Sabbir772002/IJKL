namespace SchoolAdmission.Api.Services;

public static class HoldEndpoints
{
    public static void MapHoldEndpoints(this WebApplication app)
    {
        app.MapPost("/classes/{classId}/holds", async (
            string classId, HttpRequest request, HoldService holds, CancellationToken token) =>
            ReadStudent(request) is { } student ? await holds.ApplyAsync(classId, student, token) : InvalidStudent());

        app.MapGet("/holds/{holdId}", async (string holdId, HoldService holds, CancellationToken token) =>
            await holds.GetAsync(holdId, token) is { } hold
                ? Results.Ok(new { hold.HoldId, hold.ClassId, hold.Status, expiresAt = HoldService.FormatExpiry(hold.ExpiresAt) })
                : ClassEndpoints.Error(404, "NOT_FOUND", "Application not found."));

        app.MapPost("/holds/{holdId}/confirm", async (
            string holdId, HttpRequest request, HoldService holds, CancellationToken token) =>
            ReadStudent(request) is { } student ? await holds.ChangeAsync(holdId, student, true, token) : InvalidStudent());

        app.MapDelete("/holds/{holdId}", async (
            string holdId, HttpRequest request, HoldService holds, CancellationToken token) =>
            ReadStudent(request) is { } student ? await holds.ChangeAsync(holdId, student, false, token) : InvalidStudent());

        app.MapGet("/me/holds", async (HttpRequest request, HoldService holds, CancellationToken token) =>
            ReadStudent(request) is { } student
                ? Results.Ok((await holds.ListAsync(student, token)).Select(hold => new
                {
                    hold.HoldId, hold.ClassId, hold.ClassName, hold.Status,
                    expiresAt = HoldService.FormatExpiry(hold.ExpiresAt)
                }))
                : InvalidStudent());
    }

    private static string? ReadStudent(HttpRequest request)
    {
        var value = request.Headers["X-Student-Id"].ToString();
        return value.Length == 17 && value.All(char.IsAsciiDigit) ? value : null;
    }

    private static IResult InvalidStudent() =>
        ClassEndpoints.Error(400, "INVALID_STUDENT_ID", "Provide a 17-digit X-Student-Id header.");
}
