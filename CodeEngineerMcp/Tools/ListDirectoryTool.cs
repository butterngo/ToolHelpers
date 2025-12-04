using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public class ListDirectoryTool
{
	private readonly ICodeIndexService _indexService;

	public ListDirectoryTool(ICodeIndexService indexService)
	{
		_indexService = indexService;
	}

	[McpServerTool(Name = "list_directory")]
	[Description("List files and directories in a path, showing the folder structure as a tree")]
	public async Task<string> ListDirectoryAsync(
		[Description("Directory path to list")]
		string path,

		[Description("Maximum depth to traverse. Default: 2")]
		int maxDepth = 2,

		CancellationToken ct = default)
	{
		try
		{
			var listing = await _indexService.ListDirectoryAsync(path, maxDepth, ct);

			var sb = new StringBuilder();
			sb.AppendLine($"📁 {listing.Path}");

			BuildTree(sb, listing.Entries, "", true);

			return sb.ToString();
		}
		catch (DirectoryNotFoundException)
		{
			return $"Directory not found: {path}";
		}
		catch (Exception ex)
		{
			return $"Error listing directory: {ex.Message}";
		}
	}

	private static void BuildTree(
		StringBuilder sb,
		IReadOnlyList<DirectoryEntry> entries,
		string indent,
		bool isRoot)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			var entry = entries[i];
			var isLast = i == entries.Count - 1;
			var prefix = isRoot ? "" : (isLast ? "└── " : "├── ");
			var childIndent = indent + (isRoot ? "" : (isLast ? "    " : "│   "));

			var icon = entry.IsDirectory ? "📁" : GetFileIcon(entry.Name);
			var sizeInfo = entry.Size.HasValue ? $" ({FormatSize(entry.Size.Value)})" : "";

			sb.AppendLine($"{indent}{prefix}{icon} {entry.Name}{sizeInfo}");

			if (entry.Children != null && entry.Children.Count > 0)
			{
				BuildTree(sb, entry.Children, childIndent, false);
			}
		}
	}

	private static string GetFileIcon(string fileName)
	{
		var ext = Path.GetExtension(fileName).ToLowerInvariant();
		return ext switch
		{
			".cs" or ".vb" or ".fs" => "🔷",
			".js" or ".ts" or ".jsx" or ".tsx" => "🟨",
			".py" => "🐍",
			".json" => "📋",
			".xml" or ".xaml" => "📰",
			".md" or ".txt" => "📝",
			".sql" => "🗃️",
			".yaml" or ".yml" => "⚙️",
			".csproj" or ".sln" or ".fsproj" => "🔧",
			_ => "📄"
		};
	}

	private static string FormatSize(long bytes)
	{
		string[] sizes = ["B", "KB", "MB", "GB"];
		int order = 0;
		double size = bytes;

		while (size >= 1024 && order < sizes.Length - 1)
		{
			order++;
			size /= 1024;
		}

		return $"{size:0.##} {sizes[order]}";
	}
}