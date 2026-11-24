# PANiXiDA.Core.Backend.CodeGenerator

`PANiXiDA.Core.Backend.CodeGenerator` is a .NET library that scaffolds editable C# CRUD files from domain aggregates and their child entity collections.

## Status

[![CI](https://github.com/panixida-dotnet-core/backend-code-generator/actions/workflows/ci.yml/badge.svg)](https://github.com/panixida-dotnet-core/backend-code-generator/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PANiXiDA.Core.Backend.CodeGenerator.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Backend.CodeGenerator)
[![NuGet downloads](https://img.shields.io/nuget/dt/PANiXiDA.Core.Backend.CodeGenerator.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Backend.CodeGenerator)
[![Target Framework](https://img.shields.io/badge/target-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

## Features

- Generates C# commands, queries, handlers, validation, EF Core mappings, and ASP.NET Core endpoints.
- Supports explicitly configured child entity collections with parent-scoped mutations and paginated queries.
- Maps scalar properties, enumerations, single-value objects, and compound value objects from the domain model.
- Ships Scriban templates and shared partials inside the library. Jobs can replace individual templates and output directories.
- Supports multiple jobs, selected artifacts, explicit overwrite, and deletion of files with the matching generator marker.

## Requirements

- .NET 10 SDK.
- A backend repository with PANiXiDA.Core domain conventions and the corresponding Application, Infrastructure, and Presentation projects for the artifacts you select.

## Installation

Add the NuGet reference to the console project that will run the generator.

```bash
dotnet add tools/Acme.Store.CodeGenerator package PANiXiDA.Core.Backend.CodeGenerator
```

## Quick Start

From the backend repository root, run the console project after adding this code to its `Program.cs`.

```csharp
using PANiXiDA.Core.Backend.CodeGenerator;

var changes = await BackendCodeGenerator.GenerateAsync(
    Directory.GetCurrentDirectory(),
    "tools/Acme.Store.CodeGenerator/appsettings.json");

foreach (var change in changes)
{
    Console.WriteLine($"{change.Action}: {change.Path}");
}
```

Save the JSON configuration alongside the console project.

```json
{
  "jobs": [
    {
      "pathToModels": "src/Modules/Catalog/Acme.Store.Catalog.Domain",
      "models": [
        {
          "aggregate": "Acme.Store.Catalog.Domain.Products.Product",
          "route": "products",
          "detailsFields": ["Id", "Name"],
          "listFields": ["Id", "Name"]
        }
      ],
      "generatedFiles": ["CreateCommand", "UpdateCommand"],
      "overwriteExisting": false,
      "deleteGenerated": false
    }
  ]
}
```

```bash
dotnet run --project tools/Acme.Store.CodeGenerator
```

The example generates two files. See the [full CRUD example](tests/PANiXiDA.Core.Backend.CodeGenerator.UnitTests/Fixtures/Backend/tools/Generator/appsettings.json) for the full artifact list and a `children` entry.

## Model Conventions

- An aggregate derives from `AggregateRoot<TId>` and has a Guid-backed ID exposing `Value` and `Create(Guid)`. It provides a static `Create` returning `Result<TAggregate>` and an instance `Update` returning `Result`.
- Method parameters bind to writable properties by name and input type. A private setter is supported. For a compound value object, each factory parameter must match a readable scalar property by name, type, and nullability. Use the value object's type in the aggregate method.
- Both read projections include `Id`. Compound value objects render as nested DTOs and map to a `jsonb` column. List sorting uses top-level scalar fields.
- A child collection exposes entities deriving from `Entity<TId>`. Configure `collection`, `resource`, `route`, read projections, and the aggregate method names for the write artifacts you select. Create returns `Result<TChildId>`. Update and delete take the child ID first and return `Result`.

## Configuration

| Setting | Behavior |
| --- | --- |
| `pathToModels` | Path to the Domain project directory or its `.Domain.csproj`, relative to the backend repository root. |
| `models` | One or more aggregate configurations from that Domain project. |
| `generatedFiles` | Explicit artifact names. Use `Child*` artifacts for entries in `children`. |
| `templates` | Optional mapping from an artifact name or an embedded partial filename to a Scriban file within the repository. Relative paths are resolved from the directory containing the configuration file. |
| `outputDirectory` | Optional mapping from an artifact name to a directory relative to the repository root. Supports `{Module}`, `{ModuleName}`, `{Feature}`, and `{Resource}`. |
| `overwriteExisting` | Allows replacing selected files. Defaults to `false`. |
| `deleteGenerated` | Deletes selected files with a matching generator marker without building Domain. Defaults to `false`. |

For example, `"templates": { "CreateCommand": "Templates/MyCreate.sbn", "DtoRecord.sbn": "Templates/MyDto.sbn" }` overrides one artifact and one shared partial for the job. All other templates come from the package.

Review the generated files before committing them to your backend repository.

## Development

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release
dotnet test --configuration Release
```
