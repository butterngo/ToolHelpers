using CodeEngineerMcp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CodeEngineerMcp.Services;

public interface IFileWriteService
{
	Task<WriteResult> CreateFileAsync(string filePath, string content, bool overwrite = false, CancellationToken ct = default);
	Task<WriteResult> EditFileAsync(string filePath, string oldContent, string newContent, CancellationToken ct = default);
	Task<WriteResult> InsertLinesAsync(string filePath, int lineNumber, string content, CancellationToken ct = default);
	Task<WriteResult> DeleteLinesAsync(string filePath, int startLine, int endLine, CancellationToken ct = default);
	Task<WriteResult> RenameFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default);
	Task<WriteResult> AppendToFileAsync(string filePath, string content, CancellationToken ct = default);
	Task<WriteResult> DeleteFileAsync(string filePath, bool force = false, CancellationToken ct = default);
}

public class FileWriteService : IFileWriteService
{
	private readonly ILogger<FileWriteService> _logger;
	private readonly bool _createBackups;
	private readonly string _backupDirectory;

	public FileWriteService(ILogger<FileWriteService> logger, IConfiguration? configuration = null)
	{
		_logger = logger;
		_createBackups = configuration?.GetValue<bool>("FileWrite:CreateBackups") ?? true;
		_backupDirectory = configuration?.GetValue<string>("FileWrite:BackupDirectory") ?? Path.Combine(Path.GetTempPath(), "CodeEngineerMcp", "backups");
	}

	public async Task<WriteResult> CreateFileAsync(string filePath, string content, bool overwrite = false, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

			var fullPath = Path.GetFullPath(filePath);
			var directory = Path.GetDirectoryName(fullPath);

			if (File.Exists(fullPath) && !overwrite)
			{
				return new WriteResult(false, $"File already exists: {fullPath}. Use overwrite=true to replace.");
			}

			// Create directory if needed
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
				_logger.LogInformation("Created directory: {Directory}", directory);
			}

			// Backup existing file if overwriting
			string? backupPath = null;
			if (File.Exists(fullPath) && overwrite && _createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, ct);

			var lineCount = content.Split('\n').Length;
			_logger.LogInformation("Created file: {FilePath} ({Lines} lines)", fullPath, lineCount);

			return new WriteResult(true, $"File created successfully: {fullPath}", fullPath, lineCount, backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to create file: {FilePath}", filePath);
			return new WriteResult(false, $"Error creating file: {ex.Message}");
		}
	}

	public async Task<WriteResult> EditFileAsync(string filePath, string oldContent, string newContent, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
			ArgumentNullException.ThrowIfNull(oldContent);

			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return new WriteResult(false, $"File not found: {fullPath}");
			}

			var currentContent = await File.ReadAllTextAsync(fullPath, ct);

			// Check if old content exists (handle different line endings)
			var normalizedCurrent = NormalizeLineEndings(currentContent);
			var normalizedOld = NormalizeLineEndings(oldContent);

			if (!normalizedCurrent.Contains(normalizedOld))
			{
				return new WriteResult(false, "The specified content to replace was not found in the file.");
			}

			// Count occurrences
			var occurrences = CountOccurrences(normalizedCurrent, normalizedOld);
			if (occurrences > 1)
			{
				return new WriteResult(false, $"The content to replace appears {occurrences} times. Please provide more unique content to ensure a single match.");
			}

			// Create backup
			string? backupPath = null;
			if (_createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			// Perform replacement (preserve original line endings)
			var updatedContent = currentContent.Replace(oldContent, newContent);

			// If that didn't work due to line endings, try normalized
			if (updatedContent == currentContent)
			{
				updatedContent = normalizedCurrent.Replace(normalizedOld, NormalizeLineEndings(newContent));
			}

			await File.WriteAllTextAsync(fullPath, updatedContent, Encoding.UTF8, ct);

			_logger.LogInformation("Edited file: {FilePath}", fullPath);

			return new WriteResult(true, "File edited successfully", fullPath, BackupPath: backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to edit file: {FilePath}", filePath);
			return new WriteResult(false, $"Error editing file: {ex.Message}");
		}
	}

	public async Task<WriteResult> InsertLinesAsync(string filePath, int lineNumber, string content, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return new WriteResult(false, $"File not found: {fullPath}");
			}

			var lines = (await File.ReadAllLinesAsync(fullPath, ct)).ToList();

			if (lineNumber < 1 || lineNumber > lines.Count + 1)
			{
				return new WriteResult(false, $"Line number {lineNumber} is out of range. File has {lines.Count} lines (valid range: 1-{lines.Count + 1}).");
			}

			// Create backup
			string? backupPath = null;
			if (_createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			// Insert new lines
			var newLines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
			lines.InsertRange(lineNumber - 1, newLines);

			await File.WriteAllLinesAsync(fullPath, lines, Encoding.UTF8, ct);

			_logger.LogInformation("Inserted {Count} lines at line {LineNumber} in: {FilePath}", newLines.Count, lineNumber, fullPath);

			return new WriteResult(true, $"Inserted {newLines.Count} lines at line {lineNumber}", fullPath, newLines.Count, backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to insert lines in: {FilePath}", filePath);
			return new WriteResult(false, $"Error inserting lines: {ex.Message}");
		}
	}

	public async Task<WriteResult> DeleteLinesAsync(string filePath, int startLine, int endLine, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return new WriteResult(false, $"File not found: {fullPath}");
			}

			var lines = (await File.ReadAllLinesAsync(fullPath, ct)).ToList();

			if (startLine < 1 || endLine > lines.Count || startLine > endLine)
			{
				return new WriteResult(false, $"Invalid line range {startLine}-{endLine}. File has {lines.Count} lines.");
			}

			// Create backup
			string? backupPath = null;
			if (_createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			var linesToDelete = endLine - startLine + 1;
			lines.RemoveRange(startLine - 1, linesToDelete);

			await File.WriteAllLinesAsync(fullPath, lines, Encoding.UTF8, ct);

			_logger.LogInformation("Deleted lines {Start}-{End} from: {FilePath}", startLine, endLine, fullPath);

			return new WriteResult(true, $"Deleted {linesToDelete} lines ({startLine}-{endLine})", fullPath, linesToDelete, backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to delete lines from: {FilePath}", filePath);
			return new WriteResult(false, $"Error deleting lines: {ex.Message}");
		}
	}

	public async Task<WriteResult> RenameFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
			ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

			var fullSource = Path.GetFullPath(sourcePath);
			var fullDest = Path.GetFullPath(destinationPath);

			if (!File.Exists(fullSource))
			{
				return new WriteResult(false, $"Source file not found: {fullSource}");
			}

			if (File.Exists(fullDest) && !overwrite)
			{
				return new WriteResult(false, $"Destination file already exists: {fullDest}. Use overwrite=true to replace.");
			}

			// Create destination directory if needed
			var destDir = Path.GetDirectoryName(fullDest);
			if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
			{
				Directory.CreateDirectory(destDir);
			}

			File.Move(fullSource, fullDest, overwrite);

			_logger.LogInformation("Renamed file: {Source} -> {Dest}", fullSource, fullDest);

			return new WriteResult(true, $"File renamed: {Path.GetFileName(fullSource)} -> {Path.GetFileName(fullDest)}", fullDest);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to rename file: {Source}", sourcePath);
			return new WriteResult(false, $"Error renaming file: {ex.Message}");
		}
	}

	public async Task<WriteResult> AppendToFileAsync(string filePath, string content, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return new WriteResult(false, $"File not found: {fullPath}");
			}

			// Create backup
			string? backupPath = null;
			if (_createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			await File.AppendAllTextAsync(fullPath, content, Encoding.UTF8, ct);

			var lineCount = content.Split('\n').Length;
			_logger.LogInformation("Appended {Lines} lines to: {FilePath}", lineCount, fullPath);

			return new WriteResult(true, $"Appended {lineCount} lines to file", fullPath, lineCount, backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to append to file: {FilePath}", filePath);
			return new WriteResult(false, $"Error appending to file: {ex.Message}");
		}
	}

	public async Task<WriteResult> DeleteFileAsync(string filePath, bool force = false, CancellationToken ct = default)
	{
		try
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

			var fullPath = Path.GetFullPath(filePath);

			if (!File.Exists(fullPath))
			{
				return new WriteResult(false, $"File not found: {fullPath}");
			}

			// Create backup before deletion
			string? backupPath = null;
			if (_createBackups)
			{
				backupPath = await CreateBackupAsync(fullPath, ct);
			}

			File.Delete(fullPath);

			_logger.LogInformation("Deleted file: {FilePath}", fullPath);

			return new WriteResult(true, $"File deleted successfully: {Path.GetFileName(fullPath)}", fullPath, BackupPath: backupPath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to delete file: {FilePath}", filePath);
			return new WriteResult(false, $"Error deleting file: {ex.Message}");
		}
	}

	private async Task<string> CreateBackupAsync(string filePath, CancellationToken ct)
	{
		var fileName = Path.GetFileName(filePath);
		var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		var backupFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";

		if (!Directory.Exists(_backupDirectory))
		{
			Directory.CreateDirectory(_backupDirectory);
		}

		var backupPath = Path.Combine(_backupDirectory, backupFileName);

		await using var source = File.OpenRead(filePath);
		await using var dest = File.Create(backupPath);
		await source.CopyToAsync(dest, ct);

		_logger.LogInformation("Created backup: {BackupPath}", backupPath);
		return backupPath;
	}

	private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

	private static int CountOccurrences(string text, string pattern)
	{
		int count = 0;
		int index = 0;
		while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
		{
			count++;
			index += pattern.Length;
		}
		return count;
	}
}