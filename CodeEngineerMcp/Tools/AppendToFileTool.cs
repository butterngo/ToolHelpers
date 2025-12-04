using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class AppendToFileTool
{
	private readonly IFileWriteService _writeService;

	public AppendToFileTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "append_to_file")]
	[Description("Append content to the end of an existing file.")]
	public async Task<string> AppendToFileAsync(
		[Description("Path to the file to append to")]
		string filePath,

		[Description("Content to append to the end of the file")]
		string content,

		CancellationToken ct = default)
	{
		var result = await _writeService.AppendToFileAsync(filePath, content, ct);

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