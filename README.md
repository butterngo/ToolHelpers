# Code Engineer MCP Server

[![NuGet](https://img.shields.io/nuget/v/CodeEngineerMcp.svg)](https://www.nuget.org/packages/CodeEngineerMcp/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A Model Context Protocol (MCP) server for code analysis and manipulation, built with .NET 8 and Roslyn.

## Installation

### As a .NET Global Tool (Recommended)

```bash
dotnet tool install -g CodeEngineerMcp
```

### From Source

```bash
git clone https://github.com/yourusername/CodeEngineerMcp.git
cd CodeEngineerMcp
dotnet build
```

## Features

### Code Analysis Tools

| Tool | Description |
|------|-------------|
| `search_code` | Full-text or regex search across codebase |
| `get_file_content` | Read file contents with line numbers |
| `list_directory` | Browse folder structure as tree |
| `find_symbol` | Find symbol definitions (classes, methods, etc.) |
| `find_references` | Find all usages of a symbol |
| `analyze_dependencies` | Show project and package dependencies |
| `load_solution` | Load a .NET solution for Roslyn analysis |

### File Manipulation Tools

| Tool | Description |
|------|-------------|
| `create_file` | Create new files with auto-directory creation |
| `edit_file` | Replace content (str_replace style, unique match required) |
| `insert_lines` | Insert content at specific line number |
| `delete_lines` | Delete range of lines |
| `append_to_file` | Append content to end of file |
| `rename_file` | Rename or move files |
| `delete_file` | **Safe delete with reference checking** |

## Quick Start

### 1. Install the Tool

```bash
dotnet tool install -g CodeEngineerMcp
```

### 2. Configure Claude Desktop

Edit `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "code-engineer": {
      "command": "code-engineer-mcp",
      "env": {
        "SOLUTION_PATH": "C:\\path\\to\\your\\solution.sln"
      }
    }
  }
}
```

### 3. Start Using

Ask Claude to search code, find symbols, create files, and more!

## Configuration

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `SOLUTION_PATH` | Path to .sln file for Roslyn analysis | Optional |

### With Roslyn Analysis (Full Features)

```bash
# Windows
set SOLUTION_PATH=C:\path\to\your\solution.sln
code-engineer-mcp

# Linux/macOS
SOLUTION_PATH=/path/to/your/solution.sln code-engineer-mcp
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

### create_file

```
Create a new service class
- filePath: /path/to/NewService.cs
- content: "public class NewService { }"
```

## Architecture

```
┌─────────────────────────────────────────────┐
│           MCP Server (.NET 8)               │
├─────────────────────────────────────────────┤
│        Transport (stdio)                    │
├─────────────────────────────────────────────┤
│           Tool Handlers                     │
│  ┌─────────┐ ┌──────────┐ ┌──────────────┐  │
│  │ Search  │ │ Symbols  │ │ File Writes  │  │
│  └────┬────┘ └────┬─────┘ └──────┬───────┘  │
├───────┼───────────┼──────────────┼──────────┤
│       │    Services Layer        │          │
│  ┌────┴────────────┴─────────────┴────┐     │
│  │ CodeIndex │ Roslyn │ FileWrite     │     │
│  └────────────────────────────────────┘     │
└─────────────────────────────────────────────┘
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

### Packaging

```bash
dotnet pack -c Release
```

### Local Install (for testing)

```bash
dotnet tool install -g --add-source ./nupkg CodeEngineerMcp
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
    [McpServerTool(Name = "my_tool")]
    [Description("Description of what this tool does")]
    public async Task<string> MyToolAsync(
        [Description("Parameter description")] string param,
        CancellationToken ct = default)
    {
        // Implementation
        return "result";
    }
}
```

## License

MIT
