using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeEngineerMcp.Tools;

[McpServerToolType]
public partial class GitTool
{
	private readonly ILogger<GitTool> _logger;

	public GitTool(ILogger<GitTool> logger)
	{
		_logger = logger;
	}

	#region Core Git Operations

	[McpServerTool(Name = "git_status")]
	[Description("Get the current status of the Git repository including staged, unstaged, and untracked files.")]
	public async Task<string> GitStatusAsync(
		[Description("Path to the Git repository. Defaults to current directory.")]
		string? repoPath = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);
			
			// Get status with porcelain format for parsing
			var statusResult = await RunGitCommandAsync("status --porcelain=v2 --branch", workingDir, ct);
			var statusNormal = await RunGitCommandAsync("status", workingDir, ct);

			if (!statusResult.Success)
				return FormatError("git status", statusResult);

			var status = ParseGitStatus(statusResult.Output);
			status.RawStatus = statusNormal.Output;

			return JsonSerializer.Serialize(new
			{
				Success = true,
				Status = status
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git status", ex);
		}
	}

	[McpServerTool(Name = "git_pull")]
	[Description("Pull changes from remote repository. Supports rebase and fast-forward options.")]
	public async Task<string> GitPullAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Remote name. Default: origin")]
		string remote = "origin",

		[Description("Branch name. If not specified, pulls current branch.")]
		string? branch = null,

		[Description("Use rebase instead of merge. Default: false")]
		bool rebase = false,

		[Description("Only allow fast-forward merges. Default: false")]
		bool fastForwardOnly = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("pull");
			
			if (rebase)
				args.Append(" --rebase");
			
			if (fastForwardOnly)
				args.Append(" --ff-only");

			args.Append($" {remote}");
			
			if (!string.IsNullOrWhiteSpace(branch))
				args.Append($" {branch}");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			// Check for conflicts
			if (result.Output.Contains("CONFLICT") || result.Error.Contains("CONFLICT"))
			{
				var conflicts = await GetConflictedFilesAsync(workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = false,
					HasConflicts = true,
					Message = "Pull completed with conflicts that need to be resolved",
					ConflictedFiles = conflicts,
					Output = result.Output,
					Suggestion = "Use git_resolve_conflict or git_abort_merge to handle conflicts"
				}, JsonOptions);
			}

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Pull completed successfully" : "Pull failed",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git pull", ex);
		}
	}

	[McpServerTool(Name = "git_push")]
	[Description("Push commits to remote repository.")]
	public async Task<string> GitPushAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Remote name. Default: origin")]
		string remote = "origin",

		[Description("Branch name. If not specified, pushes current branch.")]
		string? branch = null,

		[Description("Force push (use with caution!). Default: false")]
		bool force = false,

		[Description("Set upstream tracking. Default: false")]
		bool setUpstream = false,

		[Description("Push tags along with commits. Default: false")]
		bool tags = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("push");
			
			if (force)
				args.Append(" --force");
			
			if (setUpstream)
				args.Append(" --set-upstream");
			
			if (tags)
				args.Append(" --tags");

			args.Append($" {remote}");
			
			if (!string.IsNullOrWhiteSpace(branch))
				args.Append($" {branch}");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Push completed successfully" : "Push failed",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git push", ex);
		}
	}

	[McpServerTool(Name = "git_commit")]
	[Description("Create a new commit with staged changes.")]
	public async Task<string> GitCommitAsync(
		[Description("Commit message")]
		string message,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Stage all modified and deleted files before committing. Default: false")]
		bool all = false,

		[Description("Amend the previous commit. Default: false")]
		bool amend = false,

		[Description("Allow empty commit. Default: false")]
		bool allowEmpty = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("commit");
			
			if (all)
				args.Append(" -a");
			
			if (amend)
				args.Append(" --amend");
			
			if (allowEmpty)
				args.Append(" --allow-empty");

			// Escape the message properly
			var escapedMessage = message.Replace("\"", "\\\"");
			args.Append($" -m \"{escapedMessage}\"");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			if (result.Success)
			{
				// Get the commit hash
				var hashResult = await RunGitCommandAsync("rev-parse HEAD", workingDir, ct);
				var shortHashResult = await RunGitCommandAsync("rev-parse --short HEAD", workingDir, ct);

				return JsonSerializer.Serialize(new
				{
					Success = true,
					Message = "Commit created successfully",
					CommitHash = hashResult.Output.Trim(),
					ShortHash = shortHashResult.Output.Trim(),
					Output = result.Output
				}, JsonOptions);
			}

			return JsonSerializer.Serialize(new
			{
				Success = false,
				Message = "Commit failed",
				Output = result.Output,
				Errors = result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git commit", ex);
		}
	}

	[McpServerTool(Name = "git_add")]
	[Description("Stage files for commit.")]
	public async Task<string> GitAddAsync(
		[Description("Files or patterns to stage. Use '.' for all files.")]
		string files,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Also stage file deletions when using wildcards. Default: true")]
		bool includeDeleted = true,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = includeDeleted ? $"add -A {files}" : $"add {files}";
			var result = await RunGitCommandAsync(args, workingDir, ct);

			// Get updated status
			var statusResult = await RunGitCommandAsync("status --porcelain", workingDir, ct);
			var stagedFiles = statusResult.Output
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Where(line => line.Length >= 2 && line[0] != ' ' && line[0] != '?')
				.Select(line => line[3..].Trim())
				.ToList();

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Files staged successfully" : "Failed to stage files",
				StagedFiles = stagedFiles,
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git add", ex);
		}
	}

	#endregion

	#region Branch Operations

	[McpServerTool(Name = "git_branch")]
	[Description("List, create, or delete branches.")]
	public async Task<string> GitBranchAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Name for new branch (leave empty to list branches)")]
		string? newBranch = null,

		[Description("Delete branch by name")]
		string? deleteBranch = null,

		[Description("Force delete branch. Default: false")]
		bool force = false,

		[Description("Include remote branches in listing. Default: false")]
		bool includeRemote = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			// Delete branch
			if (!string.IsNullOrWhiteSpace(deleteBranch))
			{
				var deleteFlag = force ? "-D" : "-d";
				var result = await RunGitCommandAsync($"branch {deleteFlag} {deleteBranch}", workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					Message = result.Success ? $"Branch '{deleteBranch}' deleted" : "Failed to delete branch",
					Output = result.Output,
					Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
				}, JsonOptions);
			}

			// Create branch
			if (!string.IsNullOrWhiteSpace(newBranch))
			{
				var result = await RunGitCommandAsync($"branch {newBranch}", workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					Message = result.Success ? $"Branch '{newBranch}' created" : "Failed to create branch",
					Output = result.Output,
					Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
				}, JsonOptions);
			}

			// List branches
			var listArgs = includeRemote ? "branch -a" : "branch";
			var listResult = await RunGitCommandAsync(listArgs, workingDir, ct);
			
			var branches = listResult.Output
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(b => new
				{
					Name = b.TrimStart('*', ' ').Trim(),
					IsCurrent = b.StartsWith('*'),
					IsRemote = b.Contains("remotes/")
				})
				.ToList();

			var currentBranch = branches.FirstOrDefault(b => b.IsCurrent)?.Name ?? "";

			return JsonSerializer.Serialize(new
			{
				Success = listResult.Success,
				CurrentBranch = currentBranch,
				Branches = branches
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git branch", ex);
		}
	}

	[McpServerTool(Name = "git_checkout")]
	[Description("Switch branches or restore files.")]
	public async Task<string> GitCheckoutAsync(
		[Description("Branch name or commit to checkout")]
		string target,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Create new branch and checkout. Default: false")]
		bool createBranch = false,

		[Description("Specific files to checkout (restore). Leave empty for branch checkout.")]
		string? files = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("checkout");
			
			if (createBranch)
				args.Append(" -b");

			args.Append($" {target}");

			if (!string.IsNullOrWhiteSpace(files))
				args.Append($" -- {files}");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			// Get current branch after checkout
			var branchResult = await RunGitCommandAsync("branch --show-current", workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? $"Checked out '{target}'" : "Checkout failed",
				CurrentBranch = branchResult.Output.Trim(),
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git checkout", ex);
		}
	}

	[McpServerTool(Name = "git_merge")]
	[Description("Merge a branch into the current branch.")]
	public async Task<string> GitMergeAsync(
		[Description("Branch to merge into current branch")]
		string branch,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Create a merge commit even for fast-forward. Default: false")]
		bool noFastForward = false,

		[Description("Abort if conflicts occur (don't leave partial merge). Default: false")]
		bool abortOnConflict = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("merge");
			
			if (noFastForward)
				args.Append(" --no-ff");

			args.Append($" {branch}");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			// Check for conflicts
			if (result.Output.Contains("CONFLICT") || result.Error.Contains("CONFLICT"))
			{
				if (abortOnConflict)
				{
					await RunGitCommandAsync("merge --abort", workingDir, ct);
					return JsonSerializer.Serialize(new
					{
						Success = false,
						Message = "Merge aborted due to conflicts (abortOnConflict=true)",
						HadConflicts = true
					}, JsonOptions);
				}

				var conflicts = await GetConflictedFilesAsync(workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = false,
					HasConflicts = true,
					Message = "Merge has conflicts that need to be resolved",
					ConflictedFiles = conflicts,
					Output = result.Output,
					Suggestion = "Use git_get_conflicts to see conflict details, then git_resolve_conflict to fix them"
				}, JsonOptions);
			}

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? $"Successfully merged '{branch}'" : "Merge failed",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git merge", ex);
		}
	}

	#endregion

	#region Conflict Resolution

	[McpServerTool(Name = "git_get_conflicts")]
	[Description("Get detailed information about merge conflicts including the conflicting content.")]
	public async Task<string> GitGetConflictsAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var conflicts = await GetConflictedFilesAsync(workingDir, ct);

			if (conflicts.Count == 0)
			{
				return JsonSerializer.Serialize(new
				{
					Success = true,
					HasConflicts = false,
					Message = "No conflicts found"
				}, JsonOptions);
			}

			var conflictDetails = new List<ConflictDetail>();

			foreach (var file in conflicts)
			{
				var filePath = Path.Combine(workingDir, file);
				if (File.Exists(filePath))
				{
					var content = await File.ReadAllTextAsync(filePath, ct);
					var sections = ParseConflictSections(content);
					
					conflictDetails.Add(new ConflictDetail
					{
						FilePath = file,
						ConflictSections = sections,
						FullContent = content.Length > 5000 ? content[..5000] + "\n... (truncated)" : content
					});
				}
			}

			return JsonSerializer.Serialize(new
			{
				Success = true,
				HasConflicts = true,
				ConflictCount = conflicts.Count,
				Conflicts = conflictDetails,
				Suggestion = "Use git_resolve_conflict to resolve each file, or git_abort_merge to cancel"
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git get conflicts", ex);
		}
	}

	[McpServerTool(Name = "git_resolve_conflict")]
	[Description("Resolve a conflict by choosing a resolution strategy or providing resolved content.")]
	public async Task<string> GitResolveConflictAsync(
		[Description("Path to the conflicted file (relative to repo root)")]
		string filePath,

		[Description("Resolution strategy: 'ours' (keep current branch), 'theirs' (keep incoming), 'manual' (provide content)")]
		string strategy,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Resolved content when using 'manual' strategy")]
		string? resolvedContent = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);
			var fullFilePath = Path.Combine(workingDir, filePath);

			if (!File.Exists(fullFilePath))
			{
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = $"File not found: {filePath}"
				}, JsonOptions);
			}

			switch (strategy.ToLowerInvariant())
			{
				case "ours":
					var oursResult = await RunGitCommandAsync($"checkout --ours \"{filePath}\"", workingDir, ct);
					if (oursResult.Success)
						await RunGitCommandAsync($"add \"{filePath}\"", workingDir, ct);
					return JsonSerializer.Serialize(new
					{
						Success = oursResult.Success,
						Message = oursResult.Success ? $"Resolved '{filePath}' using 'ours' (current branch)" : "Failed to resolve",
						Output = oursResult.Output,
						Errors = string.IsNullOrWhiteSpace(oursResult.Error) ? null : oursResult.Error
					}, JsonOptions);

				case "theirs":
					var theirsResult = await RunGitCommandAsync($"checkout --theirs \"{filePath}\"", workingDir, ct);
					if (theirsResult.Success)
						await RunGitCommandAsync($"add \"{filePath}\"", workingDir, ct);
					return JsonSerializer.Serialize(new
					{
						Success = theirsResult.Success,
						Message = theirsResult.Success ? $"Resolved '{filePath}' using 'theirs' (incoming branch)" : "Failed to resolve",
						Output = theirsResult.Output,
						Errors = string.IsNullOrWhiteSpace(theirsResult.Error) ? null : theirsResult.Error
					}, JsonOptions);

				case "manual":
					if (string.IsNullOrEmpty(resolvedContent))
					{
						return JsonSerializer.Serialize(new
						{
							Success = false,
							Message = "resolvedContent is required when using 'manual' strategy"
						}, JsonOptions);
					}

					await File.WriteAllTextAsync(fullFilePath, resolvedContent, ct);
					await RunGitCommandAsync($"add \"{filePath}\"", workingDir, ct);
					
					return JsonSerializer.Serialize(new
					{
						Success = true,
						Message = $"Resolved '{filePath}' with provided content and staged"
					}, JsonOptions);

				default:
					return JsonSerializer.Serialize(new
					{
						Success = false,
						Message = $"Unknown strategy: {strategy}. Use 'ours', 'theirs', or 'manual'"
					}, JsonOptions);
			}
		}
		catch (Exception ex)
		{
			return FormatException("git resolve conflict", ex);
		}
	}

	[McpServerTool(Name = "git_abort_merge")]
	[Description("Abort the current merge operation and restore the pre-merge state.")]
	public async Task<string> GitAbortMergeAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);
			var result = await RunGitCommandAsync("merge --abort", workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Merge aborted successfully" : "Failed to abort merge",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git abort merge", ex);
		}
	}

	[McpServerTool(Name = "git_continue_merge")]
	[Description("Continue the merge after resolving all conflicts.")]
	public async Task<string> GitContinueMergeAsync(
		[Description("Commit message for the merge")]
		string? message = null,

		[Description("Path to the Git repository")]
		string? repoPath = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			// Check if there are still conflicts
			var conflicts = await GetConflictedFilesAsync(workingDir, ct);
			if (conflicts.Count > 0)
			{
				return JsonSerializer.Serialize(new
				{
					Success = false,
					Message = "Cannot continue: unresolved conflicts remain",
					ConflictedFiles = conflicts
				}, JsonOptions);
			}

			// Complete the merge with a commit
			var args = string.IsNullOrWhiteSpace(message) 
				? "commit --no-edit" 
				: $"commit -m \"{message.Replace("\"", "\\\"")}\"";

			var result = await RunGitCommandAsync(args, workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Merge completed successfully" : "Failed to complete merge",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git continue merge", ex);
		}
	}

	#endregion

	#region History & Diff

	[McpServerTool(Name = "git_log")]
	[Description("Show commit history.")]
	public async Task<string> GitLogAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Maximum number of commits to show. Default: 10")]
		int maxCount = 10,

		[Description("Show commits for specific file or path")]
		string? path = null,

		[Description("Show commits by author")]
		string? author = null,

		[Description("Show commits since date (e.g., '2024-01-01' or '1 week ago')")]
		string? since = null,

		[Description("One-line format for compact output. Default: false")]
		bool oneLine = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var format = oneLine 
				? "--oneline" 
				: "--format=%H|%h|%an|%ae|%ai|%s";

			var args = new StringBuilder($"log -{maxCount} {format}");

			if (!string.IsNullOrWhiteSpace(author))
				args.Append($" --author=\"{author}\"");

			if (!string.IsNullOrWhiteSpace(since))
				args.Append($" --since=\"{since}\"");

			if (!string.IsNullOrWhiteSpace(path))
				args.Append($" -- \"{path}\"");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			if (oneLine)
			{
				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					Commits = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				}, JsonOptions);
			}

			var commits = result.Output
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(line =>
				{
					var parts = line.Split('|');
					return new
					{
						Hash = parts.ElementAtOrDefault(0) ?? "",
						ShortHash = parts.ElementAtOrDefault(1) ?? "",
						Author = parts.ElementAtOrDefault(2) ?? "",
						Email = parts.ElementAtOrDefault(3) ?? "",
						Date = parts.ElementAtOrDefault(4) ?? "",
						Message = parts.ElementAtOrDefault(5) ?? ""
					};
				})
				.ToList();

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				CommitCount = commits.Count,
				Commits = commits
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git log", ex);
		}
	}

	[McpServerTool(Name = "git_diff")]
	[Description("Show changes between commits, working tree, and staging area.")]
	public async Task<string> GitDiffAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Compare staged changes (vs HEAD). Default: false (shows unstaged changes)")]
		bool staged = false,

		[Description("Specific file to diff")]
		string? file = null,

		[Description("First commit/branch for comparison")]
		string? from = null,

		[Description("Second commit/branch for comparison")]
		string? to = null,

		[Description("Show only file names (stat). Default: false")]
		bool nameOnly = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("diff");

			if (staged)
				args.Append(" --staged");

			if (nameOnly)
				args.Append(" --name-only");

			if (!string.IsNullOrWhiteSpace(from))
			{
				args.Append($" {from}");
				if (!string.IsNullOrWhiteSpace(to))
					args.Append($" {to}");
			}

			if (!string.IsNullOrWhiteSpace(file))
				args.Append($" -- \"{file}\"");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			if (nameOnly)
			{
				var files = result.Output
					.Split('\n', StringSplitOptions.RemoveEmptyEntries)
					.ToList();

				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					ChangedFiles = files,
					FileCount = files.Count
				}, JsonOptions);
			}

			// Also get stat summary
			var statResult = await RunGitCommandAsync(args.ToString().Replace("diff", "diff --stat"), workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Diff = result.Output.Length > 10000 
					? result.Output[..10000] + "\n... (truncated, use file parameter for specific file)" 
					: result.Output,
				Stats = statResult.Output
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git diff", ex);
		}
	}

	#endregion

	#region Remote Operations

	[McpServerTool(Name = "git_remote")]
	[Description("Manage remote repositories.")]
	public async Task<string> GitRemoteAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Add remote with this name")]
		string? addName = null,

		[Description("URL for new remote")]
		string? addUrl = null,

		[Description("Remove remote by name")]
		string? removeName = null,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			// Add remote
			if (!string.IsNullOrWhiteSpace(addName) && !string.IsNullOrWhiteSpace(addUrl))
			{
				var result = await RunGitCommandAsync($"remote add {addName} {addUrl}", workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					Message = result.Success ? $"Remote '{addName}' added" : "Failed to add remote",
					Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
				}, JsonOptions);
			}

			// Remove remote
			if (!string.IsNullOrWhiteSpace(removeName))
			{
				var result = await RunGitCommandAsync($"remote remove {removeName}", workingDir, ct);
				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					Message = result.Success ? $"Remote '{removeName}' removed" : "Failed to remove remote",
					Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
				}, JsonOptions);
			}

			// List remotes
			var listResult = await RunGitCommandAsync("remote -v", workingDir, ct);
			var remotes = listResult.Output
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(line =>
				{
					var match = RemoteRegex().Match(line);
					return match.Success ? new
					{
						Name = match.Groups["name"].Value,
						Url = match.Groups["url"].Value,
						Type = match.Groups["type"].Value
					} : null;
				})
				.Where(r => r != null)
				.DistinctBy(r => r!.Name + r.Type)
				.ToList();

			return JsonSerializer.Serialize(new
			{
				Success = listResult.Success,
				Remotes = remotes
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git remote", ex);
		}
	}

	[McpServerTool(Name = "git_fetch")]
	[Description("Download objects and refs from remote repository.")]
	public async Task<string> GitFetchAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Remote name. Default: origin")]
		string remote = "origin",

		[Description("Fetch all remotes. Default: false")]
		bool all = false,

		[Description("Prune deleted remote branches. Default: false")]
		bool prune = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = new StringBuilder("fetch");
			
			if (all)
				args.Append(" --all");
			else
				args.Append($" {remote}");
			
			if (prune)
				args.Append(" --prune");

			var result = await RunGitCommandAsync(args.ToString(), workingDir, ct);

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? "Fetch completed" : "Fetch failed",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git fetch", ex);
		}
	}

	#endregion

	#region Stash Operations

	[McpServerTool(Name = "git_stash")]
	[Description("Stash or restore uncommitted changes.")]
	public async Task<string> GitStashAsync(
		[Description("Path to the Git repository")]
		string? repoPath = null,

		[Description("Operation: 'push' (save), 'pop' (restore & remove), 'apply' (restore & keep), 'list', 'drop', 'clear'")]
		string operation = "push",

		[Description("Message for stash (when pushing)")]
		string? message = null,

		[Description("Stash index for pop/apply/drop (e.g., 'stash@{0}')")]
		string? stashRef = null,

		[Description("Include untracked files when pushing. Default: false")]
		bool includeUntracked = false,

		CancellationToken ct = default)
	{
		try
		{
			var workingDir = GetWorkingDirectory(repoPath);

			var args = operation.ToLowerInvariant() switch
			{
				"push" => BuildStashPushArgs(message, includeUntracked),
				"pop" => $"stash pop {stashRef ?? ""}".Trim(),
				"apply" => $"stash apply {stashRef ?? ""}".Trim(),
				"list" => "stash list",
				"drop" => $"stash drop {stashRef ?? ""}".Trim(),
				"clear" => "stash clear",
				_ => throw new ArgumentException($"Unknown stash operation: {operation}")
			};

			var result = await RunGitCommandAsync(args, workingDir, ct);

			if (operation.Equals("list", StringComparison.OrdinalIgnoreCase))
			{
				var stashes = result.Output
					.Split('\n', StringSplitOptions.RemoveEmptyEntries)
					.Select(line =>
					{
						var match = StashRegex().Match(line);
						return match.Success ? new
						{
							Ref = match.Groups["ref"].Value,
							Branch = match.Groups["branch"].Value,
							Message = match.Groups["message"].Value
						} : null;
					})
					.Where(s => s != null)
					.ToList();

				return JsonSerializer.Serialize(new
				{
					Success = result.Success,
					StashCount = stashes.Count,
					Stashes = stashes
				}, JsonOptions);
			}

			return JsonSerializer.Serialize(new
			{
				Success = result.Success,
				Message = result.Success ? $"Stash {operation} completed" : $"Stash {operation} failed",
				Output = result.Output,
				Errors = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error
			}, JsonOptions);
		}
		catch (Exception ex)
		{
			return FormatException("git stash", ex);
		}
	}

	private static string BuildStashPushArgs(string? message, bool includeUntracked)
	{
		var args = new StringBuilder("stash push");
		if (includeUntracked)
			args.Append(" -u");
		if (!string.IsNullOrWhiteSpace(message))
			args.Append($" -m \"{message.Replace("\"", "\\\"")}\"");
		return args.ToString();
	}

	#endregion

	#region Helper Methods

	private static string GetWorkingDirectory(string? repoPath)
	{
		if (string.IsNullOrWhiteSpace(repoPath))
			return Environment.CurrentDirectory;

		var fullPath = Path.GetFullPath(repoPath);
		
		if (File.Exists(fullPath))
			return Path.GetDirectoryName(fullPath) ?? fullPath;
		
		return fullPath;
	}

	private async Task<GitCommandResult> RunGitCommandAsync(string arguments, string workingDir, CancellationToken ct)
	{
		_logger.LogDebug("Running: git {Args} in {Dir}", arguments, workingDir);

		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "git",
				Arguments = arguments,
				WorkingDirectory = workingDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		var output = new StringBuilder();
		var error = new StringBuilder();

		process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await process.WaitForExitAsync(ct);

		return new GitCommandResult
		{
			Success = process.ExitCode == 0,
			ExitCode = process.ExitCode,
			Output = output.ToString().Trim(),
			Error = error.ToString().Trim()
		};
	}

	private async Task<List<string>> GetConflictedFilesAsync(string workingDir, CancellationToken ct)
	{
		var result = await RunGitCommandAsync("diff --name-only --diff-filter=U", workingDir, ct);
		return result.Output
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(f => f.Trim())
			.ToList();
	}

	private GitStatusInfo ParseGitStatus(string porcelainOutput)
	{
		var status = new GitStatusInfo();
		var lines = porcelainOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		foreach (var line in lines)
		{
			if (line.StartsWith("# branch.head"))
				status.Branch = line.Split(' ').Last();
			else if (line.StartsWith("# branch.upstream"))
				status.Upstream = line.Split(' ').Last();
			else if (line.StartsWith("# branch.ab"))
			{
				var match = BranchAbRegex().Match(line);
				if (match.Success)
				{
					status.Ahead = int.Parse(match.Groups["ahead"].Value);
					status.Behind = int.Parse(match.Groups["behind"].Value);
				}
			}
			else if (line.StartsWith("1 ") || line.StartsWith("2 "))
			{
				// Changed entry
				var parts = line.Split(' ');
				if (parts.Length >= 9)
				{
					var xy = parts[1];
					var path = parts[^1];

					if (xy[0] != '.')
						status.Staged.Add(new FileStatus { Path = path, Status = GetStatusName(xy[0]) });
					if (xy[1] != '.')
						status.Unstaged.Add(new FileStatus { Path = path, Status = GetStatusName(xy[1]) });
				}
			}
			else if (line.StartsWith("? "))
			{
				status.Untracked.Add(line[2..].Trim());
			}
			else if (line.StartsWith("u "))
			{
				// Unmerged (conflict)
				var parts = line.Split(' ');
				status.Conflicted.Add(parts[^1]);
			}
		}

		return status;
	}

	private static string GetStatusName(char code) => code switch
	{
		'M' => "Modified",
		'A' => "Added",
		'D' => "Deleted",
		'R' => "Renamed",
		'C' => "Copied",
		'U' => "Unmerged",
		_ => "Unknown"
	};

	private static List<ConflictSection> ParseConflictSections(string content)
	{
		var sections = new List<ConflictSection>();
		var matches = ConflictMarkerRegex().Matches(content);

		foreach (Match match in matches)
		{
			sections.Add(new ConflictSection
			{
				OursContent = match.Groups["ours"].Value.Trim(),
				TheirsContent = match.Groups["theirs"].Value.Trim(),
				StartIndex = match.Index,
				Length = match.Length
			});
		}

		return sections;
	}

	private static string FormatError(string operation, GitCommandResult result) =>
		JsonSerializer.Serialize(new
		{
			Success = false,
			Message = $"{operation} failed",
			Output = result.Output,
			Errors = result.Error
		}, JsonOptions);

	private static string FormatException(string operation, Exception ex) =>
		JsonSerializer.Serialize(new
		{
			Success = false,
			Message = $"Error in {operation}: {ex.Message}"
		}, JsonOptions);

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	#endregion

	#region Generated Regex

	[GeneratedRegex(@"#\s*branch\.ab\s*\+(?<ahead>\d+)\s*-(?<behind>\d+)")]
	private static partial Regex BranchAbRegex();

	[GeneratedRegex(@"(?<name>\S+)\s+(?<url>\S+)\s+\((?<type>fetch|push)\)")]
	private static partial Regex RemoteRegex();

	[GeneratedRegex(@"(?<ref>stash@\{\d+\}):\s*(?:WIP on|On)\s*(?<branch>[^:]+):\s*(?<message>.*)")]
	private static partial Regex StashRegex();

	[GeneratedRegex(@"<<<<<<<[^\n]*\n(?<ours>.*?)=======\n(?<theirs>.*?)>>>>>>>[^\n]*", RegexOptions.Singleline)]
	private static partial Regex ConflictMarkerRegex();

	#endregion
}

#region Models

internal class GitCommandResult
{
	public bool Success { get; set; }
	public int ExitCode { get; set; }
	public string Output { get; set; } = "";
	public string Error { get; set; } = "";
}

internal class GitStatusInfo
{
	public string Branch { get; set; } = "";
	public string? Upstream { get; set; }
	public int Ahead { get; set; }
	public int Behind { get; set; }
	public List<FileStatus> Staged { get; set; } = [];
	public List<FileStatus> Unstaged { get; set; } = [];
	public List<string> Untracked { get; set; } = [];
	public List<string> Conflicted { get; set; } = [];
	public string? RawStatus { get; set; }
	
	public bool IsClean => Staged.Count == 0 && Unstaged.Count == 0 && Untracked.Count == 0 && Conflicted.Count == 0;
}

internal class FileStatus
{
	public string Path { get; set; } = "";
	public string Status { get; set; } = "";
}

internal class ConflictDetail
{
	public string FilePath { get; set; } = "";
	public List<ConflictSection> ConflictSections { get; set; } = [];
	public string? FullContent { get; set; }
}

internal class ConflictSection
{
	public string OursContent { get; set; } = "";
	public string TheirsContent { get; set; } = "";
	public int StartIndex { get; set; }
	public int Length { get; set; }
}

#endregion
