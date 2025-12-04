using CodeEngineerMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddTransient<ICodeIndexService, CodeIndexService>();
builder.Services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>(); // Singleton for stateful workspace
builder.Services.AddTransient<IFileWriteService, FileWriteService>(); // New file write service

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
	options.LogToStandardErrorThreshold = LogLevel.Trace; // All logs to stderr
});

builder.Services
	.AddMcpServer()
	.WithStdioServerTransport()
	.WithToolsFromAssembly();

var app = builder.Build();

await app.RunAsync();
