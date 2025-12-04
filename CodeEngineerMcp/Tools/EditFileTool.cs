using System.ComponentModel;
using System.Text.Json;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class EditFileTool
{
	private readonly IFileWriteService _writeService;

	public EditFileTool(IFileWriteService writeService)
	{
		_writeService = writeService;
	}

	[McpServerTool(Name = "edit_file")]
	[Description("Edit a file by replacing specific content. The content to replace must appear exactly once in the file to ensure precise editing.")]
	public async Task<string> EditFileAsync(
		[Description("Path to the file to edit")]
		string filePath,

		[Description("The exact content to find and replace (must be unique in the file)")]
		string oldContent,

		[Description("The new content to replace with")]
		string newContent,

		CancellationToken ct = default)
	{
		var result = await _writeService.EditFileAsync(filePath, oldContent, newContent, ct);

		return JsonSerializer.Serialize(new
		{
			result.Success,
			result.Message,
			result.FilePath,
			result.BackupPath
		}, new JsonSerializerOptions { WriteIndented = true });
	}
}