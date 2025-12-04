using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class InsertLinesTool
{
	private readonly IFileWriteService _writeService;

	public InsertLinesTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "insert_lines")]
	[Description("Insert content at a specific line number in a file. Existing content at that line and below will be shifted down.")]
	public async Task<string> InsertLinesAsync(
		[Description("Path to the file to modify")]
		string filePath,

		[Description("Line number where content should be inserted (1-based). Use line 1 to insert at the beginning.")]
		int lineNumber,

		[Description("Content to insert (can be multiple lines)")]
		string content,

		CancellationToken ct = default)
	{
		var result = await _writeService.InsertLinesAsync(filePath, lineNumber, content, ct);

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