using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using MovingHeadSequencer.Web;

var deployedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var isPublishedLayout = Directory.Exists(deployedWebRoot);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	Args = args,
	ContentRootPath = isPublishedLayout ? AppContext.BaseDirectory : null,
	WebRootPath = isPublishedLayout ? deployedWebRoot : null,
});
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5187");
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();

var configuredRoot = builder.Configuration["workspace-root"];
var workspacePreference = new WorkspacePreferenceStore(
	builder.Configuration["workspace-preference-path"]);
var workspaceRoot = ResolveWorkspaceRoot(
	configuredRoot,
	builder.Environment.ContentRootPath,
	string.IsNullOrWhiteSpace(configuredRoot) ? workspacePreference.Load() : null);
var editorHost = new EditorWorkspaceHost(workspaceRoot, workspacePreference);
builder.Services.AddSingleton(editorHost);
builder.Services.AddSingleton<IEditorService>(editorHost);

var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (EditorWorkspaceHost host) =>
		TypedResults.Ok(new { status = "ready", workspaceRoot = host.WorkspaceRoot }))
	.WithName("GetEditorHealth")
	.WithSummary("Check the local editor service");
app.MapGet("/api/bootstrap", (string? sequence, int? headCount, IEditorService editor) =>
		Execute(() => editor.Bootstrap(sequence, headCount ?? 4)))
	.WithName("BootstrapEditor")
	.WithSummary("Load a supported sequence into the visual editor")
	.Produces<EditorDocument>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/preview", (EditorRequest request, IEditorService editor) =>
		Execute(() => editor.Compile(request)))
	.WithName("CompileEditorPreview")
	.WithSummary("Compile and validate an edited cue sheet")
	.Produces<EditorDocument>()
	.ProducesProblem(StatusCodes.Status400BadRequest);
app.MapPost("/api/suggestions", (CueRegenerationRequest request, IEditorService editor) =>
		Execute(() => editor.Suggest(request)))
	.WithName("SuggestCueRegeneration")
	.WithSummary("Rank context-aware moving-head alternatives for one cue")
	.Produces<CueRegenerationResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/regenerate-song", (WholeSongRegenerationRequest request, IEditorService editor) =>
		Execute(() => editor.Regenerate(request)))
	.WithName("RegenerateWholeSong")
	.WithSummary("Apply ranked context-aware choreography to every cue atomically")
	.Produces<WholeSongRegenerationResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/generate", (EditorRequest request, IEditorService editor) =>
		Execute(() => editor.Generate(request)))
	.WithName("GenerateEditorSequence")
	.WithSummary("Generate a validated XSQ file in the local workspace")
	.Produces<GenerationResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status409Conflict);
app.MapGet("/api/backups", (string sequence, IEditorService editor) =>
		Execute(() => editor.ListBackups(sequence)))
	.WithName("ListSequenceBackups")
	.WithSummary("List verified backups for an original sequence")
	.Produces<SequenceBackupCatalog>()
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/generate-original", (EditorRequest request, IEditorService editor) =>
		Execute(() => editor.GenerateOriginal(request)))
	.WithName("GenerateOriginalSequence")
	.WithSummary("Back up and atomically replace the original sequence")
	.Produces<OriginalGenerationResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status409Conflict);
app.MapPost("/api/restore-backup", (RestoreBackupRequest request, IEditorService editor) =>
		Execute(() => editor.RestoreBackup(request)))
	.WithName("RestoreOriginalSequenceBackup")
	.WithSummary("Restore an original sequence backup after preserving the current version")
	.Produces<RestoreBackupResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status404NotFound)
	.ProducesProblem(StatusCodes.Status409Conflict);
app.MapGet("/api/project", (string sequence, int headCount, IEditorService editor) =>
		Execute(() => editor.LoadProject(sequence, headCount)))
	.WithName("LoadStudioProject")
	.WithSummary("Load the hidden editor project linked to a sequence and head count")
	.Produces<StudioProjectLoadResult>()
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/project", (SaveStudioProjectRequest request, IEditorService editor) =>
		Execute(() => editor.SaveProject(request)))
	.WithName("SaveStudioProject")
	.WithSummary("Atomically auto-save a hidden editor project")
	.Produces<SaveStudioProjectResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status409Conflict);
app.MapPost("/api/project/reset", (ResetStudioProjectRequest request, IEditorService editor) =>
		Execute(() =>
		{
			editor.ResetProject(request);
			return new { reset = true };
		}))
	.WithName("ResetStudioProject")
	.WithSummary("Archive a hidden project and rebuild from its sequence")
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/workspace", (WorkspaceSwitchRequest request, EditorWorkspaceHost host) =>
		Execute(() => host.Switch(request)))
	.WithName("SwitchWorkspace")
	.WithSummary("Switch to an absolute xLights sequence workspace")
	.Produces<WorkspaceSwitchResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest)
	.ProducesProblem(StatusCodes.Status404NotFound);
app.MapPost("/api/workspace/pick", (int? headCount, EditorWorkspaceHost host) =>
		Execute(() => host.PickAndSwitch(headCount ?? 4)))
	.WithName("PickWorkspace")
	.WithSummary("Choose and switch workspace with the operating-system folder picker")
	.Produces<WorkspaceSwitchResult>()
	.ProducesProblem(StatusCodes.Status400BadRequest);
app.MapFallbackToFile("index.html");

if (!args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase))
{
	app.Lifetime.ApplicationStarted.Register(() =>
	{
		var url = app.Urls.FirstOrDefault(address => address.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
			?? app.Urls.FirstOrDefault();
		if (url is null) return;
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch
		{
			Console.WriteLine($"Open {url} in a browser.");
		}
	});
}

app.Run();

static IResult Execute<T>(Func<T> action)
{
	try
	{
		return Results.Ok(action());
	}
	catch (FileNotFoundException exception)
	{
		return Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Source not found");
	}
	catch (IOException exception)
	{
		return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "File conflict");
	}
	catch (Exception exception) when (exception is InvalidDataException or JsonException)
	{
		return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid editor document");
	}
}

static string ResolveWorkspaceRoot(
	string? configuredRoot,
	string contentRoot,
	string? preferredRoot)
{
	if (!string.IsNullOrWhiteSpace(configuredRoot) && Path.IsPathFullyQualified(configuredRoot))
	{
		return Path.GetFullPath(configuredRoot);
	}

	var configuredCandidate = string.IsNullOrWhiteSpace(configuredRoot)
		? null
		: Path.GetFullPath(configuredRoot, Directory.GetCurrentDirectory());
	if (configuredCandidate is not null && ContainsSequenceSources(configuredCandidate))
	{
		return configuredCandidate;
	}

	if (!string.IsNullOrWhiteSpace(preferredRoot))
	{
		var preferredCandidate = Path.GetFullPath(preferredRoot);
		if (ContainsSequenceSources(preferredCandidate))
		{
			return preferredCandidate;
		}
	}

	foreach (var start in new[] { Directory.GetCurrentDirectory(), contentRoot, AppContext.BaseDirectory })
	{
		var directory = new DirectoryInfo(start);
		while (directory is not null)
		{
			if (ContainsSequenceSources(directory.FullName))
			{
				return directory.FullName;
			}
			directory = directory.Parent;
		}
	}

	return configuredCandidate ?? Path.GetFullPath(Directory.GetCurrentDirectory());
}

static bool ContainsSequenceSources(string path) =>
	Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
		.Any(file => Path.GetExtension(file).Equals(".xsq", StringComparison.OrdinalIgnoreCase));
