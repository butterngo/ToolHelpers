namespace CodeEngineerMcp.Models;

public record SearchOptions(
	string? FilePattern = null,
	bool CaseSensitive = false,
	bool UseRegex = false,
	int MaxResults = 50,
	string[]? ExcludeFolders = null
);

public record SearchResult(
	string FilePath,
	int LineNumber,
	string LineContent,
	string MatchedText
);

public record FileContent(
	string FilePath,
	string Content,
	int TotalLines,
	int StartLine,
	int EndLine
);

public record DirectoryListing(
	string Path,
	IReadOnlyList<DirectoryEntry> Entries
);

public record DirectoryEntry(
	string Name,
	string FullPath,
	bool IsDirectory,
	long? Size,
	IReadOnlyList<DirectoryEntry>? Children
);
