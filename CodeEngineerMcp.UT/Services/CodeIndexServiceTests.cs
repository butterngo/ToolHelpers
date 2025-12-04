using CodeEngineerMcp.Models;
using CodeEngineerMcp.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CodeEngineerMcp.UT.Services;

public class CodeIndexServiceTests : IDisposable
{
	private readonly string _testDirectory;
	private readonly CodeIndexService _service;
	private readonly Mock<ILogger<CodeIndexService>> _loggerMock;

	public CodeIndexServiceTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), "CodeEngineerMcp_Tests", Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory);
		
		_loggerMock = new Mock<ILogger<CodeIndexService>>();
		_service = new CodeIndexService(_loggerMock.Object);
	}

	public void Dispose()
	{
		if (Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, recursive: true);
		}
	}

	#region SearchAsync Tests

	[Fact]
	public async Task SearchAsync_FindsMatchingText()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class MyTestClass { }");

		// Act
		var results = await _service.SearchAsync(_testDirectory, "MyTestClass");

		// Assert
		var resultList = results.ToList();
		Assert.Single(resultList);
		Assert.Contains("MyTestClass", resultList[0].LineContent);
	}

	[Fact]
	public async Task SearchAsync_CaseInsensitive_FindsMatch()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class UPPERCASE { }");

		var options = new SearchOptions(CaseSensitive: false);

		// Act
		var results = await _service.SearchAsync(_testDirectory, "uppercase", options);

		// Assert
		Assert.Single(results);
	}

	[Fact]
	public async Task SearchAsync_CaseSensitive_NoMatchWhenCaseDiffers()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class UPPERCASE { }");

		var options = new SearchOptions(CaseSensitive: true);

		// Act
		var results = await _service.SearchAsync(_testDirectory, "uppercase", options);

		// Assert
		Assert.Empty(results);
	}

	[Fact]
	public async Task SearchAsync_WithRegex_FindsPattern()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "test.cs");
		await File.WriteAllTextAsync(filePath, "public class Test123 { }");

		var options = new SearchOptions(UseRegex: true);

		// Act
		var results = await _service.SearchAsync(_testDirectory, @"Test\d+", options);

		// Assert
		Assert.Single(results);
		Assert.Equal("Test123", results.First().MatchedText);
	}

	[Fact]
	public async Task SearchAsync_WithFilePattern_OnlySearchesMatchingFiles()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.cs"), "FindMe in CS");
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.txt"), "FindMe in TXT");

		var options = new SearchOptions(FilePattern: "*.cs");

		// Act
		var results = await _service.SearchAsync(_testDirectory, "FindMe", options);

		// Assert
		var resultList = results.ToList();
		Assert.Single(resultList);
		Assert.EndsWith(".cs", resultList[0].FilePath);
	}

	[Fact]
	public async Task SearchAsync_RespectsMaxResults()
	{
		// Arrange
		var content = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line{i} Match"));
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.cs"), content);

		var options = new SearchOptions(MaxResults: 5);

		// Act
		var results = await _service.SearchAsync(_testDirectory, "Match", options);

		// Assert
		Assert.Equal(5, results.Count());
	}

	[Fact]
	public async Task SearchAsync_ExcludesFolders()
	{
		// Arrange
		var binDir = Path.Combine(_testDirectory, "bin");
		Directory.CreateDirectory(binDir);
		await File.WriteAllTextAsync(Path.Combine(binDir, "test.cs"), "FindMe in bin");
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.cs"), "FindMe in root");

		// Act
		var results = await _service.SearchAsync(_testDirectory, "FindMe");

		// Assert
		var resultList = results.ToList();
		Assert.Single(resultList);
		Assert.DoesNotContain("bin", resultList[0].FilePath);
	}

	[Fact]
	public async Task SearchAsync_NoMatches_ReturnsEmpty()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.cs"), "Some content");

		// Act
		var results = await _service.SearchAsync(_testDirectory, "NonExistent");

		// Assert
		Assert.Empty(results);
	}

	#endregion

	#region GetFileContentAsync Tests

	[Fact]
	public async Task GetFileContentAsync_ReturnsFullContent()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "content.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3");

		// Act
		var result = await _service.GetFileContentAsync(filePath);

		// Assert
		Assert.Equal(filePath, result.FilePath);
		Assert.Equal(3, result.TotalLines);
		Assert.Contains("Line1", result.Content);
		Assert.Contains("Line2", result.Content);
		Assert.Contains("Line3", result.Content);
	}

	[Fact]
	public async Task GetFileContentAsync_WithLineRange_ReturnsSubset()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "content.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2\nLine3\nLine4\nLine5");

		// Act
		var result = await _service.GetFileContentAsync(filePath, startLine: 2, endLine: 4);

		// Assert
		Assert.Equal(2, result.StartLine);
		Assert.Equal(4, result.EndLine);
		Assert.Contains("Line2", result.Content);
		Assert.Contains("Line3", result.Content);
		Assert.Contains("Line4", result.Content);
		Assert.DoesNotContain("Line1", result.Content);
		Assert.DoesNotContain("Line5", result.Content);
	}

	[Fact]
	public async Task GetFileContentAsync_FileNotFound_ThrowsException()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "nonexistent.cs");

		// Act & Assert
		await Assert.ThrowsAsync<FileNotFoundException>(() => 
			_service.GetFileContentAsync(filePath));
	}

	[Fact]
	public async Task GetFileContentAsync_IncludesLineNumbers()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "content.cs");
		await File.WriteAllTextAsync(filePath, "Line1\nLine2");

		// Act
		var result = await _service.GetFileContentAsync(filePath);

		// Assert
		Assert.Contains("1 |", result.Content);
		Assert.Contains("2 |", result.Content);
	}

	#endregion

	#region ListDirectoryAsync Tests

	[Fact]
	public async Task ListDirectoryAsync_ReturnsDirectoryContents()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file1.cs"), "");
		await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file2.cs"), "");
		Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir"));

		// Act
		var result = await _service.ListDirectoryAsync(_testDirectory);

		// Assert
		Assert.Equal(_testDirectory, result.Path);
		Assert.Equal(3, result.Entries.Count); // 2 files + 1 directory
	}

	[Fact]
	public async Task ListDirectoryAsync_RespectsMaxDepth()
	{
		// Arrange
		var level1 = Path.Combine(_testDirectory, "level1");
		var level2 = Path.Combine(level1, "level2");
		var level3 = Path.Combine(level2, "level3");
		Directory.CreateDirectory(level3);
		await File.WriteAllTextAsync(Path.Combine(level3, "deep.cs"), "");

		// Act
		var result = await _service.ListDirectoryAsync(_testDirectory, maxDepth: 1);

		// Assert
		var level1Entry = result.Entries.FirstOrDefault(e => e.Name == "level1");
		Assert.NotNull(level1Entry);
		Assert.NotNull(level1Entry.Children);
		
		var level2Entry = level1Entry.Children.FirstOrDefault(e => e.Name == "level2");
		Assert.NotNull(level2Entry);
		Assert.Null(level2Entry.Children); // maxDepth reached
	}

	[Fact]
	public async Task ListDirectoryAsync_ExcludesIgnoredFolders()
	{
		// Arrange
		Directory.CreateDirectory(Path.Combine(_testDirectory, "bin"));
		Directory.CreateDirectory(Path.Combine(_testDirectory, "obj"));
		Directory.CreateDirectory(Path.Combine(_testDirectory, "src"));

		// Act
		var result = await _service.ListDirectoryAsync(_testDirectory);

		// Assert
		Assert.Single(result.Entries); // Only "src"
		Assert.Equal("src", result.Entries[0].Name);
	}

	[Fact]
	public async Task ListDirectoryAsync_DirectoryNotFound_ThrowsException()
	{
		// Arrange
		var path = Path.Combine(_testDirectory, "nonexistent");

		// Act & Assert
		await Assert.ThrowsAsync<DirectoryNotFoundException>(() => 
			_service.ListDirectoryAsync(path));
	}

	[Fact]
	public async Task ListDirectoryAsync_IncludesFileSize()
	{
		// Arrange
		var filePath = Path.Combine(_testDirectory, "sized.cs");
		await File.WriteAllTextAsync(filePath, "Some content here");

		// Act
		var result = await _service.ListDirectoryAsync(_testDirectory);

		// Assert
		var fileEntry = result.Entries.FirstOrDefault(e => e.Name == "sized.cs");
		Assert.NotNull(fileEntry);
		Assert.True(fileEntry.Size > 0);
	}

	#endregion
}
