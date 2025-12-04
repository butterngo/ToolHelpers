using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public partial class DeleteFileTool
{
	private readonly IFileWriteService _writeService;
	private readonly ICodeIndexService _indexService;
	private readonly IRoslynWorkspaceService _workspaceService;
	private readonly ILogger<DeleteFileTool> _logger;

	public DeleteFileTool(
		IFileWriteService writeService,
		ICodeIndexService indexService,
		IRoslynWorkspaceService workspaceService,
		ILogger<DeleteFileTool> logger)
	{
		_writeService = writeService;
		_indexService = indexService;
		_workspaceService = workspaceService;
		_logger = logger;
	}

	[McpServerTool(Name = "delete_file")]
	[Description("Safely delete a file after checking for references. By default, checks for code references and usages before deletion to prevent breaking changes.")]
	public async Task<string> DeleteFileAsync(
		[Description("Full path to the file to delete")]
		string filePath,

		[Description("Skip reference checking and force delete. Default: false")]
		bool force = false,

		[Description("Root directory to search for references. If not provided, uses the file's directory.")]
		string? searchRoot = null,

		CancellationToken ct = default)
	{
		try
		{
			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = $"File not found: {fullPath}"
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			var fileName = Path.GetFileName(fullPath);
			var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
			var extension = Path.GetExtension(fullPath).ToLowerInvariant();

			// If force delete, skip reference checking
			if (force)
			{
				var result = await _writeService.DeleteFileAsync(fullPath, force: true, ct);
				return JsonSerializer.Serialize(new
				{
					result.Success,
					result.Message,
					result.FilePath,
					result.BackupPath,
					Warning = "File deleted with force=true, reference checking was skipped."
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			// Determine search root
			var rootPath = searchRoot ?? Path.GetDirectoryName(fullPath) ?? fullPath;
			
			// Find references
			var references = await FindReferencesAsync(fullPath, fileNameWithoutExt, extension, rootPath, ct);

			if (references.Count > 0)
			{
				// References found - return warning with details
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = $"Cannot delete '{fileName}': Found {references.Count} reference(s) in other files.",
					ReferencesFound = references.Count,
					References = references.Take(20).Select(r => new
					{
						r.FilePath,
						r.LineNumber,
						r.LineContent
					}),
					HasMoreReferences = references.Count > 20,
					Suggestion = "Review the references above. Use force=true to delete anyway, or update the referencing files first."
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			// Check Roslyn references for C# files
			if (extension == ".cs" && _workspaceService.IsLoaded)
			{
				var roslynRefs = await FindRoslynReferencesAsync(fullPath, ct);
				if (roslynRefs.Count > 0)
				{
					return JsonSerializer.Serialize(new
					{
						Success = false,
						Message = $"Cannot delete '{fileName}': Found {roslynRefs.Count} symbol reference(s) via Roslyn analysis.",
						SymbolReferences = roslynRefs,
						Suggestion = "These symbols are used elsewhere in the solution. Use force=true to delete anyway."
					}, new JsonSerializerOptions { WriteIndented = true });
				}
			}

			// No references found - safe to delete
			var deleteResult = await _writeService.DeleteFileAsync(fullPath, force: false, ct);

			return JsonSerializer.Serialize(new
			{
				deleteResult.Success,
				deleteResult.Message,
				deleteResult.FilePath,
				deleteResult.BackupPath,
				ReferenceCheckPerformed = true,
				ReferencesFound = 0
			}, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error in DeleteFileTool for: {FilePath}", filePath);
			return JsonSerializer.Serialize(new
			{
				Success = false,
				Message = $"Error: {ex.Message}"
			}, new JsonSerializerOptions { WriteIndented = true });
		}
	}

	private async Task<List<SearchResult>> FindReferencesAsync(
		string filePath,
		string fileNameWithoutExt,
		string extension,
		string rootPath,
		CancellationToken ct)
	{
		var references = new List<SearchResult>();
		var searchPatterns = new List<string>();

		// Build search patterns based on file type
		switch (extension)
		{
			case ".cs":
				// Search for: using statements, class references, file references in csproj
				searchPatterns.Add(fileNameWithoutExt); // Class name (convention: file name = class name)
				break;

			case ".js":
			case ".ts":
			case ".jsx":
			case ".tsx":
				// Search for: import/require statements
				searchPatterns.Add($"from ['\"].*{Regex.Escape(fileNameWithoutExt)}");
				searchPatterns.Add($"require\\(['\"].*{Regex.Escape(fileNameWithoutExt)}");
				searchPatterns.Add($"import ['\"].*{Regex.Escape(fileNameWithoutExt)}");
				break;

			case ".css":
			case ".scss":
			case ".less":
				// Search for: @import statements, url() references
				searchPatterns.Add($"@import.*{Regex.Escape(fileNameWithoutExt)}");
				searchPatterns.Add($"url\\(.*{Regex.Escape(fileNameWithoutExt)}");
				break;

			case ".json":
				// Search for JSON file references
				searchPatterns.Add(Path.GetFileName(filePath));
				break;

			default:
				// Generic: search for filename
				searchPatterns.Add(Path.GetFileName(filePath));
				break;
		}

		// Also always search for the exact filename
		searchPatterns.Add(Path.GetFileName(filePath));

		// Remove duplicates
		searchPatterns = searchPatterns.Distinct().ToList();

		foreach (var pattern in searchPatterns)
		{
			try
			{
				var options = new SearchOptions(
					FilePattern: null,
					CaseSensitive: false,
					UseRegex: pattern.Contains('\\') || pattern.Contains('['), // Use regex if pattern contains regex chars
					MaxResults: 50
				);

				var results = await _indexService.SearchAsync(rootPath, pattern, options, ct);

				foreach (var result in results)
				{
					// Exclude the file itself from results
					var resultFullPath = Path.GetFullPath(Path.Combine(rootPath, result.FilePath));
					if (!resultFullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
					{
						// Avoid duplicates
						if (!references.Any(r => 
							r.FilePath == result.FilePath && 
							r.LineNumber == result.LineNumber))
						{
							references.Add(result);
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error searching for pattern: {Pattern}", pattern);
			}
		}

		return references;
	}

	private async Task<List<object>> FindRoslynReferencesAsync(string filePath, CancellationToken ct)
	{
		var roslynRefs = new List<object>();

		try
		{
			// Read the file and extract type/member names
			var content = await File.ReadAllTextAsync(filePath, ct);
			
			// Simple regex to find class/interface/struct/enum names
			var typeMatches = TypeNameRegex().Matches(content);

			foreach (Match match in typeMatches)
			{
				var typeName = match.Groups[1].Value;
				
				// Find references using Roslyn
				var refs = await _workspaceService.FindReferencesAsync(typeName, ct);
				var refList = refs.ToList();

				foreach (var refSymbol in refList)
				{
					var locations = refSymbol.Locations
						.Where(loc => loc.Location.IsInSource)
						.Where(loc =>
						{
							var locPath = loc.Location.GetLineSpan().Path;
							return !locPath.Equals(filePath, StringComparison.OrdinalIgnoreCase);
						})
						.Select(loc =>
						{
							var lineSpan = loc.Location.GetLineSpan();
							return new
							{
								FilePath = lineSpan.Path,
								Line = lineSpan.StartLinePosition.Line + 1,
								Symbol = typeName
							};
						})
						.ToList();

					roslynRefs.AddRange(locations);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error during Roslyn reference analysis for: {FilePath}", filePath);
		}

		return roslynRefs.DistinctBy(r => $"{((dynamic)r).FilePath}:{((dynamic)r).Line}").Take(20).ToList();
	}

	[GeneratedRegex(@"(?:class|interface|struct|enum|record)\s+(\w+)", RegexOptions.Multiline)]
	private static partial Regex TypeNameRegex();
}
