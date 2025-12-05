using CodeEngineerMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


internal class Program
{
	private static async Task Main(string[] args)
	{

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

		// 1. Get the environment variable
		var SOLUTION_PATH = Environment.GetEnvironmentVariable("SOLUTION_PATH");

		Console.WriteLine("SOLUTION_PATH ", SOLUTION_PATH);

		if (string.IsNullOrEmpty(SOLUTION_PATH))
		{
			Console.Error.WriteLine("Error: SOLUTION_PATH environment variable is not set.");
			// Optionally, throw an exception or exit gracefully
			return;
		}

		// 2. Resolve the service using the built application's service provider
		//    Use a 'using' statement to ensure the scope is disposed if you weren't using the root scope
		var workspaceService = app.Services.GetRequiredService<IRoslynWorkspaceService>();

		// 3. Call the necessary loading method
		Console.WriteLine($"Attempting to load solution from: {SOLUTION_PATH}");
		await workspaceService.LoadSolutionAsync(SOLUTION_PATH);

		await app.RunAsync();
	}
}
