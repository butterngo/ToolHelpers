using CodeEngineerMcp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace CodeEngineerMcp.Services;

public interface IRoslynWorkspaceService
{
	Task LoadSolutionAsync(string solutionPath, CancellationToken ct = default);
	Task LoadProjectAsync(string projectPath, CancellationToken ct = default);
	Solution? CurrentSolution { get; }
	bool IsLoaded { get; }
	Task<IEnumerable<ISymbol>> FindSymbolsAsync(string name, CancellationToken ct = default);
	Task<IEnumerable<ReferencedSymbol>> FindReferencesAsync(string symbolName, CancellationToken ct = default);
	Task<IEnumerable<ProjectDependency>> GetDependenciesAsync(CancellationToken ct = default);
}

public class RoslynWorkspaceService : IRoslynWorkspaceService, IDisposable
{
	private readonly ILogger<RoslynWorkspaceService> _logger;
	private MSBuildWorkspace? _workspace;
	private Solution? _solution;
	private readonly SemaphoreSlim _lock = new(1, 1);

	public RoslynWorkspaceService(ILogger<RoslynWorkspaceService> logger)
	{
		_logger = logger;
	}

	public Solution? CurrentSolution => _solution;
	public bool IsLoaded => _solution != null;

	public async Task LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			_logger.LogInformation("Loading solution: {SolutionPath}", solutionPath);

			_workspace?.Dispose();
			_workspace = MSBuildWorkspace.Create();

			_workspace.RegisterWorkspaceFailedHandler((args) =>
			{
				_logger.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
			});

			_solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);

			_logger.LogInformation("Solution loaded with {ProjectCount} projects", _solution.ProjectIds.Count);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task LoadProjectAsync(string projectPath, CancellationToken ct = default)
	{
		await _lock.WaitAsync(ct);
		try
		{
			_logger.LogInformation("Loading project: {ProjectPath}", projectPath);

			_workspace?.Dispose();
			_workspace = MSBuildWorkspace.Create();

			var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
			_solution = project.Solution;

			_logger.LogInformation("Project loaded: {ProjectName}", project.Name);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<IEnumerable<ISymbol>> FindSymbolsAsync(string name, CancellationToken ct = default)
	{
		if (_solution == null)
			return [];

		var results = new List<ISymbol>();

		foreach (var project in _solution.Projects)
		{
			var compilation = await project.GetCompilationAsync(ct);
			if (compilation == null) continue;

			var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
				project,
				name,
				ignoreCase: true,
				cancellationToken: ct);

			results.AddRange(symbols);
		}

		return results;
	}

	public async Task<IEnumerable<ReferencedSymbol>> FindReferencesAsync(string symbolName, CancellationToken ct = default)
	{
		if (_solution == null)
			return [];

		var symbols = await FindSymbolsAsync(symbolName, ct);
		var firstSymbol = symbols.FirstOrDefault();

		if (firstSymbol == null)
			return [];

		return await SymbolFinder.FindReferencesAsync(firstSymbol, _solution, ct);
	}

	public Task<IEnumerable<ProjectDependency>> GetDependenciesAsync(CancellationToken ct = default)
	{
		if (_solution == null)
			return Task.FromResult<IEnumerable<ProjectDependency>>([]);

		var dependencies = _solution.Projects.Select(project =>
		{
			var projectRefs = project.ProjectReferences
				.Select(pr => _solution.GetProject(pr.ProjectId)?.Name ?? "Unknown")
				.ToList();

			var packageRefs = project.MetadataReferences
				.OfType<PortableExecutableReference>()
				.Where(r => r.FilePath?.Contains("nuget", StringComparison.OrdinalIgnoreCase) == true)
				.Select(r =>
				{
					var path = r.FilePath ?? "";
					var parts = path.Split(Path.DirectorySeparatorChar);
					var nugetIdx = Array.FindIndex(parts, p =>
						p.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
						p.Equals(".nuget", StringComparison.OrdinalIgnoreCase));

					if (nugetIdx >= 0 && nugetIdx + 2 < parts.Length)
					{
						return new PackageReference(parts[nugetIdx + 1], parts[nugetIdx + 2]);
					}
					return new PackageReference(Path.GetFileNameWithoutExtension(path), "unknown");
				})
				.DistinctBy(p => p.Name)
				.ToList();

			return new ProjectDependency(
				project.Name,
				project.FilePath ?? "",
				projectRefs,
				packageRefs
			);
		});

		return Task.FromResult(dependencies);
	}

	public void Dispose()
	{
		_workspace?.Dispose();
		_lock.Dispose();
	}
}