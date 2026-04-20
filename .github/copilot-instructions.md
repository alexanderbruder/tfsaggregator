# Copilot instructions for `tfsaggregator`

## Build, test, and lint commands

Use Windows paths and run from repository root.

```powershell
# Build solution against local Azure DevOps Server assemblies in .\ADO\
dotnet build .\tfs-aggregator-plugin.sln -c Debug-2025 -p:TfsVersion=2025 -p:Platform="Any CPU"

# Run full unit test project
dotnet test .\Aggregator.Core\UnitTests.Core\UnitTests.Core.csproj -c Debug-2025 -p:TfsVersion=2025 -p:Platform="Any CPU"

# Run a single test
dotnet test .\Aggregator.Core\UnitTests.Core\UnitTests.Core.csproj -c Debug-2025 -p:TfsVersion=2025 -p:Platform="Any CPU" --filter "FullyQualifiedName~ContextCacheTests.ContextCache_cold_succeeds"

# Build installer (WiX/MSBuild path setup required in environment)
MSBuild .\build-installer.proj /t:Build /p:Configuration=Release /m
```

There is no standalone lint script. Static analyzers are wired via project references/rulesets and can be forced during build:

```powershell
dotnet build .\tfs-aggregator-plugin.sln -c Debug-2025 -p:TfsVersion=2025 -p:Platform="Any CPU" -p:RunAnalyzersDuringBuild=true -p:RunCodeAnalysis=true
```

## High-level architecture

- **Two hosts, one core pipeline**
  - `Aggregator.ServerPlugin\WorkItemChangedEventHandler.cs` is the TFS/ADOS subscriber entrypoint (`ISubscriber`) for work item change events.
  - `Aggregator.ConsoleApp\RunCommand.cs` is the CLI runner for applying policies to explicit IDs or query results.
  - Both construct a `RuntimeContext` and execute `Aggregator.Core\EventProcessor`.

- **Core execution flow**
  - `RuntimeContext` (`Aggregator.Core\Context\RuntimeContext.cs`) loads and caches parsed settings + compiled script engine keyed by policy file path, with file-change invalidation.
  - `EventProcessor` (`Aggregator.Core\EventProcessor.cs`) filters policies/scopes, runs matching rules, and persists dirty work items.
  - `WorkItemRepository` (`Aggregator.Core\Facade\WorkItemRepository.cs`) encapsulates TFS client/store access and tracks loaded/created work items for deferred save.

- **Policy and scripting model**
  - `.policies` files are parsed by `TFSAggregatorSettings.AggregatorSettingsXmlParser` (`Aggregator.Core\Configuration\AggregatorSettingsXmlParser.cs`) and validated against embedded `AggregatorConfiguration.xsd`.
  - Script language dispatch happens in `Aggregator.Core\Script\ScriptEngine.cs` (`C#`, `VB.NET`, `PowerShell`), loading snippets/rules/functions from configuration.

- **Versioned reference system**
  - `Directory.Build.targets` imports `References\$(TfsVersion)\config.targets`.
  - `References\2025\config.targets` is configured to use local server assemblies from `.\ADO\` (where Azure DevOps Server assemblies live in this repo).

## Key repository conventions

- **Version targeting convention**
  - Project configurations are version-tagged (`Debug-2018`, `Release-2022.2`, etc.).
  - `TfsVersion` controls which `References\<version>\config.targets` file is imported, and therefore which SDK/server assembly set is used.

- **Local ADO assemblies convention**
  - The repository-local `ADO\` directory is the source of Azure DevOps Server assemblies for the added `2025` reference configuration.
  - For local builds using these assemblies, set `-p:TfsVersion=2025`.

- **Server plugin config file convention**
  - The server plugin resolves settings from a `.policies` file with the same base name as the plugin DLL, located in the same folder (`WorkItemChangedEventHandler.GetServerSettingsFullPath`).

- **Event handler safety convention**
  - Keep `WorkItemChangedEventHandler` constructor empty; comments in that file are intentional and tied to TFS host behavior.

- **Unit test style**
  - Tests in `Aggregator.Core\UnitTests.Core` use MSTest + NSubstitute and repository-specific mocks (`UnitTests.Core\Mock\*`) plus embedded/deployed `.policies` fixtures.
