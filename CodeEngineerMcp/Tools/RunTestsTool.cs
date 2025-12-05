using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public partial class RunTestsTool
{
	private readonly ILogger<RunTestsTool> _logger;

	public RunTestsTool(ILogger<RunTestsTool> logger)
	{
		_logger = logger;
	}

	[McpServerTool(Name = "run_tests")]
	[Description("Run .NET tests using dotnet test command. Returns test results including passed, failed, and skipped tests with details.")]
	public async Task<string> RunTestsAsync(
		[Description("Path to the test project (.csproj), solution (.sln), or directory containing tests")]
		string projectPath,

		[Description("Filter expression to select specific tests (e.g., 'FullyQualifiedName~MyTest' or 'Category=Unit'). Optional.")]
		string? filter = null,

		[Description("Run tests without building first. Default: false")]
		bool noBuild = false,

		[Description("Configuration to use (Debug/Release). Default: Debug")]
		string configuration = "Debug",

		[Description("Verbosity level (quiet, minimal, normal, detailed, diagnostic). Default: minimal")]
		string verbosity = "minimal",

		[Description("Timeout in seconds for test execution. Default: 300 (5 minutes)")]
		int timeoutSeconds = 300,

		[Description("Collect code coverage. Default: false")]
		bool collectCoverage = false,

		CancellationToken ct = default)
	{
		try
		{
			var fullPath = Path.GetFullPath(projectPath);

			if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
			{
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = $"Path not found: {fullPath}"
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			// Build arguments
			var args = new StringBuilder($"test \"{fullPath}\"");
			args.Append($" --configuration {configuration}");
			args.Append($" --verbosity {verbosity}");
			args.Append(" --logger \"trx;LogFileName=TestResults.trx\"");
			args.Append(" --logger \"console;verbosity=detailed\"");

			if (noBuild)
				args.Append(" --no-build");

			if (!string.IsNullOrWhiteSpace(filter))
				args.Append($" --filter \"{filter}\"");

			if (collectCoverage)
				args.Append(" --collect:\"XPlat Code Coverage\"");

			_logger.LogInformation("Running: dotnet {Args}", args);

			var result = await RunDotnetCommandAsync(args.ToString(), timeoutSeconds, ct);

			// Parse the output
			var testSummary = ParseTestOutput(result.Output, result.Error);

			return JsonSerializer.Serialize(new
			{
				Success = result.ExitCode == 0,
				ExitCode = result.ExitCode,
				ExecutionTime = result.ExecutionTime,
				Summary = testSummary,
				RawOutput = result.Output.Length > 5000 
					? result.Output[..5000] + "\n... (truncated)" 
					: result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (OperationCanceledException)
		{
			return JsonSerializer.Serialize(new
			{
				Success = false,
				Message = "Test execution was cancelled"
			}, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error running tests for: {ProjectPath}", projectPath);
			return JsonSerializer.Serialize(new
			{
				Success = false,
				Message = $"Error running tests: {ex.Message}"
			}, new JsonSerializerOptions { WriteIndented = true });
		}
	}

	[McpServerTool(Name = "run_specific_test")]
	[Description("Run a specific test method by its fully qualified name.")]
	public async Task<string> RunSpecificTestAsync(
		[Description("Path to the test project (.csproj) or solution (.sln)")]
		string projectPath,

		[Description("Fully qualified test name (e.g., 'Namespace.TestClass.TestMethod')")]
		string testName,

		[Description("Timeout in seconds. Default: 60")]
		int timeoutSeconds = 60,

		CancellationToken ct = default)
	{
		return await RunTestsAsync(
			projectPath,
			filter: $"FullyQualifiedName~{testName}",
			noBuild: false,
			configuration: "Debug",
			verbosity: "detailed",
			timeoutSeconds: timeoutSeconds,
			collectCoverage: false,
			ct: ct);
	}

	[McpServerTool(Name = "list_tests")]
	[Description("List all available tests in a project without running them.")]
	public async Task<string> ListTestsAsync(
		[Description("Path to the test project (.csproj) or solution (.sln)")]
		string projectPath,

		[Description("Filter expression to filter test names. Optional.")]
		string? filter = null,

		CancellationToken ct = default)
	{
		try
		{
			var fullPath = Path.GetFullPath(projectPath);

			if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
			{
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = $"Path not found: {fullPath}"
				}, new JsonSerializerOptions { WriteIndented = true });
			}

			var args = new StringBuilder($"test \"{fullPath}\" --list-tests");

			if (!string.IsNullOrWhiteSpace(filter))
				args.Append($" --filter \"{filter}\"");

			var result = await RunDotnetCommandAsync(args.ToString(), 120, ct);

			// Parse test list from output
			var tests = ParseTestList(result.Output);

			return JsonSerializer.Serialize(new
			{
				Success = result.ExitCode == 0,
				TestCount = tests.Count,
				Tests = tests,
				RawOutput = result.Output.Length > 3000 
					? result.Output[..3000] + "\n... (truncated)" 
					: result.Output
			}, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error listing tests for: {ProjectPath}", projectPath);
			return JsonSerializer.Serialize(new
			{
				Success = false,
				Message = $"Error listing tests: {ex.Message}"
			}, new JsonSerializerOptions { WriteIndented = true });
		}
	}

	private async Task<(string Output, string Error, int ExitCode, string ExecutionTime)> RunDotnetCommandAsync(
		string arguments,
		int timeoutSeconds,
		CancellationToken ct)
	{
		var stopwatch = Stopwatch.StartNew();

		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Environment.CurrentDirectory
			}
		};

		var outputBuilder = new StringBuilder();
		var errorBuilder = new StringBuilder();

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data != null)
				outputBuilder.AppendLine(e.Data);
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data != null)
				errorBuilder.AppendLine(e.Data);
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

		try
		{
			await process.WaitForExitAsync(timeoutCts.Token);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			// Timeout occurred
			try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
			throw new TimeoutException($"Test execution timed out after {timeoutSeconds} seconds");
		}

		stopwatch.Stop();

		return (
			outputBuilder.ToString(),
			errorBuilder.ToString(),
			process.ExitCode,
			$"{stopwatch.Elapsed.TotalSeconds:F2}s"
		);
	}

	private TestSummary ParseTestOutput(string output, string error)
	{
		var summary = new TestSummary();

		// Parse summary line: "Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5"
		// Or: "Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5"
		var summaryMatch = SummaryRegex().Match(output);
		if (summaryMatch.Success)
		{
			summary.TotalTests = int.TryParse(summaryMatch.Groups["total"].Value, out var t) ? t : 0;
			summary.PassedTests = int.TryParse(summaryMatch.Groups["passed"].Value, out var p) ? p : 0;
			summary.FailedTests = int.TryParse(summaryMatch.Groups["failed"].Value, out var f) ? f : 0;
			summary.SkippedTests = int.TryParse(summaryMatch.Groups["skipped"].Value, out var s) ? s : 0;
		}

		// Parse individual test results
		var passedMatches = PassedTestRegex().Matches(output);
		foreach (Match match in passedMatches)
		{
			summary.PassedTestNames.Add(match.Groups["name"].Value.Trim());
		}

		var failedMatches = FailedTestRegex().Matches(output);
		foreach (Match match in failedMatches)
		{
			var testName = match.Groups["name"].Value.Trim();
			summary.FailedTestNames.Add(testName);

			// Try to extract error message
			var errorMatch = TestErrorRegex().Match(output, match.Index);
			if (errorMatch.Success)
			{
				summary.FailureDetails[testName] = errorMatch.Groups["error"].Value.Trim();
			}
		}

		// Parse duration if available
		var durationMatch = DurationRegex().Match(output);
		if (durationMatch.Success)
		{
			summary.Duration = durationMatch.Groups["duration"].Value;
		}

		return summary;
	}

	private List<TestInfo> ParseTestList(string output)
	{
		var tests = new List<TestInfo>();
		var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		var inTestList = false;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();

			if (trimmed.Contains("The following Tests are available:"))
			{
				inTestList = true;
				continue;
			}

			if (inTestList && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("Test run"))
			{
				// Parse test name - format is typically "    Namespace.Class.Method"
				if (trimmed.Length > 0 && !trimmed.StartsWith("["))
				{
					var parts = trimmed.Split('.');
					tests.Add(new TestInfo
					{
						FullName = trimmed,
						ClassName = parts.Length >= 2 ? parts[^2] : "",
						MethodName = parts.Length >= 1 ? parts[^1] : trimmed
					});
				}
			}
		}

		return tests;
	}

	[GeneratedRegex(@"(Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)", RegexOptions.IgnoreCase)]
	private static partial Regex SummaryRegex();

	[GeneratedRegex(@"✓\s+(?<name>[^\[]+)", RegexOptions.Multiline)]
	private static partial Regex PassedTestRegex();

	[GeneratedRegex(@"✗\s+(?<name>[^\[]+)", RegexOptions.Multiline)]
	private static partial Regex FailedTestRegex();

	[GeneratedRegex(@"Error Message:\s*(?<error>[^\n]+)", RegexOptions.Multiline)]
	private static partial Regex TestErrorRegex();

	[GeneratedRegex(@"Duration:\s*(?<duration>[\d\.:]+)", RegexOptions.IgnoreCase)]
	private static partial Regex DurationRegex();
}

public class TestSummary
{
	public int TotalTests { get; set; }
	public int PassedTests { get; set; }
	public int FailedTests { get; set; }
	public int SkippedTests { get; set; }
	public string? Duration { get; set; }
	public List<string> PassedTestNames { get; set; } = [];
	public List<string> FailedTestNames { get; set; } = [];
	public Dictionary<string, string> FailureDetails { get; set; } = [];
	
	public string Status => FailedTests == 0 ? "✅ All tests passed" : $"❌ {FailedTests} test(s) failed";
}

public class TestInfo
{
	public string FullName { get; set; } = "";
	public string ClassName { get; set; } = "";
	public string MethodName { get; set; } = "";
}
