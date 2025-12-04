namespace CodeEngineerMcp.Models;

public record ProjectDependency(
	string ProjectName,
	string ProjectPath,
	IReadOnlyList<string> ProjectReferences,
	IReadOnlyList<PackageReference> PackageReferences
);

public record PackageReference(string Name, string Version);
