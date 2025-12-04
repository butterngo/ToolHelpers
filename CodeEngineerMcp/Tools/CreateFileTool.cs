using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class CreateFileTool
{
	private readonly IFileWriteService _writeService;

	public CreateFileTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "create_file")]
	[Description("Create a new file with the specified content. Creates parent directories if they don't exist.")]
	public async Task<string> CreateFileAsync(
		[Description("Full path where the file should be created")]
		string filePath,

		[Description("Content to write to the file")]
		string content,

		[Description("Whether to overwrite if file exists. Default: false")]
		bool overwrite = false,

		CancellationToken ct = default)
	{
		var result = await _writeService.CreateFileAsync(filePath, content, overwrite, ct);

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