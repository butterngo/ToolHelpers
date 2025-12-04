namespace CodeEngineerMcp.Models;

public record WriteResult(
bool Success,
string Message,
string? FilePath = null,
int? LinesAffected = null,
string? BackupPath = null
);
