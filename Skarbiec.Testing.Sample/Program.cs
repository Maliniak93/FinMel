using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Testing.Sample.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var notesConnectionString = builder.Configuration.GetConnectionString("notes-db")
    ?? throw new InvalidOperationException("Missing connection string 'notes-db'.");
builder.Services.AddDbContext<NotesDbContext>(options => options.UseNpgsql(notesConnectionString));

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.MigrateAsync();
}

// Minimal tenant-scoped CRUD resource — exists only so
// Skarbiec.Testing.Tenancy.TenancyIsolationTests<TProgram> (T0.14) has a real HTTP resource to
// prove itself against. The tenancy filter on NotesDbContext (ADR-006) does all the isolation
// work here: "not mine" and "doesn't exist" look identical, which is exactly the point.

app.MapPost("/notes", async (CreateNoteRequest request, NotesDbContext db, CancellationToken ct) =>
{
    var note = new Note { Id = Guid.NewGuid(), Text = request.Text };
    db.Notes.Add(note);
    await db.SaveChangesAsync(ct);

    return TypedResults.Created($"/notes/{note.Id}", new NoteResponse(note.Id, note.Text));
}).RequireAuthorization();

app.MapGet("/notes/{id:guid}", async Task<Results<Ok<NoteResponse>, NotFound>> (Guid id, NotesDbContext db, CancellationToken ct) =>
{
    var note = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    return note is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(new NoteResponse(note.Id, note.Text));
}).RequireAuthorization();

app.MapPut("/notes/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, UpdateNoteRequest request, NotesDbContext db, CancellationToken ct) =>
{
    var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
    if (note is null)
    {
        return TypedResults.NotFound();
    }

    note.Text = request.Text;
    await db.SaveChangesAsync(ct);

    return TypedResults.NoContent();
}).RequireAuthorization();

app.MapDelete("/notes/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, NotesDbContext db, CancellationToken ct) =>
{
    var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
    if (note is null)
    {
        return TypedResults.NotFound();
    }

    db.Notes.Remove(note);
    await db.SaveChangesAsync(ct);

    return TypedResults.NoContent();
}).RequireAuthorization();

app.MapGet("/notes", async (NotesDbContext db, CancellationToken ct) =>
{
    var notes = await db.Notes.AsNoTracking()
        .Select(n => new NoteResponse(n.Id, n.Text))
        .ToListAsync(ct);

    return TypedResults.Ok(notes);
}).RequireAuthorization();

app.Run();

public sealed record CreateNoteRequest(string Text);

public sealed record UpdateNoteRequest(string Text);

public sealed record NoteResponse(Guid Id, string Text);

// Exposed for Skarbiec.Testing.Tests' WebApplicationFactory<Program>.
public partial class Program;
