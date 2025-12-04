using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class RenameFileTool
{
	private readonly IFileWriteService _writeService;

	public RenameFileTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "rename_file")]
	[Description("Rename or move a file to a new location. Creates destination directory if it doesn't exist.")]
	public async Task<string> RenameFileAsync(
		[Description("Current path of the file")]
		string sourcePath,

		[Description("New path for the file (can include new directory)")]
		string destinationPath,

		[Description("Whether to overwrite if destination exists. Default: false")]
		bool overwrite = false,

		CancellationToken ct = default)
	{
		var result = await _writeService.RenameFileAsync(sourcePath, destinationPath, overwrite, ct);

		return JsonSerializer.Serialize(new
		{
			result.Success,
			result.Message,
			result.FilePath
		}, new JsonSerializerOptions { WriteIndented = true });
	}
}