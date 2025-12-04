using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class FindReferencesTool
{
	private readonly IRoslynWorkspaceService _workspaceService;

	public FindReferencesTool(IRoslynWorkspaceService workspaceService)
	{
		_workspaceService = workspaceService;
	}

	[McpServerTool(Name = "find_references")]
	[Description("Find all references/usages of a symbol across the codebase using Roslyn semantic analysis. Requires a solution to be loaded.")]
	public async Task<string> FindReferencesAsync(
		[Description("Name of the symbol to find references for")]
		string symbolName,

		CancellationToken ct = default)
	{
		try
		{
			if (!_workspaceService.IsLoaded)
			{
				return "No solution loaded. Set SOLUTION_PATH environment variable to a .sln file path.";
			}

			var references = await _workspaceService.FindReferencesAsync(symbolName, ct);
			var referenceList = references.ToList();

			if (referenceList.Count == 0)
			{
				return $"No references found for '{symbolName}'";
			}

			var results = new List<object>();

			foreach (var referencedSymbol in referenceList)
			{
				var symbol = referencedSymbol.Definition;
				var locations = referencedSymbol.Locations
					.Where(loc => loc.Location.IsInSource)
					.Select(loc =>
					{
						var lineSpan = loc.Location.GetLineSpan();
						return new
						{
							FilePath = lineSpan.Path,
							Line = lineSpan.StartLinePosition.Line + 1,
							Column = lineSpan.StartLinePosition.Character + 1,
							ContainingDocument = loc.Document.Name
						};
					})
					.ToList();

				results.Add(new
				{
					Symbol = new
					{
						Name = symbol.Name,
						FullName = symbol.ToDisplayString(),
						Kind = symbol.Kind.ToString()
					},
					ReferenceCount = locations.Count,
					References = locations
				});
			}

			var output = new
			{
				Query = symbolName,
				TotalReferences = results.Sum(r => ((dynamic)r).ReferenceCount),
				Results = results
			};

			return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			return $"Error finding references: {ex.Message}";
		}
	}
}