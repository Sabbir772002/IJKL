using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SchoolAdmission.Tests;

public sealed class AdmissionFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("ADMISSION_TEST_CONNECTION")
            ?? "Host=localhost;Database=admission_test;Username=postgres;Password=postgres");
        if (connection.Database != "admission_test")
            throw new InvalidOperationException("Integration tests require the admission_test database.");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connection.ConnectionString,
                ["STUDENT_HOLD_SCOPE"] = "session",
                ["HOLD_DURATION_SECONDS"] = "600",
                ["SWEEP_INTERVAL_MS"] = "100"
            }));
    }
}

public sealed class AdmissionTests(AdmissionFactory factory) : IClassFixture<AdmissionFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task ConcurrentApplicationsNeverOversell()
    {
        var id = await CreateClass(10);
        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(_ => Apply(id, Student())));
        Assert.Equal(10, results.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created),
            r => AssertError(r, HttpStatusCode.Conflict, "SEAT_FULL"));
        Assert.Equal(10, await Seats(id, "heldSeats"));
    }

    [Fact]
    public async Task IndependentApiProcessesShareSeatAndStudentLocks()
    {
        using var apiA = new HttpClient { BaseAddress = TestAddress("TEST_API_A") };
        using var apiB = new HttpClient { BaseAddress = TestAddress("TEST_API_B") };
        Assert.NotEqual(apiA.BaseAddress, apiB.BaseAddress);
        await Task.WhenAll(WaitForApi(apiA), WaitForApi(apiB));

        var id = await CreateClass(10, target: apiA);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(index => Apply(id, Student(), index % 2 == 0 ? apiA : apiB)));
        Assert.Equal(10, attempts.Count(reply => reply.Status == HttpStatusCode.Created));
        Assert.All(attempts.Where(reply => reply.Status != HttpStatusCode.Created),
            reply => AssertError(reply, HttpStatusCode.Conflict, "SEAT_FULL"));
        Assert.Equal(10, await Seats(id, "heldSeats", apiA));
        Assert.Equal(10, await Seats(id, "heldSeats", apiB));

        var session = NewSession();
        var first = await CreateClass(session: session, target: apiA);
        var second = await CreateClass(session: session, target: apiB);
        var student = Student();
        var duplicates = await Task.WhenAll(Apply(first, student, apiA), Apply(second, student, apiB));
        Assert.Single(duplicates, reply => reply.Status == HttpStatusCode.Created);
        AssertError(Assert.Single(duplicates, reply => reply.Status != HttpStatusCode.Created),
            HttpStatusCode.Conflict, "ALREADY_APPLIED");
    }

    private static Uri TestAddress(string setting) =>
        new(Environment.GetEnvironmentVariable(setting)
            ?? throw new InvalidOperationException($"Set {setting} or run the suite with compose.test.yml."));

    private static async Task WaitForApi(HttpClient target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            try
            {
                using var response = await target.GetAsync("/health", timeout.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // Kestrel may still be starting while migrations run.
            }

            await Task.Delay(500, timeout.Token);
        }
    }

    [Fact]
    public async Task CancellingASeatPromotesTheFirstWaitlistedStudent()
    {
        var id = await CreateClass(2);
        var owner = Student();
        var held = await Hold(id, owner);
        await Hold(id, Student());
        var first = Student();
        AssertError(await Apply(id, first), HttpStatusCode.Conflict, "SEAT_FULL");
        var path = $"/classes/{id}/waitlist";
        var queued = await Send(HttpMethod.Post, path, first);
        var later = await Send(HttpMethod.Post, path, Student());
        Assert.Equal(HttpStatusCode.Created, queued.Status);
        Assert.Equal(HttpStatusCode.Created, later.Status);
        Assert.Equal("PENDING", queued.Json.GetProperty("status").GetString());
        Assert.Equal(2, later.Json.GetProperty("queuePosition").GetInt32());
        AssertError(await Send(HttpMethod.Post, path, Student()),
            HttpStatusCode.Conflict, "WAITLIST_UNAVAILABLE");

        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(HttpMethod.Delete, $"/holds/{held}", owner)).Status);
        var promoted = queued.Json.GetProperty("holdId").GetString();
        var waiting = later.Json.GetProperty("holdId").GetString();
        Assert.Equal("HELD", (await Send(HttpMethod.Get, $"/holds/{promoted}"))
            .Json.GetProperty("status").GetString());
        Assert.Equal("PENDING", (await Send(HttpMethod.Get, $"/holds/{waiting}"))
            .Json.GetProperty("status").GetString());
        Assert.Equal(2, await Seats(id, "heldSeats"));
    }

    [Fact]
    public async Task ConcurrentApplicationsFromSameStudentOnlySucceedOnce()
    {
        var id = await CreateClass(20);
        var student = Student();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Apply(id, student)));
        Assert.Single(results, r => r.Status == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created),
            r => AssertError(r, HttpStatusCode.Conflict, "ALREADY_APPLIED"));
    }

    [Fact]
    public async Task ConfirmIsIdempotentAndOnlyAllowedForOwner()
    {
        var id = await CreateClass();
        var student = Student();
        var hold = await Hold(id, student);
        var path = $"/holds/{hold}/confirm";
        AssertError(await Send(HttpMethod.Post, path, Student()), HttpStatusCode.Forbidden, "FORBIDDEN");
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send(HttpMethod.Post, path, student)));
        Assert.All(results, r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.Status);
            Assert.Equal("CONFIRMED", r.Json.GetProperty("status").GetString());
        });
        Assert.Equal(1, await Seats(id, "confirmedSeats"));
        AssertError(await Send(HttpMethod.Delete, $"/holds/{hold}", student),
            HttpStatusCode.Conflict, "INVALID_STATE");
    }

    [Fact]
    public async Task ExpiryReturnsSeatAndPreventsConfirmation()
    {
        using var shortHolds = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["HOLD_DURATION_SECONDS"] = "1" })));
        using var expiryClient = shortHolds.CreateClient();
        var id = await CreateClass();
        var student = Student();
        var hold = await Hold(id, student, expiryClient);
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(1, await Seats(id, "availableSeats"));
        var view = await Send(HttpMethod.Get, $"/holds/{hold}");
        Assert.Equal("EXPIRED", view.Json.GetProperty("status").GetString());
        var confirm = await Send(HttpMethod.Post, $"/holds/{hold}/confirm", student);
        Assert.Equal(HttpStatusCode.Conflict, confirm.Status);
        Assert.Contains(ErrorCode(confirm), new[] { "INVALID_STATE", "HOLD_EXPIRED" });
        await Hold(id, student);
    }

    [Fact]
    public async Task CancelReturnsSeatAndRemainsInStudentHistory()
    {
        var id = await CreateClass();
        var student = Student();
        var hold = await Hold(id, student);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/holds/{hold}", student)).Status);
        Assert.Equal(1, await Seats(id, "availableSeats"));
        var history = await Send(HttpMethod.Get, "/me/holds", student);
        Assert.Equal(HttpStatusCode.OK, history.Status);
        var entry = Assert.Single(history.Json.EnumerateArray());
        Assert.Equal(hold, entry.GetProperty("holdId").GetString());
        Assert.Equal("CANCELLED", entry.GetProperty("status").GetString());
        await Hold(id, Student());
    }

    [Fact]
    public async Task ValidatesInputAndUnknownResources()
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/health")).Status);
        AssertError(await Send(HttpMethod.Post, "/classes", body: new { name = "Invalid", totalSeats = 0 }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        AssertError(await Apply(await CreateClass(), "bad"), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
        AssertError(await Apply(Guid.NewGuid().ToString(), Student()), HttpStatusCode.NotFound, "NOT_FOUND");
        AssertError(await Send(HttpMethod.Get, "/holds/missing"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task StudentMayApplyOncePerSession()
    {
        var session = NewSession();
        var student = Student();
        await Hold(await CreateClass(session: session), student);
        AssertError(await Apply(await CreateClass(session: session), student),
            HttpStatusCode.Conflict, "ALREADY_APPLIED");
        await Hold(await CreateClass(), student);
    }

    [Fact]
    public async Task DuplicateClassCreationIsSerializedWithinSession()
    {
        var session = NewSession();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            Send(HttpMethod.Post, "/classes", body: new
            {
                name = index % 2 == 0 ? "Class 6" : " class  6 ",
                totalSeats = 1,
                session
            })));
        var winner = Assert.Single(results, r => r.Status == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            AssertError(r, HttpStatusCode.Conflict, "CLASS_EXISTS");
            Assert.Equal(winner.Json.GetProperty("id").GetString(),
                r.Json.GetProperty("error").GetProperty("existing").GetProperty("id").GetString());
        });
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/classes",
            body: new { name = "Class 6", totalSeats = 1, session = NewSession() })).Status);
    }

    [Fact]
    public async Task AdminCanEditClassAndCannotReduceSeatsBelowOccupancy()
    {
        var session = NewSession();
        var id = await CreateClass(3, session);
        var other = await CreateClass(3, session);
        var path = $"/classes/{id}";
        var name = (await Send(HttpMethod.Get, $"/classes/{other}")).Json.GetProperty("name").GetString();
        AssertError(await Send(HttpMethod.Patch, path, body: new { name }), HttpStatusCode.Conflict, "CLASS_EXISTS");
        var edited = await Send(HttpMethod.Patch, path, body: new { name = "Renamed", totalSeats = 4, blockedSeats = 2 });
        Assert.Equal(HttpStatusCode.OK, edited.Status);
        Assert.Equal("Renamed", edited.Json.GetProperty("name").GetString());
        Assert.Equal(2, edited.Json.GetProperty("availableSeats").GetInt32());
        await Hold(id, Student());
        await Hold(id, Student());
        AssertError(await Send(HttpMethod.Patch, path, body: new { totalSeats = 1 }),
            HttpStatusCode.Conflict, "SEATS_BELOW_OCCUPIED");
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, path, body: new { totalSeats = 2 })).Status);
        Assert.Equal(0, await Seats(id, "blockedSeats"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminCancellationFreesOrBlocksSeat(bool blockSeat)
    {
        var id = await CreateClass();
        var student = Student();
        var hold = await Hold(id, student);
        var cancelPath = $"/admin/holds/{hold}/cancel";
        AssertError(await Send(HttpMethod.Post, cancelPath, body: new { reason = " " }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, cancelPath,
            body: new { reason = "Document review", blockSeat })).Status);
        Assert.Equal(blockSeat ? 1 : 0, await Seats(id, "blockedSeats"));
        var history = await Send(HttpMethod.Get, $"/admin/classes/{id}/holds");
        Assert.Equal(HttpStatusCode.OK, history.Status);
        var entry = Assert.Single(history.Json.EnumerateArray());
        Assert.Equal("CANCELLED", entry.GetProperty("status").GetString());
        Assert.Equal("ADMIN", entry.GetProperty("cancelledBy").GetString());
        Assert.Equal("Document review", entry.GetProperty("cancelReason").GetString());

        var applicants = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Apply(id, Student())));
        Assert.Equal(blockSeat ? 0 : 1, applicants.Count(row => row.Status == HttpStatusCode.Created));
        Assert.All(applicants.Where(row => row.Status != HttpStatusCode.Created),
            row => AssertError(row, HttpStatusCode.Conflict, "SEAT_FULL"));
    }

    [Fact]
    public async Task AdminSoftDeletesOnlyClassesWithoutActiveApplications()
    {
        var id = await CreateClass();
        var student = Student();
        var hold = await Hold(id, student);
        var path = $"/classes/{id}";
        AssertError(await Send(HttpMethod.Delete, path), HttpStatusCode.Conflict, "CLASS_HAS_STUDENTS");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/holds/{hold}", student)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, path)).Status);
        AssertError(await Send(HttpMethod.Get, path), HttpStatusCode.NotFound, "NOT_FOUND");
        AssertError(await Apply(id, Student()), HttpStatusCode.NotFound, "NOT_FOUND");
        var history = await Send(HttpMethod.Get, "/me/holds", student);
        Assert.Equal("CANCELLED", Assert.Single(history.Json.EnumerateArray()).GetProperty("status").GetString());
    }

    private async Task<string> CreateClass(int totalSeats = 1, string? session = null, HttpClient? target = null)
    {
        var result = await Send(HttpMethod.Post, "/classes", body: new
        {
            name = NewSession(), totalSeats, session = session ?? NewSession()
        }, target: target);
        Assert.Equal(HttpStatusCode.Created, result.Status);
        return result.Json.GetProperty("id").GetString()!;
    }

    private async Task<string> Hold(string id, string student, HttpClient? target = null)
    {
        var result = await Apply(id, student, target);
        Assert.Equal(HttpStatusCode.Created, result.Status);
        return result.Json.GetProperty("holdId").GetString()!;
    }

    private Task<Reply> Apply(string id, string student, HttpClient? target = null) =>
        Send(HttpMethod.Post, $"/classes/{id}/holds", student, target: target);

    private async Task<int> Seats(string id, string field, HttpClient? target = null) =>
        (await Send(HttpMethod.Get, $"/classes/{id}", target: target)).Json.GetProperty(field).GetInt32();

    private async Task<Reply> Send(HttpMethod method, string path, string? student = null,
        object? body = null, HttpClient? target = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (student is not null)
            request.Headers.Add("X-Student-Id", student);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await (target ?? client).SendAsync(request);
        var json = response.StatusCode == HttpStatusCode.NoContent
            ? default
            : await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Reply(response.StatusCode, json);
    }

    private static void AssertError(Reply reply, HttpStatusCode status, string code)
    {
        Assert.Equal(status, reply.Status);
        Assert.Equal(code, ErrorCode(reply));
    }

    private static string? ErrorCode(Reply reply) =>
        reply.Json.GetProperty("error").GetProperty("code").GetString();

    private static string Student() => Random.Shared.NextInt64(0, 100_000_000_000_000_000).ToString("D17");
    private static string NewSession() => Guid.NewGuid().ToString("N");
    private sealed record Reply(HttpStatusCode Status, JsonElement Json);
}
