using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CodeEngineerMcp.UT.Services;

public class FileWriteServiceTests : IDisposable
{
	private readonly string _testDirectory;
	private readonly FileWriteService _service;
	private readonly Mock<ILogger<FileWriteService>> _loggerMock;

	public FileWriteServiceTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), "CodeEngineerMcp_Tests", Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory);
		
		_loggerMock = new Mock<ILogger<FileWriteService>>();
		_service = new FileWriteService(_loggerMock.Object);
	}

	public void Dispose()
	{
		if (Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, recursive: true);
		}
	}

	#region CreateFileAsync Tests

	[Fact]
	public async Task CreateFileAsync_WithValidPath_CreatesFile()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		var content = "public class Test { }";

		// Act
		var result = await _service.CreateFileAsync(filePath, content);

		// Assert
		Assert.True(result.Success);
		Assert.True(File.Exists(filePath));
		Assert.Equal(content, await File.ReadAllTextAsync(filePath));
	}

	[Fact]
	public async Task CreateFileAsync_WithNestedDirectory_CreatesDirectoryAndFile()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nested", "folder", "test.cs");
		var content = "public class Test { }";

		// Act
		var result = await _service.CreateFileAsync(filePath, content);

		// Assert
		Assert.True(result.Success);
		Assert.True(File.Exists(filePath));
	}

	[Fact]
	public async Task CreateFileAsync_FileExists_WithoutOverwrite_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "existing.cs");
		await File.WriteAllTextAsync(filePath, "original content");

		// Act
		var result = await _service.CreateFileAsync(filePath, "new content", overwrite: false);

		// Assert
		Assert.False(result.Success);
		Assert.Contains("already exists", result.Message);
		Assert.Equal("original content", await File.ReadAllTextAsync(filePath));
	}

	[Fact]
	public async Task CreateFileAsync_FileExists_WithOverwrite_ReplacesContent()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "existing.cs");
		await File.WriteAllTextAsync(filePath, "original content");

		// Act
		var result = await _service.CreateFileAsync(filePath, "new content", overwrite: true);

		// Assert
		Assert.True(result.Success);
		Assert.Equal("new content", await File.ReadAllTextAsync(filePath));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task CreateFileAsync_WithInvalidPath_ReturnsFailure(string? invalidPath)
	{
		// Act
		var result = await _service.CreateFileAsync(invalidPath!, "content");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("Error", result.Message);
	}

	#endregion

	#region EditFileAsync Tests

	[Fact]
	public async Task EditFileAsync_WithValidReplacement_EditsFile()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "edit.cs");
		await File.WriteAllTextAsync(filePath, "public class OldName { }");

		// Act
		var result = await _service.EditFileAsync(filePath, "OldName", "NewName");

		// Assert
		Assert.True(result.Success);
		Assert.Equal("public class NewName { }", await File.ReadAllTextAsync(filePath));
	}

	[Fact]
	public async Task EditFileAsync_ContentNotFound_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "edit.cs");
		await File.WriteAllTextAsync(filePath, "public class Test { }");

		// Act
		var result = await _service.EditFileAsync(filePath, "NonExistent", "Replacement");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("not found", result.Message);
	}

	[Fact]
	public async Task EditFileAsync_MultipleOccurrences_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "edit.cs");
		await File.WriteAllTextAsync(filePath, "Test Test Test");

		// Act
		var result = await _service.EditFileAsync(filePath, "Test", "Replaced");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("appears", result.Message);
		Assert.Contains("times", result.Message);
	}

	[Fact]
	public async Task EditFileAsync_FileNotFound_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

		// Act
		var result = await _service.EditFileAsync(filePath, "old", "new");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("not found", result.Message);
	}

	#endregion

	#region InsertLinesAsync Tests

	[Fact]
	public async Task InsertLinesAsync_AtBeginning_InsertsCorrectly()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "insert.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3");

		// Act
		var result = await _service.InsertLinesAsync(filePath, 1, "NewLine");

		// Assert
		Assert.True(result.Success);
		var lines = await File.ReadAllLinesAsync(filePath);
		Assert.Equal("NewLine", lines[0]);
		Assert.Equal("Line1", lines[1]);
	}

	[Fact]
	public async Task InsertLinesAsync_AtMiddle_InsertsCorrectly()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "insert.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3");

		// Act
		var result = await _service.InsertLinesAsync(filePath, 2, "NewLine");

		// Assert
		Assert.True(result.Success);
		var lines = await File.ReadAllLinesAsync(filePath);
		Assert.Equal("Line1", lines[0]);
		Assert.Equal("NewLine", lines[1]);
		Assert.Equal("Line2", lines[2]);
	}

	[Fact]
	public async Task InsertLinesAsync_MultipleLines_InsertsAll()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "insert.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2");

		// Act
		var result = await _service.InsertLinesAsync(filePath, 2, "NewA\nNewB\nNewC");

		// Assert
		Assert.True(result.Success);
		Assert.Equal(3, result.LinesAffected);
		var lines = await File.ReadAllLinesAsync(filePath);
		Assert.Equal(5, lines.Length);
	}

	[Fact]
	public async Task InsertLinesAsync_InvalidLineNumber_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "insert.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2");

		// Act
		var result = await _service.InsertLinesAsync(filePath, 100, "NewLine");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("out of range", result.Message);
	}

	#endregion

	#region DeleteLinesAsync Tests

	[Fact]
	public async Task DeleteLinesAsync_SingleLine_DeletesCorrectly()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "delete.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3");

		// Act
		var result = await _service.DeleteLinesAsync(filePath, 2, 2);

		// Assert
		Assert.True(result.Success);
		var lines = await File.ReadAllLinesAsync(filePath);
		Assert.Equal(2, lines.Length);
		Assert.Equal("Line1", lines[0]);
		Assert.Equal("Line3", lines[1]);
	}

	[Fact]
	public async Task DeleteLinesAsync_MultipleLines_DeletesCorrectly()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "delete.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3\nLine4\nLine5");

		// Act
		var result = await _service.DeleteLinesAsync(filePath, 2, 4);

		// Assert
		Assert.True(result.Success);
		Assert.Equal(3, result.LinesAffected);
		var lines = await File.ReadAllLinesAsync(filePath);
		Assert.Equal(2, lines.Length);
		Assert.Equal("Line1", lines[0]);
		Assert.Equal("Line5", lines[1]);
	}

	[Fact]
	public async Task DeleteLinesAsync_InvalidRange_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "delete.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2");

		// Act
		var result = await _service.DeleteLinesAsync(filePath, 3, 5);

		// Assert
		Assert.False(result.Success);
		Assert.Contains("Invalid line range", result.Message);
	}

	#endregion

	#region RenameFileAsync Tests

	[Fact]
	public async Task RenameFileAsync_ValidPaths_RenamesFile()
	{
		// Arrange
		var sourcePath = Path.Combine(_testDirectory, "source.cs");
		var destPath = Path.Combine(_testDirectory, "destination.cs");
		await File.WriteAllTextAsync(sourcePath, "content");

		// Act
		var result = await _service.RenameFileAsync(sourcePath, destPath);

		// Assert
		Assert.True(result.Success);
		Assert.False(File.Exists(sourcePath));
		Assert.True(File.Exists(destPath));
	}

	[Fact]
	public async Task RenameFileAsync_ToNewDirectory_CreatesDirectoryAndMoves()
	{
		// Arrange
		var sourcePath = Path.Combine(_testDirectory, "source.cs");
		var destPath = Path.Combine(_testDirectory, "newFolder", "destination.cs");
		await File.WriteAllTextAsync(sourcePath, "content");

		// Act
		var result = await _service.RenameFileAsync(sourcePath, destPath);

		// Assert
		Assert.True(result.Success);
		Assert.True(File.Exists(destPath));
	}

	[Fact]
	public async Task RenameFileAsync_DestinationExists_WithoutOverwrite_ReturnsFalse()
	{
		// Arrange
		var sourcePath = Path.Combine(_testDirectory, "source.cs");
		var destPath = Path.Combine(_testDirectory, "destination.cs");
		await File.WriteAllTextAsync(sourcePath, "source content");
		await File.WriteAllTextAsync(destPath, "dest content");

		// Act
		var result = await _service.RenameFileAsync(sourcePath, destPath, overwrite: false);

		// Assert
		Assert.False(result.Success);
		Assert.Contains("already exists", result.Message);
	}

	[Fact]
	public async Task RenameFileAsync_SourceNotFound_ReturnsFalse()
	{
		// Arrange
		var sourcePath = Path.Combine(_testDirectory, "nonexistent.cs");
		var destPath = Path.Combine(_testDirectory, "destination.cs");

		// Act
		var result = await _service.RenameFileAsync(sourcePath, destPath);

		// Assert
		Assert.False(result.Success);
		Assert.Contains("not found", result.Message);
	}

	#endregion

	#region AppendToFileAsync Tests

	[Fact]
	public async Task AppendToFileAsync_ValidFile_AppendsContent()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "append.cs");
		await File.WriteAllTextAsync(filePath, "Original");

		// Act
		var result = await _service.AppendToFileAsync(filePath, " Appended");

		// Assert
		Assert.True(result.Success);
		Assert.Equal("Original Appended", await File.ReadAllTextAsync(filePath));
	}

	[Fact]
	public async Task AppendToFileAsync_FileNotFound_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

		// Act
		var result = await _service.AppendToFileAsync(filePath, "content");

		// Assert
		Assert.False(result.Success);
		Assert.Contains("not found", result.Message);
	}

	#endregion

	#region DeleteFileAsync Tests

	[Fact]
	public async Task DeleteFileAsync_ValidFile_DeletesFile()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "todelete.cs");
		await File.WriteAllTextAsync(filePath, "content");

		// Act
		var result = await _service.DeleteFileAsync(filePath);

		// Assert
		Assert.True(result.Success);
		Assert.False(File.Exists(filePath));
	}

	[Fact]
	public async Task DeleteFileAsync_FileNotFound_ReturnsFalse()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

		// Act
		var result = await _service.DeleteFileAsync(filePath);

		// Assert
		Assert.False(result.Success);
		Assert.Contains("not found", result.Message);
	}

	#endregion
}
