using System.Net.Http.Json;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;

namespace Skarbiec.Testing.Tests;

/// <summary>
/// Proves <see cref="TenancyIsolationTests{TProgram}"/> (T0.14) actually catches a cross-user leak,
/// using the Notes resource in <see cref="Skarbiec.Testing.Sample"/> as a stand-in for a real
/// service's resource (portfolio, asset, transaction — Phase 1 onward, T1.x).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class NotesTenancyIsolationTests(SkarbiecContainersFixture containers) : TenancyIsolationTests<Program>
{
    protected override SkarbiecApiFactory<Program> Factory { get; } = new NotesApiFactory(containers);

    protected override async Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken)
    {
        var response = await ownerClient.PostAsJsonAsync("/notes", new CreateNoteRequest("owned by A"), cancellationToken);
        response.EnsureSuccessStatusCode();

        return response.Headers.Location!;
    }

    protected override Uri ListUrl { get; } = new("/notes", UriKind.Relative);

    protected override HttpContent CreateUpdatePayload() => JsonContent.Create(new UpdateNoteRequest("stranger's edit"));

    protected override async Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken)
    {
        var notes = await listResponse.Content.ReadFromJsonAsync<List<NoteResponse>>(cancellationToken);

        Assert.Empty(notes!);
    }
}
