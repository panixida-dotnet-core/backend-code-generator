using System.Text.Json.Nodes;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class FileChangesTests
{
    [Fact(DisplayName = "Preserve local edits by default and replace them only on explicit overwrite")]
    public async Task RequiresOverwriteToReplace()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        var created = (await backend.RunAsync(job)).ShouldHaveSingleItem();
        backend.Write(created.Path, "// manual edit");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("overwriteExisting is false");
        backend.Read(created.Path).ShouldBe("// manual edit");

        job["overwriteExisting"] = true;
        var replaced = (await backend.RunAsync(job)).ShouldHaveSingleItem();
        replaced.ShouldBe(created with { Action = GeneratedFileAction.Replaced });
        backend.Read(replaced.Path).ShouldContain("CreateProductCommand");
    }

    [Fact(DisplayName = "Delete only selected generated files without building the Domain project")]
    public async Task DeletesSelectedFiles()
    {
        using var backend = new ScratchBackend();
        var files = await backend.RunAsync(ScratchBackend.Job("CreateCommand", "ChildCreateCommand"));
        var root = files.Single(file => Path.GetFileName(file.Path) == "CreateProductCommand.cs");
        var child = files.Single(file => Path.GetFileName(file.Path) == "CreateProductVariantCommand.cs");
        backend.Write($"{ScratchBackend.DomainDirectory}/{ScratchBackend.ModuleName}.Domain.csproj", "invalid project");
        var job = ScratchBackend.Job("ChildCreateCommand");
        job["deleteGenerated"] = true;

        var deleted = (await backend.RunAsync(job)).ShouldHaveSingleItem();

        deleted.ShouldBe(child with { Action = GeneratedFileAction.Deleted });
        File.Exists(backend.FullPath(child.Path)).ShouldBeFalse();
        File.Exists(backend.FullPath(root.Path)).ShouldBeTrue();
        (await backend.RunAsync(job)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Delete only configured paths regardless of the existing source content")]
    public async Task DeletesExistingFileAtConfiguredPath()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["deleteGenerated"] = true;
        job["outputDirectory"] = new JsonObject { ["CreateCommand"] = "generated" };
        var path = Path.Combine("generated", "CreateProductCommand.cs");
        const string otherPath = "generated/Keep.cs";
        backend.Write(path, "public sealed class CreateProductCommand;");
        backend.Write(otherPath, "public sealed class Keep;");

        var deleted = (await backend.RunAsync(job)).ShouldHaveSingleItem();

        deleted.ShouldBe(new GeneratedFile(path, GeneratedFileAction.Deleted));
        File.Exists(backend.FullPath(path)).ShouldBeFalse();
        backend.Read(otherPath).ShouldBe("public sealed class Keep;");
    }

    [Fact(DisplayName = "Reject duplicate output paths and leave the repository intact")]
    public async Task RejectsDuplicatePaths()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand", "CreateCommand");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("not a unique file path");
        Directory.EnumerateFiles(backend.Root, "CreateProductCommand.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Restore previous files when a later file cannot be written")]
    public async Task RollsBackAnIncompleteWrite()
    {
        using var backend = new ScratchBackend();
        var create = (await backend.RunAsync(ScratchBackend.Job("CreateCommand"))).ShouldHaveSingleItem();
        backend.Write(create.Path, "// manual edit");
        backend.Write("blocked", "existing file");
        var job = ScratchBackend.Job("CreateCommand", "UpdateCommand");
        job["overwriteExisting"] = true;
        job["outputDirectory"] = new JsonObject { ["UpdateCommand"] = "blocked/subdirectory" };

        await Should.ThrowAsync<IOException>(() => backend.RunAsync(job));

        backend.Read(create.Path).ShouldBe("// manual edit");
        backend.Read("blocked").ShouldBe("existing file");
        Directory.EnumerateFiles(backend.Root, ".codegenerator-*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory(DisplayName = "Reject output and template paths outside the repository")]
    [InlineData("outputDirectory", "../outside")]
    [InlineData("templates", "../../../outside.sbn")]
    public async Task RejectsPathsOutsideRepository(string setting, string path)
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job[setting] = new JsonObject { ["CreateCommand"] = path };

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("inside the repository");
    }

    [Fact(DisplayName = "Reject combined overwrite and delete modes")]
    public async Task RejectsConflictingModes()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["overwriteExisting"] = true;
        job["deleteGenerated"] = true;

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("cannot both be true");
    }
}
