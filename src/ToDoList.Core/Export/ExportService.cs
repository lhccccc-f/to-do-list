namespace ToDoList.Core.Export;

public sealed class ExportService(DocumentExporter exporter)
{
    public static IReadOnlyList<string> TargetFiles(string directory, string name, ExportFormats formats)
    {
        name = ExportPlanner.ValidateFileName(name);
        if (formats == 0 || (formats & ~(ExportFormats.Word | ExportFormats.Pdf)) != 0)
            throw new InvalidOperationException("请至少选择一种导出格式。");
        var paths = new List<string>();
        if (formats.HasFlag(ExportFormats.Word)) paths.Add(Path.Combine(directory, name + ".docx"));
        if (formats.HasFlag(ExportFormats.Pdf)) paths.Add(Path.Combine(directory, name + ".pdf"));
        return paths;
    }

    // Render everything before touching existing output. Roll back the whole set
    // if a later file is locked or cannot be replaced.
    public IReadOnlyList<string> Export(ExportPlan plan, string directory, string name, ExportFormats formats, bool overwrite)
    {
        directory = Path.GetFullPath(directory);
        var targets = TargetFiles(directory, name, formats);
        Directory.CreateDirectory(directory);
        if (!overwrite && targets.Any(File.Exists)) throw new IOException("存在同名文件，请先确认覆盖或更改文件名。");
        var stage = Path.Combine(directory, ".todo-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var installed = new List<string>();
        var originals = new Dictionary<string, string>();
        var preserveStage = false;
        try
        {
            foreach (var target in targets)
            {
                var temporary = Path.Combine(stage, Path.GetFileName(target));
                if (Path.GetExtension(target).Equals(".docx", StringComparison.OrdinalIgnoreCase)) exporter.WriteWord(plan, temporary);
                else exporter.WritePdf(plan, temporary);
            }
            foreach (var target in targets)
            {
                if (File.Exists(target))
                {
                    if (!overwrite) throw new IOException("导出期间出现同名文件，请重新确认覆盖。");
                    var original = Path.Combine(stage, Path.GetFileName(target) + ".original");
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
