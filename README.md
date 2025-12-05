# Code Engineer MCP Server

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

A Model Context Protocol (MCP) server for code analysis, file manipulation, testing, and Git operations, built with .NET 8 and Roslyn.

## Installation

### From Source

```bash
git clone https://github.com/yourusername/CodeEngineerMcp.git
cd CodeEngineerMcp
dotnet build
```

## Features

### 📂 Code Analysis Tools

| Tool | Description |
|------|-------------|
| `search_code` | Full-text or regex search across codebase |
| `get_file_content` | Read file contents with line numbers |
| `list_directory` | Browse folder structure as tree |
| `find_symbol` | Find symbol definitions (classes, methods, etc.) |
| `find_references` | Find all usages of a symbol |
| `analyze_dependencies` | Show project and package dependencies |
| `load_solution` | Load a .NET solution for Roslyn analysis |

### 📝 File Manipulation Tools

| Tool | Description |
|------|-------------|
| `create_file` | Create new files with auto-directory creation |
| `edit_file` | Replace content (str_replace style, unique match required) |
| `insert_lines` | Insert content at specific line number |
| `delete_lines` | Delete range of lines |
| `append_to_file` | Append content to end of file |
| `rename_file` | Rename or move files |
| `delete_file` | **Safe delete with reference checking** |

### 🧪 Testing Tools

| Tool | Description |
|------|-------------|
| `run_tests` | Run .NET tests with filters, coverage, and detailed results |
| `run_specific_test` | Run a single test by fully qualified name |
| `list_tests` | Discover and list all available tests |

### 🔀 Git Tools

#### Core Operations
| Tool | Description |
|------|-------------|
| `git_status` | Get repo status (staged, unstaged, untracked, conflicts) |
| `git_pull` | Pull with rebase/ff-only options, auto-detects conflicts |
| `git_push` | Push with force/set-upstream/tags options |
| `git_commit` | Commit with -a/--amend/--allow-empty options |
| `git_add` | Stage files (supports patterns and deletions) |

#### Branch Operations
| Tool | Description |
|------|-------------|
| `git_branch` | List, create, or delete branches |
| `git_checkout` | Switch branches, create new branches, restore files |
| `git_merge` | Merge branches with conflict detection |

#### Conflict Resolution
| Tool | Description |
|------|-------------|
| `git_get_conflicts` | Get detailed conflict info with both sides' content |
| `git_resolve_conflict` | Resolve using `ours`, `theirs`, or `manual` strategy |
| `git_abort_merge` | Abort merge and restore pre-merge state |
| `git_continue_merge` | Complete merge after resolving conflicts |

#### History & Diff
| Tool | Description |
|------|-------------|
| `git_log` | Show commit history with filters (author, date, path) |
| `git_diff` | Show changes (staged/unstaged, between commits) |

#### Remote & Stash
| Tool | Description |
|------|-------------|
| `git_remote` | List, add, or remove remotes |
| `git_fetch` | Fetch from remotes with prune option |
| `git_stash` | Push/pop/apply/list/drop/clear stashes |

## Quick Start

### 1. Build the Project

```bash
cd CodeEngineerMcp
dotnet build
```

### 2. Test with MCP Inspector

Use the MCP Inspector to test and debug tools interactively:

```bash
npx @modelcontextprotocol/inspector dotnet run --no-build --no-launch-profile
```

This opens a web UI where you can:
- Browse all available tools
- Test tool invocations with custom parameters
- View JSON responses in real-time

### 3. Configure Claude Desktop

Edit `claude_desktop_config.json`:

**Windows:**
```json
{
  "mcpServers": {
    "code-engineer": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\CodeEngineerMcp\\CodeEngineerMcp.csproj", "--no-launch-profile"],
      "env": {
        "SOLUTION_PATH": "C:\\path\\to\\your\\solution.sln"
      }
    }
  }
}
```

**macOS/Linux:**
```json
{
  "mcpServers": {
    "code-engineer": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/CodeEngineerMcp/CodeEngineerMcp.csproj", "--no-launch-profile"],
      "env": {
        "SOLUTION_PATH": "/path/to/your/solution.sln"
      }
    }
  }
}
```

### 4. Start Using

Ask Claude to search code, run tests, manage Git, and more!

## Configuration

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `SOLUTION_PATH` | Path to .sln file for Roslyn analysis | Optional |

### With Roslyn Analysis (Full Features)

```bash
# Windows
set SOLUTION_PATH=C:\path\to\your\solution.sln
dotnet run --project CodeEngineerMcp

# Linux/macOS
SOLUTION_PATH=/path/to/your/solution.sln dotnet run --project CodeEngineerMcp
```

## Tool Examples

### search_code

```
Search for "IRepository" in C# files
- rootPath: /path/to/codebase
- query: IRepository
- filePattern: *.cs
```

### find_symbol

```
Find the definition of OrderService
- symbolName: OrderService
```

### delete_file (with reference checking)

```
Safely delete a file
- filePath: /path/to/file.cs
- force: false (default, checks references first)
```

### run_tests

```
Run all tests in a project
- projectPath: /path/to/Tests.csproj
- filter: "Category=Unit"
- collectCoverage: true
```

**Example Output:**
```json
{
  "Success": true,
  "Summary": {
    "TotalTests": 25,
    "PassedTests": 24,
    "FailedTests": 1,
    "Status": "❌ 1 test(s) failed",
    "FailedTestNames": ["MyNamespace.MyTest.ShouldPass"],
    "FailureDetails": {
      "MyNamespace.MyTest.ShouldPass": "Assert.Equal() Failure"
    }
  }
}
```

### git_status

```
Get repository status
- repoPath: /path/to/repo
```

**Example Output:**
```json
{
  "Success": true,
  "Status": {
    "Branch": "feature/new-feature",
    "Ahead": 2,
    "Behind": 0,
    "Staged": [{"Path": "file.cs", "Status": "Modified"}],
    "Untracked": ["new-file.txt"]
  }
}
```

### git_resolve_conflict

```
Resolve merge conflict
- filePath: "src/Service.cs"
- strategy: "theirs"  // or "ours" or "manual"
- resolvedContent: "..." // required if strategy is "manual"
```

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│              MCP Server (.NET 8)                         │
├──────────────────────────────────────────────────────────┤
│           Transport (stdio)                              │
├──────────────────────────────────────────────────────────┤
│                    Tool Handlers                         │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────────┐  │
│  │  Search  │ │ Symbols  │ │   Git    │ │   Tests     │  │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └──────┬──────┘  │
│       │            │            │              │         │
├───────┼────────────┼────────────┼──────────────┼─────────┤
│       │       Services Layer    │              │         │
│  ┌────┴────────────┴────────────┴──────────────┴────┐    │
│  │ CodeIndex │ Roslyn │ FileWrite │ Git │ DotnetCLI │    │
│  └──────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────┘
```

## Development

### Building

```bash
dotnet build
```

### Testing

```bash
dotnet test
```

### Debug with MCP Inspector

```bash
npx @modelcontextprotocol/inspector dotnet run --no-build --no-launch-profile
```

### Run Directly

```bash
dotnet run --project CodeEngineerMcp
```

## Extending

### Adding New Tools

1. Create a new class in the `Tools` folder
2. Decorate with `[McpServerToolType]`
3. Add methods with `[McpServerTool("tool_name")]`
4. Tools are auto-discovered via `WithToolsFromAssembly()`

Example:

```csharp
[McpServerToolType]
public class MyCustomTool
{
    private readonly ILogger<MyCustomTool> _logger;

    public MyCustomTool(ILogger<MyCustomTool> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "my_tool")]
    [Description("Description of what this tool does")]
    public async Task<string> MyToolAsync(
        [Description("Parameter description")] string param,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Running my_tool with {Param}", param);
        
        return JsonSerializer.Serialize(new
        {
            Success = true,
            Result = "your result"
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

## Workflows

### Git Feature Branch Workflow

```
1. git_checkout(target: "feature/my-feature", createBranch: true)
2. // Make changes
3. git_add(files: ".")
4. git_commit(message: "Add new feature")
5. git_push(setUpstream: true)
```

### Pull with Conflict Resolution

```
1. git_pull(repoPath: "/project")
   → Returns: HasConflicts: true

2. git_get_conflicts()
   → Returns: conflict details with ours/theirs content

3. git_resolve_conflict(filePath: "file.cs", strategy: "theirs")
   → Or: strategy: "manual", resolvedContent: "merged code"

4. git_continue_merge(message: "Resolved conflicts")
```

### Test-Driven Development

```
1. list_tests(projectPath: "Tests.csproj")
   → Discover available tests

2. run_specific_test(testName: "MyTest.ShouldWork")
   → Run single test during development

3. run_tests(projectPath: "Tests.csproj", collectCoverage: true)
   → Run all tests with coverage
```

## License

MIT
