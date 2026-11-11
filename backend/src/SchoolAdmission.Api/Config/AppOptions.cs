namespace SchoolAdmission.Api.Config;

public sealed record AppOptions(int HoldDurationSeconds, int SweepIntervalMs, bool ScopeByClass, string DefaultSession)
{
    public static AppOptions Read(IConfiguration configuration)
    {
        var session = configuration["DEFAULT_SESSION"]?.Trim();
        return new AppOptions(
            PositiveSetting(configuration["HOLD_DURATION_SECONDS"], 600),
            PositiveSetting(configuration["SWEEP_INTERVAL_MS"], 1000),
            configuration["STUDENT_HOLD_SCOPE"] == "class",
            string.IsNullOrEmpty(session) ? "2027" : session);
    }

    private static int PositiveSetting(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
