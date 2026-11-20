namespace ToDoList.Core.Export;

public sealed class ExportService(DocumentExporter exporter,
    Func<string, string, Task<IReadOnlyList<string>>>? renderJpg = null)
{
    public static IReadOnlyList<string> TargetFiles(string directory, string name, ExportFormats formats)
    {
        name = ExportPlanner.ValidateFileName(name);
        if (formats == 0 || (formats & ~(ExportFormats.Word | ExportFormats.Pdf | ExportFormats.Jpg)) != 0)
            throw new InvalidOperationException("请至少选择一种导出格式。");
        var paths = new List<string>();
        if (formats.HasFlag(ExportFormats.Word)) paths.Add(Path.Combine(directory, name + ".docx"));
        if (formats.HasFlag(ExportFormats.Pdf)) paths.Add(Path.Combine(directory, name + ".pdf"));
        return paths;
    }

    public IReadOnlyList<string> Export(ExportPlan plan, string directory, string name, ExportFormats formats, bool overwrite)
        => Task.Run(() => ExportAsync(plan, directory, name, formats,
            overwrite ? _ => Task.FromResult(true) : null)).GetAwaiter().GetResult();

    // The JPG page count is known only after rendering. Check the exact set of
    // filenames before replacing anything and roll back if installation fails.
    public async Task<IReadOnlyList<string>> ExportAsync(ExportPlan plan, string directory, string name, ExportFormats formats,
        Func<IReadOnlyList<string>, Task<bool>>? confirmOverwrite)
    {
        directory = Path.GetFullPath(directory);
        name = ExportPlanner.ValidateFileName(name);
        var targets = TargetFiles(directory, name, formats).ToList();
        Directory.CreateDirectory(directory);
        var stage = Path.Combine(directory, ".todo-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var installed = new List<string>();
        var originals = new Dictionary<string, string>();
        var preserveStage = false;
        try
        {
            var pdf = Path.Combine(stage, formats.HasFlag(ExportFormats.Pdf) ? name + ".pdf" : "render-source.pdf");
            await Task.Run(() =>
            {
                if (formats.HasFlag(ExportFormats.Word)) exporter.WriteWord(plan, Path.Combine(stage, name + ".docx"));
                if ((formats & (ExportFormats.Pdf | ExportFormats.Jpg)) != 0) exporter.WritePdf(plan, pdf);
            });
            if (formats.HasFlag(ExportFormats.Jpg))
            {
                if (renderJpg is null) throw new InvalidOperationException("当前环境无法导出 JPG 图片。");
                var images = await renderJpg(pdf, name);
                targets.AddRange(images.Select(file => Path.Combine(directory, file)));
            }

            var existing = targets.Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existing.Count > 0)
            {
                if (confirmOverwrite is null) throw new IOException("存在同名文件，请先确认覆盖或更改文件名。");
                if (!await confirmOverwrite(targets.Where(existing.Contains).ToArray())) return [];
            }

            foreach (var target in targets)
            {
                if (File.Exists(target))
                {
                    if (!existing.Contains(target)) throw new IOException("导出期间出现同名文件，请重新确认覆盖。");
                    var backupFolder = Path.Combine(stage, "originals");
                    Directory.CreateDirectory(backupFolder);
                    var original = Path.Combine(backupFolder, Path.GetFileName(target));
                    File.Move(target, original);
                    originals.Add(target, original);
                }
                File.Move(Path.Combine(stage, Path.GetFileName(target)), target);
                installed.Add(target);
            }
            return targets;
        }
        catch (Exception failure)
        {
            try
            {
                foreach (var target in installed) File.Delete(target);
                foreach (var (target, original) in originals) File.Move(original, target);
            }
            catch (Exception rollback)
            {
                // Preserve the stage, including previous documents, for recovery.
                preserveStage = true;
                throw new IOException($"导出失败，原文件备份保留于：{stage}。请检查目录权限或文件占用。", new AggregateException(failure, rollback));
            }
            throw;
        }
        finally
        {
            // Cleanup is best effort and must never mask the original exception.
            if (!preserveStage)
                try { Directory.Delete(stage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
