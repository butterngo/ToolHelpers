using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CodeEngineerMcp.Tools
{
	[McpServerToolType]
	public class LoadSolutionTool
	{
		private readonly IRoslynWorkspaceService _workspaceService;

		public LoadSolutionTool(IRoslynWorkspaceService workspaceService)
		{
			_workspaceService = workspaceService;
		}

		[McpServerTool(Name = "load_solution")]
		[Description("Load a .NET solution for code analysis. Required before using find_symbol, find_references, or analyze_dependencies.")]
		public async Task<string> LoadSolutionAsync(
			[Description("Full path to the .sln file")]
		string solutionPath,
			CancellationToken ct = default)
		{
			if (!File.Exists(solutionPath))
				return $"Solution file not found: {solutionPath}";

			await _workspaceService.LoadSolutionAsync(solutionPath, ct);

			var projectCount = _workspaceService.CurrentSolution?.ProjectIds.Count ?? 0;
			return $"Solution loaded successfully with {projectCount} projects.";
		}
	}
}
