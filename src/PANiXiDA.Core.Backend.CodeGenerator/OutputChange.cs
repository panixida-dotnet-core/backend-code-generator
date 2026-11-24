namespace PANiXiDA.Core.Backend.CodeGenerator;

internal sealed record OutputChange(string Path, byte[]? Before, byte[]? After)
{
    internal GeneratedFileAction Action => After is null
        ? GeneratedFileAction.Deleted
        : Before is null ? GeneratedFileAction.Created : GeneratedFileAction.Replaced;

    internal static void ApplyAll(IEnumerable<OutputChange> changes)
    {
        var applied = new List<OutputChange>();
        try
        {
            foreach (var change in changes)
            {
                change.Apply(change.Before, change.After);
                applied.Add(change);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            List<Exception> errors = [exception];
            for (var index = applied.Count - 1; index >= 0; index--)
            {
                var change = applied[index];
                try
                {
                    change.Apply(change.After, change.Before);
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    errors.Add(rollbackError);
                }
            }

            if (errors.Count > 1)
            {
                throw new IOException("Could not apply the file changes or fully restore them.", new AggregateException(errors));
            }

            throw;
        }
    }

    private void Apply(byte[]? expected, byte[]? next)
    {
        var current = File.Exists(Path) ? File.ReadAllBytes(Path) : null;
        if ((expected is null) != (current is null)
            || (expected is not null && !expected.AsSpan().SequenceEqual(current)))
        {
            throw new IOException($"'{Path}' changed while the generation plan was being applied.");
        }

        if (next is null)
        {
            File.Delete(Path);
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new IOException($"'{Path}' has no output directory.");
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".codegenerator-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(next);
            }

            File.Move(temporary, Path, overwrite: expected is not null);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
