# Verifying the pure F# core

Run `dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj` from the repository root. GitHub Actions executes the same command on push and pull requests. Only synthetic tests and small human-authored YAML examples are used. The ESAPI adapter needs an independent build against clinic-installed vendor assemblies.
