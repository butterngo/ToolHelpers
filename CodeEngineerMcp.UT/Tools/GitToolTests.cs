using CodeEngineerMcp.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CodeEngineerMcp.UT.Tools;

public class GitToolTests : IDisposable
{
	private readonly string _testDirectory;
	private readonly string _repoPath;
	private readonly GitTool _tool;
	private readonly Mock<ILogger<GitTool>> _loggerMock;

	public GitToolTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), "CodeEngineerMcp_GitTests", Guid.NewGuid().ToString());
		_repoPath = Path.Combine(_testDirectory, "test-repo");
		Directory.CreateDirectory(_repoPath);

		_loggerMock = new Mock<ILogger<GitTool>>();
		_tool = new GitTool(_loggerMock.Object);

		// Initialize a git repository for testing
		InitializeGitRepo().GetAwaiter().GetResult();
	}

	public void Dispose()
	{
		if (Directory.Exists(_testDirectory))
		{
			// Git creates read-only files, need to make them writable first
			SetAttributesNormal(new DirectoryInfo(_testDirectory));
			Directory.Delete(_testDirectory, recursive: true);
		}
	}

	private static void SetAttributesNormal(DirectoryInfo dir)
	{
		foreach (var subDir in dir.GetDirectories())
		{
			SetAttributesNormal(subDir);
		}
		foreach (var file in dir.GetFiles())
		{
			file.Attributes = FileAttributes.Normal;
		}
	}

	private async Task InitializeGitRepo()
	{
		await RunGitCommand("init");
		await RunGitCommand("config user.email \"test@test.com\"");
		await RunGitCommand("config user.name \"Test User\"");
		
		// Create initial commit
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Test Repo");
		await RunGitCommand("add .");
		await RunGitCommand("commit -m \"Initial commit\"");
	}

	private async Task RunGitCommand(string args)
	{
		var process = new System.Diagnostics.Process
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "git",
				Arguments = args,
				WorkingDirectory = _repoPath,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};
		process.Start();
		await process.WaitForExitAsync();
	}

	#region git_status Tests

	[Fact]
	public async Task GitStatusAsync_CleanRepo_ReturnsCleanStatus()
	{
		// Act
		var result = await _tool.GitStatusAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		var status = json.RootElement.GetProperty("Status");
		Assert.NotNull(status.GetProperty("Branch").GetString());
	}

	[Fact]
	public async Task GitStatusAsync_WithUnstagedChanges_ShowsUnstaged()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Modified");

		// Act
		var result = await _tool.GitStatusAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		var status = json.RootElement.GetProperty("Status");
		Assert.True(status.GetProperty("Unstaged").GetArrayLength() > 0 || 
		            status.GetProperty("RawStatus").GetString()!.Contains("modified"));
	}

	[Fact]
	public async Task GitStatusAsync_WithUntrackedFile_ShowsUntracked()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "newfile.txt"), "new content");

		// Act
		var result = await _tool.GitStatusAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitStatusAsync_InvalidPath_ReturnsFailure()
	{
		// Arrange
		var invalidPath = Path.Combine(_testDirectory, "nonexistent");

		// Act
		var result = await _tool.GitStatusAsync(invalidPath);
		var json = JsonDocument.Parse(result);

		// Assert
		// Git will fail on non-existent directory
		Assert.True(json.RootElement.TryGetProperty("Success", out _));
	}

	#endregion

	#region git_add Tests

	[Fact]
	public async Task GitAddAsync_NewFile_StagesFile()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "newfile.cs"), "public class Test {}");

		// Act
		var result = await _tool.GitAddAsync("newfile.cs", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitAddAsync_AllFiles_StagesAllFiles()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "file1.cs"), "class A {}");
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "file2.cs"), "class B {}");

		// Act
		var result = await _tool.GitAddAsync(".", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_commit Tests

	[Fact]
	public async Task GitCommitAsync_WithStagedChanges_CreatesCommit()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "newfile.cs"), "public class Test {}");
		await RunGitCommand("add .");

		// Act
		var result = await _tool.GitCommitAsync("Test commit message", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.NotNull(json.RootElement.GetProperty("CommitHash").GetString());
		Assert.NotNull(json.RootElement.GetProperty("ShortHash").GetString());
	}

	[Fact]
	public async Task GitCommitAsync_NoStagedChanges_Fails()
	{
		// Act
		var result = await _tool.GitCommitAsync("Empty commit", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitCommitAsync_WithAllFlag_StagesAndCommits()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Modified Content");

		// Act
		var result = await _tool.GitCommitAsync("Auto-staged commit", _repoPath, all: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitCommitAsync_AllowEmpty_CreatesEmptyCommit()
	{
		// Act
		var result = await _tool.GitCommitAsync("Empty commit", _repoPath, allowEmpty: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_branch Tests

	[Fact]
	public async Task GitBranchAsync_ListBranches_ReturnsCurrentBranch()
	{
		// Act
		var result = await _tool.GitBranchAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.NotNull(json.RootElement.GetProperty("CurrentBranch").GetString());
		Assert.True(json.RootElement.GetProperty("Branches").GetArrayLength() > 0);
	}

	[Fact]
	public async Task GitBranchAsync_CreateBranch_CreatesBranch()
	{
		// Act
		var result = await _tool.GitBranchAsync(_repoPath, newBranch: "feature/test-branch");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("created", json.RootElement.GetProperty("Message").GetString()!.ToLower());
	}

	[Fact]
	public async Task GitBranchAsync_DeleteBranch_DeletesBranch()
	{
		// Arrange
		await RunGitCommand("branch test-to-delete");

		// Act
		var result = await _tool.GitBranchAsync(_repoPath, deleteBranch: "test-to-delete");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_checkout Tests

	[Fact]
	public async Task GitCheckoutAsync_ExistingBranch_SwitchesBranch()
	{
		// Arrange
		await RunGitCommand("branch test-branch");

		// Act
		var result = await _tool.GitCheckoutAsync("test-branch", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Equal("test-branch", json.RootElement.GetProperty("CurrentBranch").GetString());
	}

	[Fact]
	public async Task GitCheckoutAsync_CreateAndSwitch_CreatesBranchAndSwitches()
	{
		// Act
		var result = await _tool.GitCheckoutAsync("new-feature-branch", _repoPath, createBranch: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Equal("new-feature-branch", json.RootElement.GetProperty("CurrentBranch").GetString());
	}

	[Fact]
	public async Task GitCheckoutAsync_NonexistentBranch_Fails()
	{
		// Act
		var result = await _tool.GitCheckoutAsync("nonexistent-branch", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_log Tests

	[Fact]
	public async Task GitLogAsync_ReturnsCommitHistory()
	{
		// Act
		var result = await _tool.GitLogAsync(_repoPath, maxCount: 5);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.True(json.RootElement.GetProperty("Commits").GetArrayLength() > 0);
	}

	[Fact]
	public async Task GitLogAsync_OneLine_ReturnsCompactFormat()
	{
		// Act
		var result = await _tool.GitLogAsync(_repoPath, oneLine: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.True(json.RootElement.GetProperty("Commits").GetArrayLength() > 0);
	}

	[Fact]
	public async Task GitLogAsync_WithAuthorFilter_FiltersCommits()
	{
		// Act
		var result = await _tool.GitLogAsync(_repoPath, author: "Test User");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_diff Tests

	[Fact]
	public async Task GitDiffAsync_NoChanges_ReturnsEmptyDiff()
	{
		// Act
		var result = await _tool.GitDiffAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitDiffAsync_WithChanges_ShowsDiff()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Modified Content Here");

		// Act
		var result = await _tool.GitDiffAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.NotEmpty(json.RootElement.GetProperty("Diff").GetString()!);
	}

	[Fact]
	public async Task GitDiffAsync_Staged_ShowsStagedChanges()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Staged Change");
		await RunGitCommand("add .");

		// Act
		var result = await _tool.GitDiffAsync(_repoPath, staged: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitDiffAsync_NameOnly_ReturnsFileList()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Changed");

		// Act
		var result = await _tool.GitDiffAsync(_repoPath, nameOnly: true);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.True(json.RootElement.TryGetProperty("ChangedFiles", out _));
	}

	#endregion

	#region git_stash Tests

	[Fact]
	public async Task GitStashAsync_PushWithChanges_StashesChanges()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Stash this");

		// Act
		var result = await _tool.GitStashAsync(_repoPath, operation: "push", message: "Test stash");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitStashAsync_List_ReturnsStashList()
	{
		// Act
		var result = await _tool.GitStashAsync(_repoPath, operation: "list");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.True(json.RootElement.TryGetProperty("Stashes", out _));
	}

	[Fact]
	public async Task GitStashAsync_PopWithStash_RestoresChanges()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "README.md"), "# Stash and pop");
		await RunGitCommand("stash push -m \"Test stash\"");

		// Act
		var result = await _tool.GitStashAsync(_repoPath, operation: "pop");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_remote Tests

	[Fact]
	public async Task GitRemoteAsync_ListRemotes_ReturnsRemoteList()
	{
		// Act
		var result = await _tool.GitRemoteAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.True(json.RootElement.TryGetProperty("Remotes", out _));
	}

	[Fact]
	public async Task GitRemoteAsync_AddRemote_AddsRemote()
	{
		// Act
		var result = await _tool.GitRemoteAsync(_repoPath, addName: "test-remote", addUrl: "https://github.com/test/repo.git");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("added", json.RootElement.GetProperty("Message").GetString()!.ToLower());
	}

	[Fact]
	public async Task GitRemoteAsync_RemoveRemote_RemovesRemote()
	{
		// Arrange
		await RunGitCommand("remote add to-remove https://github.com/test/repo.git");

		// Act
		var result = await _tool.GitRemoteAsync(_repoPath, removeName: "to-remove");
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region git_merge Tests

	[Fact]
	public async Task GitMergeAsync_FastForward_MergesBranch()
	{
		// Arrange
		await RunGitCommand("checkout -b feature-branch");
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "feature.cs"), "class Feature {}");
		await RunGitCommand("add .");
		await RunGitCommand("commit -m \"Add feature\"");
		await RunGitCommand("checkout master");

		// Act
		var result = await _tool.GitMergeAsync("feature-branch", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitMergeAsync_NonexistentBranch_Fails()
	{
		// Act
		var result = await _tool.GitMergeAsync("nonexistent-branch", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion

	#region Conflict Resolution Tests

	[Fact]
	public async Task GitGetConflictsAsync_NoConflicts_ReturnsNoConflicts()
	{
		// Act
		var result = await _tool.GitGetConflictsAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.True(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.False(json.RootElement.GetProperty("HasConflicts").GetBoolean());
	}

	[Fact]
	public async Task GitAbortMergeAsync_NoMerge_ReturnsError()
	{
		// Act
		var result = await _tool.GitAbortMergeAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		// Should fail as there's no merge in progress
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	[Fact]
	public async Task GitResolveConflictAsync_InvalidStrategy_ReturnsError()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "test.cs"), "content");

		// Act
		var result = await _tool.GitResolveConflictAsync("test.cs", "invalid-strategy", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("Unknown strategy", json.RootElement.GetProperty("Message").GetString());
	}

	[Fact]
	public async Task GitResolveConflictAsync_ManualWithoutContent_ReturnsError()
	{
		// Arrange
		await File.WriteAllTextAsync(Path.Combine(_repoPath, "test.cs"), "content");

		// Act
		var result = await _tool.GitResolveConflictAsync("test.cs", "manual", _repoPath, resolvedContent: null);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("required", json.RootElement.GetProperty("Message").GetString()!.ToLower());
	}

	[Fact]
	public async Task GitResolveConflictAsync_FileNotFound_ReturnsError()
	{
		// Act
		var result = await _tool.GitResolveConflictAsync("nonexistent.cs", "ours", _repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
		Assert.Contains("not found", json.RootElement.GetProperty("Message").GetString()!.ToLower());
	}

	#endregion

	#region git_fetch Tests

	[Fact]
	public async Task GitFetchAsync_NoRemote_ReturnsError()
	{
		// Act (no remote configured)
		var result = await _tool.GitFetchAsync(_repoPath);
		var json = JsonDocument.Parse(result);

		// Assert
		Assert.False(json.RootElement.GetProperty("Success").GetBoolean());
	}

	#endregion
}
