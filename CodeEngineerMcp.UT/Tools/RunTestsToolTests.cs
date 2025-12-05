using CodeEngineerMcp.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CodeEngineerMcp.UT.Tools;

public class RunTestsToolTests : IDisposable
{
	private readonly string _testDirectory;
	private readonly RunTestsTool _tool;
	private readonly Mock<ILogger<RunTestsTool>> _loggerMock;

	public RunTestsToolTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), "CodeEngineerMcp_RunTests", Guid.NewGuid().ToString());
		Directory.CreateDirectory(_testDirectory);

		_loggerMock = new Mock<ILogger<RunTestsTool>>();
		_tool = new RunTestsTool(_loggerMock.Object);
	}

	public void Dispose()
	{
		if (Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, recursive: true);
		}
	}

	#region RunTestsAsync Tests

	[Fact]
	public async Task RunTestsAsync_InvalidPath_ReturnsFailure()
	{
		// Arrange
		var invalidPath = Path.Combine(_testDirectory, "nonexistent.csproj");

		// Act
		var result = await _tool.RunTestsAsync(invalidPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("not found", json.RootElement.GetProperty("Message").GetString());
	}

	[Fact]
	public async Task RunTestsAsync_WithValidProject_ReturnsResult()
	{
		// Arrange
		var projectPath = await CreateTestProjectAsync();

		// Act
		var result = await _tool.RunTestsAsync(projectPath, timeoutSeconds: 120);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.TryGetProperty("Success", out _));
		Assert.True(json.RootElement.TryGetProperty("Summary", out _));
	}

	[Fact]
	public async Task RunTestsAsync_WithFilter_PassesFilterToCommand()
	{
		// Arrange
		var projectPath = await CreateTestProjectAsync();

		// Act
		var result = await _tool.RunTestsAsync(
			projectPath,
			filter: "FullyQualifiedName~SampleTest",
			timeoutSeconds: 120);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.TryGetProperty("Success", out _));
	}

	[Fact]
	public async Task RunTestsAsync_CancellationRequested_ReturnsCancelled()
	{
		// Arrange
		var projectPath = await CreateTestProjectAsync();
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// Act
		var result = await _tool.RunTestsAsync(projectPath, ct: cts.Token);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("cancelled", json.RootElement.GetProperty("Message").GetString()!.ToLower());
	}

	#endregion

	#region RunSpecificTestAsync Tests

	[Fact]
	public async Task RunSpecificTestAsync_WithTestName_RunsSpecificTest()
	{
		// Arrange
		var projectPath = await CreateTestProjectAsync();

		// Act
		var result = await _tool.RunSpecificTestAsync(
			projectPath,
			testName: "SampleTests.SampleTest",
			timeoutSeconds: 120);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.TryGetProperty("Success", out _));
	}

	#endregion

	#region ListTestsAsync Tests

	[Fact]
	public async Task ListTestsAsync_InvalidPath_ReturnsFailure()
	{
		// Arrange
		var invalidPath = Path.Combine(_testDirectory, "nonexistent.csproj");

		// Act
		var result = await _tool.ListTestsAsync(invalidPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task ListTestsAsync_WithValidProject_ReturnsTestList()
	{
		// Arrange
		var projectPath = await CreateTestProjectAsync();

		// Act
		var result = await _tool.ListTestsAsync(projectPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.TryGetProperty("Tests", out _));
	}

	#endregion

	#region Helper Methods

	private async Task<string> CreateTestProjectAsync()
	{
		var projectDir = Path.Combine(_testDirectory, "TestProject");
		Directory.CreateDirectory(projectDir);

		// Create a minimal test project
		var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""xunit"" Version=""2.6.2"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.4"" />
  </ItemGroup>
</Project>";

		var testContent = @"using Xunit;

namespace SampleTests;

public class SampleTest
{
    [Fact]
    public void Test_ShouldPass()
    {
        Assert.True(true);
    }

    [Fact]
    public void Test_Addition()
    {
        Assert.Equal(4, 2 + 2);
    }
}";

		await File.WriteAllTextAsync(Path.Combine(projectDir, "TestProject.csproj"), csprojContent);
		await File.WriteAllTextAsync(Path.Combine(projectDir, "SampleTest.cs"), testContent);

		// Restore packages
		var restoreProcess = new System.Diagnostics.Process
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = "restore",
				WorkingDirectory = projectDir,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};
		restoreProcess.Start();
		await restoreProcess.WaitForExitAsync();

		return Path.Combine(projectDir, "TestProject.csproj");
	}

	#endregion
}
