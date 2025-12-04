using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class GetFileContentTool
{
	private readonly ICodeIndexService _indexService;

	public GetFileContentTool(ICodeIndexService indexService)
	{
		_indexService = indexService;
	}

	[McpServerTool(Name ="get_file_content")]
	[Description("Get the content of a file with line numbers. Optionally specify a line range.")]
	public async Task<string> GetFileContentAsync(
		[Description("Full path to the file")]
		string filePath,

		[Description("Starting line number (1-based). Optional.")]
		int? startLine = null,

		[Description("Ending line number (1-based, inclusive). Optional.")]
		int? endLine = null,

		CancellationToken ct = default)
	{
		try
		{
			var content = await _indexService.GetFileContentAsync(filePath, startLine, endLine, ct);

			var output = new
			{
				content.FilePath,
				content.TotalLines,
				Range = $"Lines {content.StartLine}-{content.EndLine}",
				Content = content.Content
			};

			return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (FileNotFoundException)
		{
			return $"File not found: {filePath}";
		}
		catch (Exception ex)
		{
			return $"Error reading file: {ex.Message}";
		}
	}
}