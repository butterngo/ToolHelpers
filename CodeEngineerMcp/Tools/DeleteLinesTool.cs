using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class DeleteLinesTool
{
	private readonly IFileWriteService _writeService;

	public DeleteLinesTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "delete_lines")]
	[Description("Delete a range of lines from a file. Lines are 1-based and inclusive.")]
	public async Task<string> DeleteLinesAsync(
		[Description("Path to the file to modify")]
		string filePath,

		[Description("Starting line number to delete (1-based, inclusive)")]
		int startLine,

		[Description("Ending line number to delete (1-based, inclusive)")]
		int endLine,

		CancellationToken ct = default)
	{
		var result = await _writeService.DeleteLinesAsync(filePath, startLine, endLine, ct);

		return JsonSerializer.Serialize(new
		{
			result.Success,
			result.Message,
			result.FilePath,
			result.LinesAffected,
			result.BackupPath
		}, new JsonSerializerOptions { WriteIndented = true });
	}
}