using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class FindSymbolTool
{
	private readonly IRoslynWorkspaceService _workspaceService;

	public FindSymbolTool(IRoslynWorkspaceService workspaceService)
	{
		_workspaceService = workspaceService;
	}

	[McpServerTool(Name = "find_symbol")]
	[Description("Find symbol definitions (classes, methods, properties, etc.) by name using Roslyn semantic analysis. Requires a solution to be loaded via SOLUTION_PATH environment variable.")]
	public async Task<string> FindSymbolAsync(
		[Description("Name of the symbol to find (class, method, property, etc.)")]
		string symbolName,

		CancellationToken ct = default)
	{
		try
		{
			if (!_workspaceService.IsLoaded)
			{
				return "No solution loaded. Set SOLUTION_PATH environment variable to a .sln file path.";
			}

			var symbols = await _workspaceService.FindSymbolsAsync(symbolName, ct);
			var symbolList = symbols.ToList();

			if (symbolList.Count == 0)
			{
				return $"No symbols found matching '{symbolName}'";
			}

			var results = symbolList.Select(s => new
			{
				Name = s.Name,
				FullName = s.ToDisplayString(),
				Kind = s.Kind.ToString(),
				ContainingType = s.ContainingType?.ToDisplayString(),
				ContainingNamespace = s.ContainingNamespace?.ToDisplayString(),
				Location = GetLocationInfo(s),
				Accessibility = s.DeclaredAccessibility.ToString(),
				IsStatic = s.IsStatic,
				Documentation = s.GetDocumentationCommentXml()?.Trim()
			});

			var output = new
			{
				Query = symbolName,
				Count = symbolList.Count,
				Symbols = results
			};

			return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			return $"Error finding symbol: {ex.Message}";
		}
	}

	private static object? GetLocationInfo(ISymbol symbol)
	{
		var location = symbol.Locations.FirstOrDefault();
		if (location == null || !location.IsInSource)
			return null;

		var lineSpan = location.GetLineSpan();
		return new
		{
			FilePath = lineSpan.Path,
			StartLine = lineSpan.StartLinePosition.Line + 1,
			EndLine = lineSpan.EndLinePosition.Line + 1,
			StartColumn = lineSpan.StartLinePosition.Character + 1
		};
	}
}