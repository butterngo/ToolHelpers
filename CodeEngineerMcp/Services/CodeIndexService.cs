using CodeEngineerMcp.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CodeEngineerMcp.Services;

public interface ICodeIndexService
{
	Task<IEnumerable<SearchResult>> SearchAsync(
		string rootPath,
		string query,
		SearchOptions? options = null,
		CancellationToken ct = default);

	Task<FileContent> GetFileContentAsync(
		string filePath,
		int? startLine = null,
		int? endLine = null,
		CancellationToken ct = default);

	Task<DirectoryListing> ListDirectoryAsync(
		string path,
		int maxDepth = 2,
		CancellationToken ct = default);
}

public partial class CodeIndexService : ICodeIndexService
{
	private readonly ILogger<CodeIndexService> _logger;

	private static readonly string[] DefaultExcludeFolders =
	[
		"bin", "obj", "node_modules", ".git", ".vs", ".idea",
		"packages", "TestResults", ".nuget", "artifacts"
	];

	private static readonly string[] CodeExtensions =
	[
		".cs", ".csx", ".vb", ".fs", ".fsx",
		".js", ".ts", ".jsx", ".tsx", ".vue",
		".py", ".rb", ".go", ".rs", ".java", ".kt",
		".cpp", ".c", ".h", ".hpp",
		".json", ".xml", ".yaml", ".yml", ".toml",
		".md", ".txt", ".sql", ".sh", ".ps1", ".psm1"
	];

	public CodeIndexService(ILogger<CodeIndexService> logger)
	{
		_logger = logger;
	}

	public async Task<IEnumerable<SearchResult>> SearchAsync(
		string rootPath,
		string query,
		SearchOptions? options = null,
		CancellationToken ct = default)
	{
		options ??= new SearchOptions();
		var results = new ConcurrentBag<SearchResult>();
		var excludeFolders = options.ExcludeFolders ?? DefaultExcludeFolders;

		var files = GetCodeFiles(rootPath, options.FilePattern, excludeFolders);

		var regex = options.UseRegex
			? new Regex(query, options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
			: null;

		var comparison = options.CaseSensitive
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

		await Parallel.ForEachAsync(files, ct, async (file, token) =>
		{
			if (results.Count >= options.MaxResults)
				return;

			try
			{
				var lines = await File.ReadAllLinesAsync(file, token);
				for (int i = 0; i < lines.Length && results.Count < options.MaxResults; i++)
				{
					var line = lines[i];
					string? matchedText = null;

					if (regex != null)
					{
						var match = regex.Match(line);
						if (match.Success)
							matchedText = match.Value;
					}
					else if (line.Contains(query, comparison))
					{
						matchedText = query;
					}

					if (matchedText != null)
					{
						results.Add(new SearchResult(
							GetRelativePath(rootPath, file),
							i + 1,
							line.Trim(),
							matchedText
						));
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to search file: {File}", file);
			}
		});

		return results.Take(options.MaxResults).OrderBy(r => r.FilePath).ThenBy(r => r.LineNumber);
	}

	public async Task<FileContent> GetFileContentAsync(
		string filePath,
		int? startLine = null,
		int? endLine = null,
		CancellationToken ct = default)
	{
		if (!File.Exists(filePath))
			throw new FileNotFoundException($"File not found: {filePath}");

		var allLines = await File.ReadAllLinesAsync(filePath, ct);
		var totalLines = allLines.Length;

		var start = Math.Max(1, startLine ?? 1);
		var end = Math.Min(totalLines, endLine ?? totalLines);

		var selectedLines = allLines
			.Skip(start - 1)
			.Take(end - start + 1);

		var numberedContent = selectedLines
			.Select((line, idx) => $"{start + idx,6} | {line}");

		return new FileContent(
			filePath,
			string.Join(Environment.NewLine, numberedContent),
			totalLines,
			start,
			end
		);
	}

	public Task<DirectoryListing> ListDirectoryAsync(
		string path,
		int maxDepth = 2,
		CancellationToken ct = default)
	{
		if (!Directory.Exists(path))
			throw new DirectoryNotFoundException($"Directory not found: {path}");

		var entries = GetDirectoryEntries(path, 0, maxDepth);

		return Task.FromResult(new DirectoryListing(path, entries));
	}

	private List<DirectoryEntry> GetDirectoryEntries(string path, int currentDepth, int maxDepth)
	{
		var entries = new List<DirectoryEntry>();

		try
		{
			// Add directories
			foreach (var dir in Directory.GetDirectories(path))
			{
				var dirName = Path.GetFileName(dir);
				if (DefaultExcludeFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
					continue;

				var children = currentDepth < maxDepth
					? GetDirectoryEntries(dir, currentDepth + 1, maxDepth)
					: null;

				entries.Add(new DirectoryEntry(
					dirName,
					dir,
					IsDirectory: true,
					Size: null,
					Children: children
				));
			}

			// Add files
			foreach (var file in Directory.GetFiles(path))
			{
				var ext = Path.GetExtension(file);
				if (!CodeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
					continue;

				var fileInfo = new FileInfo(file);
				entries.Add(new DirectoryEntry(
					fileInfo.Name,
					file,
					IsDirectory: false,
					Size: fileInfo.Length,
					Children: null
				));
			}
		}
		catch (UnauthorizedAccessException)
		{
			_logger.LogWarning("Access denied to directory: {Path}", path);
		}

		return entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name).ToList();
	}

	private IEnumerable<string> GetCodeFiles(string rootPath, string? pattern, string[] excludeFolders)
	{
		var searchPattern = pattern ?? "*.*";

		return Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories)
			.Where(f =>
			{
				var ext = Path.GetExtension(f);
				if (!CodeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
					return false;

				var relativePath = GetRelativePath(rootPath, f);
				return !excludeFolders.Any(folder =>
					relativePath.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}",
						StringComparison.OrdinalIgnoreCase) ||
					relativePath.StartsWith($"{folder}{Path.DirectorySeparatorChar}",
						StringComparison.OrdinalIgnoreCase));
			});
	}

	private static string GetRelativePath(string rootPath, string fullPath)
	{
		return Path.GetRelativePath(rootPath, fullPath);
	}
}