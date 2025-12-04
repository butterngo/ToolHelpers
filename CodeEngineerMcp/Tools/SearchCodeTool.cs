
using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class SearchCodeTool
{
	private readonly ICodeIndexService _indexService;

	public SearchCodeTool(ICodeIndexService indexService)
	{
		_indexService = indexService;
	}

	[McpServerTool(Name = "search_code")]
	[Description("Search for code across the codebase using text or regex patterns")]
	public async Task<string> SearchCodeAsync(
		[Description("Root directory path to search in")]
		string rootPath,

		[Description("Search query - text or regex pattern")]
		string query,

		[Description("File pattern filter (e.g., '*.cs' for C# files). Optional.")]
		string? filePattern = null,

		[Description("Use regex pattern matching. Default: false")]
		bool useRegex = false,

		[Description("Case sensitive search. Default: false")]
		bool caseSensitive = false,

		[Description("Maximum number of results. Default: 50")]
		int maxResults = 50,

		CancellationToken ct = default)
	{
		try
		{
			var options = new SearchOptions(
				FilePattern: filePattern,
				CaseSensitive: caseSensitive,
				UseRegex: useRegex,
				MaxResults: maxResults
			);

			var results = await _indexService.SearchAsync(rootPath, query, options, ct);
			var resultList = results.ToList();

			if (resultList.Count == 0)
			{
				return $"No results found for '{query}' in {rootPath}";
			}

			var output = new
			{
				Query = query,
				RootPath = rootPath,
				ResultCount = resultList.Count,
				Results = resultList.Select(r => new
				{
					r.FilePath,
					r.LineNumber,
					r.LineContent,
					r.MatchedText
				})
			};

			return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			return $"Error searching code: {ex.Message}";
		}
	}
}