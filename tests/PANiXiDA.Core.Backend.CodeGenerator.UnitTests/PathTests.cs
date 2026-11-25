using System.Diagnostics;

using Scriban;

using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;
using PANiXiDA.Core.Backend.CodeGenerator.Generation;
using PANiXiDA.Core.Backend.CodeGenerator.Templating;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class PathTests
{
    [Fact(DisplayName = "Resolve the repository root and reject its parent")]
    public void ResolvesRoot()
    {
        using var backend = new ScratchBackend(withFixture: false);

        RepositoryLayout.ResolvePath(".", backend.Root, backend.Root).ShouldBe(backend.Root);
        Should.Throw<InvalidOperationException>(() => RepositoryLayout.ResolvePath("..", backend.Root, backend.Root));
        Should.Throw<InvalidOperationException>(() => RepositoryLayout.ValidateRelativePath(
            "absolute result", Path.GetPathRoot(backend.Root)!));
    }

    [Fact(DisplayName = "Reject a directory link inside the repository")]
    public async Task RejectsLinks()
    {
        using var backend = new ScratchBackend(withFixture: false);
        using var outside = new ScratchBackend(withFixture: false);
        var link = backend.FullPath("linked");
        if (OperatingSystem.IsWindows())
        {
            using var junction = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/d", "/c", "mklink", "/J", link, outside.Root }
            })!;
            await junction.WaitForExitAsync(TestContext.Current.CancellationToken);
            junction.ExitCode.ShouldBe(0, await junction.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            Directory.CreateSymbolicLink(link, outside.Root);
        }

        try
        {
            var error = Should.Throw<InvalidOperationException>(() =>
                RepositoryLayout.ResolvePath("linked/out.cs", backend.Root, backend.Root));
            error.Message.ShouldContain("symbolic link");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact(DisplayName = "Require each selected artifact's target project")]
    public void RequiresTargetProject()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var module = new DomainProject(backend.FullPath("Domain/Test.Domain.csproj"), "Test", "Test");
        var target = new ResourceTarget(module, "Products", "Product");

        var missing = Should.Throw<InvalidOperationException>(() => target.DefaultDirectory("Application"));
        missing.Message.ShouldContain("Test.Application");

        backend.Write("Test.Other/Test.Other.csproj", "");
        Should.Throw<InvalidOperationException>(() => target.DefaultDirectory("Other"));

        var rootProject = new DomainProject(Path.Combine(Path.GetPathRoot(backend.Root)!, "Test.Domain.csproj"), "Test", "Test");
        Should.Throw<InvalidOperationException>(() => new ResourceTarget(rootProject, "Products", "Product")
            .DefaultDirectory("Application"));
    }

    [Fact(DisplayName = "Report the name of an unknown shared template")]
    public void RejectsUnknownPartial()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var layout = new RepositoryLayout(backend.Root, backend.Root);
        var loader = new PartialTemplateLoader(layout, null);

        var error = Should.Throw<InvalidOperationException>(() =>
            loader.GetPath(new TemplateContext(), default, "Missing.sbn"));

        error.Message.ShouldContain("Unknown template partial 'Missing.sbn'");
    }

    [Fact(DisplayName = "Load embedded and overridden shared templates through Scriban's async API")]
    public async Task RendersPartialOverridesAsynchronously()
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write("tools/Custom.sbn", "local");
        var layout = new RepositoryLayout(backend.Root, backend.FullPath("tools"));
        var loader = new PartialTemplateLoader(layout, new Dictionary<string, string> { ["DtoRecord.sbn"] = "Custom.sbn" });
        var context = new TemplateContext { TemplateLoader = loader };
        var template = Template.Parse("""{{ include "DtoRecord.sbn" }}""");

        var local = await template.RenderAsync(context);
        using var reader = new StreamReader(EmbeddedTemplates.Open("Partials/DtoRecord.sbn"));
        var embedded = await loader.LoadAsync(context, default, "Partials/DtoRecord.sbn");

        local.ShouldBe("local");
        embedded.ShouldBe(await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
