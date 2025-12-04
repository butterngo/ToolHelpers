using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using CodeEngineerMcp.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CodeEngineerMcp.UT.Tools;

public class DeleteFileToolTests : IDisposable
{
	private readonly string _testDirectory;
	private readonly Mock<IFileWriteService> _writeServiceMock;
	private readonly Mock<ICodeIndexService> _indexServiceMock;
	private readonly Mock<IRoslynWorkspaceService> _workspaceServiceMock;
	private readonly Mock<ILogger<DeleteFileTool>> _loggerMock;
	private readonly DeleteFileTool _tool;

	public DeleteFileToolTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), "CodeEngineerMcp_Tests", Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory);

		_writeServiceMock = new Mock<IFileWriteService>();
		_indexServiceMock = new Mock<ICodeIndexService>();
		_workspaceServiceMock = new Mock<IRoslynWorkspaceService>();
		_loggerMock = new Mock<ILogger<DeleteFileTool>>();

		_tool = new DeleteFileTool(
			_writeServiceMock.Object,
			_indexServiceMock.Object,
			_workspaceServiceMock.Object,
			_loggerMock.Object);
	}

	public void Dispose()
	{
		if (Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, recursive: true);
		}
	}

	#region Basic Delete Tests

	[Fact]
	public async Task DeleteFileAsync_FileNotFound_ReturnsFailure()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.False(response.GetProperty("Success").GetBoolean());
		Assert.Contains("not found", response.GetProperty("Message").GetString());
	}

	[Fact]
	public async Task DeleteFileAsync_WithForce_SkipsReferenceCheck()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class Test { }");

		_writeServiceMock
			.Setup(x => x.DeleteFileAsync(filePath, true, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new WriteResult(true, "Deleted", filePath));

		// Act
		var result = await _tool.DeleteFileAsync(filePath, force: true);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.True(response.GetProperty("Success").GetBoolean());
		Assert.Contains("force=true", response.GetProperty("Warning").GetString());
		
		// Verify search was NOT called
		_indexServiceMock.Verify(
			x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	#endregion

	#region Reference Check Tests

	[Fact]
	public async Task DeleteFileAsync_WithReferences_BlocksDeletion()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "Referenced.cs");
		await File.WriteAllTextAsync(filePath, "public class Referenced { }");

		var references = new List<SearchResult>
		{
			new SearchResult("OtherFile.cs", 10, "var x = new Referenced();", "Referenced")
		};

		_indexServiceMock
			.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(references);

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.False(response.GetProperty("Success").GetBoolean());
		Assert.Contains("reference", response.GetProperty("Message").GetString().ToLower());
		Assert.Equal(1, response.GetProperty("ReferencesFound").GetInt32());

		// Verify delete was NOT called
		_writeServiceMock.Verify(
			x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[Fact]
	public async Task DeleteFileAsync_NoReferences_DeletesFile()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "Unreferenced.cs");
		await File.WriteAllTextAsync(filePath, "public class Unreferenced { }");

		_indexServiceMock
			.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SearchResult>());

		_workspaceServiceMock.Setup(x => x.IsLoaded).Returns(false);

		_writeServiceMock
			.Setup(x => x.DeleteFileAsync(filePath, false, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new WriteResult(true, "Deleted", filePath));

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.True(response.GetProperty("Success").GetBoolean());
		Assert.True(response.GetProperty("ReferenceCheckPerformed").GetBoolean());
		Assert.Equal(0, response.GetProperty("ReferencesFound").GetInt32());
	}

	[Fact]
	public async Task DeleteFileAsync_ExcludesSelfFromReferences()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "SelfRef.cs");
		await File.WriteAllTextAsync(filePath, "public class SelfRef { SelfRef other; }");

		// Return reference to self (should be excluded)
		var references = new List<SearchResult>
		{
			new SearchResult("SelfRef.cs", 1, "public class SelfRef { SelfRef other; }", "SelfRef")
		};

		_indexServiceMock
			.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(references);

		_workspaceServiceMock.Setup(x => x.IsLoaded).Returns(false);

		_writeServiceMock
			.Setup(x => x.DeleteFileAsync(filePath, false, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new WriteResult(true, "Deleted", filePath));

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert - Should succeed because self-reference is excluded
		Assert.True(response.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region Custom Search Root Tests

	[Fact]
	public async Task DeleteFileAsync_WithSearchRoot_UsesCustomRoot()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		var customRoot = Path.Combine(_testDirectory, "custom");
		Directory.CreateDirectory(customRoot);
		await File.WriteAllTextAsync(filePath, "public class Test { }");

		_indexServiceMock
			.Setup(x => x.SearchAsync(customRoot, It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<SearchResult>());

		_workspaceServiceMock.Setup(x => x.IsLoaded).Returns(false);

		_writeServiceMock
			.Setup(x => x.DeleteFileAsync(filePath, false, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new WriteResult(true, "Deleted", filePath));

		// Act
		await _tool.DeleteFileAsync(filePath, searchRoot: customRoot);

		// Assert - Verify search used custom root
		_indexServiceMock.Verify(
			x => x.SearchAsync(customRoot, It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()),
			Times.AtLeastOnce);
	}

	#endregion

	#region Multiple References Tests

	[Fact]
	public async Task DeleteFileAsync_ManyReferences_ShowsFirst20()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "Popular.cs");
		await File.WriteAllTextAsync(filePath, "public class Popular { }");

		var references = Enumerable.Range(1, 30)
			.Select(i => new SearchResult($"File{i}.cs", i, $"using Popular; // line {i}", "Popular"))
			.ToList();

		_indexServiceMock
			.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(references);

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.False(response.GetProperty("Success").GetBoolean());
		Assert.True(response.GetProperty("HasMoreReferences").GetBoolean());
		Assert.Equal(30, response.GetProperty("ReferencesFound").GetInt32());
	}

	#endregion

	#region Suggestion Tests

	[Fact]
	public async Task DeleteFileAsync_WithReferences_IncludesSuggestion()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class Test { }");

		var references = new List<SearchResult>
		{
			new SearchResult("Other.cs", 1, "using Test;", "Test")
		};

		_indexServiceMock
			.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(references);

		// Act
		var result = await _tool.DeleteFileAsync(filePath);
		var response = JsonSerializer.Deserialize<JsonElement>(result);

		// Assert
		Assert.True(response.TryGetProperty("Suggestion", out var suggestion));
		Assert.Contains("force=true", suggestion.GetString());
	}

	#endregion
}
