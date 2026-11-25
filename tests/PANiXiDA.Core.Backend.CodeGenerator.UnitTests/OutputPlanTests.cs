using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class OutputPlanTests
{
    [Theory(DisplayName = "Detect creations, removals, and edits made after planning")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DetectsConcurrentEdits(bool expected, bool exists)
    {
        using var backend = new ScratchBackend(withFixture: false);
        var path = backend.FullPath("file.cs");
        if (exists)
        {
            backend.Write("file.cs", "current");
        }

        var before = expected ? "original"u8.ToArray() : null;
        var change = new OutputChange(path, before, "requested"u8.ToArray());

        var exception = Should.Throw<IOException>(() => OutputChange.ApplyAll([change]));

        exception.Message.ShouldContain("changed while the generation plan was being applied");
    }

    [Fact(DisplayName = "Retain apply and rollback errors when another writer blocks recovery")]
    public void ReportsRollbackFailure()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var file = backend.FullPath("file.cs");
        var conflict = backend.FullPath("directory.cs");
        Directory.CreateDirectory(conflict);

        var exception = Should.Throw<IOException>(() => OutputChange.ApplyAll(Changes()));

        exception.InnerException.ShouldBeOfType<AggregateException>().InnerExceptions.Count.ShouldBe(2);
        backend.Read("file.cs").ShouldBe("manual");
        Directory.EnumerateFiles(backend.Root, ".codegenerator-*.tmp").ShouldBeEmpty();

        IEnumerable<OutputChange> Changes()
        {
            yield return new OutputChange(file, null, "generated"u8.ToArray());
            backend.Write("file.cs", "manual");
            yield return new OutputChange(conflict, null, "will fail"u8.ToArray());
        }
    }

    [Fact(DisplayName = "Report a write with no containing directory")]
    public void RequiresOutputDirectory()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var change = new OutputChange(Path.GetPathRoot(backend.Root)!, null, "generated"u8.ToArray());

        var exception = Should.Throw<IOException>(() => OutputChange.ApplyAll([change]));

        exception.Message.ShouldContain("has no output directory");
    }
}
