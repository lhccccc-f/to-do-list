using System.Globalization;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace ToDoList.App;

internal static class PdfImageRenderer
{
    public static async Task<IReadOnlyList<string>> RenderJpgAsync(string pdfPath, string name)
    {
        var source = await StorageFile.GetFileFromPathAsync(pdfPath);
        var document = await PdfDocument.LoadFromFileAsync(source);
        if (document.PageCount == 0) throw new InvalidOperationException("没有可转换为图片的页面。");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(pdfPath)!);
        var files = new List<string>();
        var digits = Math.Max(2, document.PageCount.ToString(CultureInfo.InvariantCulture).Length);
        for (uint i = 0; i < document.PageCount; i++)
        {
            var pageNumber = (i + 1).ToString("D" + digits, CultureInfo.InvariantCulture);
            var fileName = document.PageCount == 1 ? name + ".jpg" : $"{name}-{pageNumber}.jpg";
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists);
            using var page = document.GetPage(i);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = 1654,
                BackgroundColor = Microsoft.UI.Colors.White,
                BitmapEncoderId = BitmapEncoder.JpegEncoderId
            });
            files.Add(fileName);
        }
        return files;
    }
}
