using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class AnalyzeDependenciesTool
{
	private readonly IRoslynWorkspaceService _workspaceService;

	public AnalyzeDependenciesTool(IRoslynWorkspaceService workspaceService)
	{
		_workspaceService = workspaceService;
	}

	[McpServerTool(Name = "analyze_dependencies")]
	[Description("Analyze project and package dependencies in the solution. Shows project references and NuGet packages for each project.")]
	public async Task<string> AnalyzeDependenciesAsync(
		[Description("Output format: 'json' for structured data, 'tree' for visual diagram. Default: tree")]
		string format = "tree",

		CancellationToken ct = default)
	{
		try
		{
			if (!_workspaceService.IsLoaded)
			{
				return "No solution loaded. Set SOLUTION_PATH environment variable to a .sln file path.";
			}

			var dependencies = await _workspaceService.GetDependenciesAsync(ct);
			var depList = dependencies.ToList();

			if (depList.Count == 0)
			{
				return "No projects found in the solution.";
			}

			if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
			{
				return JsonSerializer.Serialize(new
				{
					ProjectCount = depList.Count,
					Projects = depList.Select(d => new
					{
						d.ProjectName,
						d.ProjectPath,
						ProjectReferences = d.ProjectReferences,
						PackageReferences = d.PackageReferences.Select(p => new { p.Name, p.Version })
					})
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			// Tree format
			var sb = new StringBuilder();
			sb.AppendLine("📦 Solution Dependencies");
			sb.AppendLine("========================");
			sb.AppendLine();

			foreach (var project in depList.OrderBy(p => p.ProjectName))
			{
				sb.AppendLine($"🔷 {project.ProjectName}");

				if (project.ProjectReferences.Count > 0)
				{
					sb.AppendLine("   📁 Project References:");
					foreach (var projRef in project.ProjectReferences)
					{
						sb.AppendLine($"      └── {projRef}");
					}
				}

				if (project.PackageReferences.Count > 0)
				{
					sb.AppendLine("   📦 Package References:");
					var topPackages = project.PackageReferences.Take(10).ToList();
					foreach (var pkgRef in topPackages)
					{
						sb.AppendLine($"      └── {pkgRef.Name} ({pkgRef.Version})");
					}

					if (project.PackageReferences.Count > 10)
					{
						sb.AppendLine($"      └── ... and {project.PackageReferences.Count - 10} more packages");
					}
				}

				sb.AppendLine();
			}

			// Summary
			sb.AppendLine("📊 Summary");
			sb.AppendLine("----------");
			sb.AppendLine($"Total Projects: {depList.Count}");
			sb.AppendLine($"Total Project References: {depList.Sum(p => p.ProjectReferences.Count)}");
			sb.AppendLine($"Total Package References: {depList.Sum(p => p.PackageReferences.Count)}");

			// Find circular dependencies hint
			var projectNames = depList.Select(p => p.ProjectName).ToHashSet();
			var orphanProjects = depList.Where(p =>
				p.ProjectReferences.Count == 0 &&
				!depList.Any(other => other.ProjectReferences.Contains(p.ProjectName)))
				.ToList();

			if (orphanProjects.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("⚠️  Standalone Projects (no references to/from):");
				foreach (var orphan in orphanProjects)
				{
					sb.AppendLine($"   - {orphan.ProjectName}");
				}
			}

			return sb.ToString();
		}
		catch (Exception ex)
		{
			return $"Error analyzing dependencies: {ex.Message}";
		}
	}
}