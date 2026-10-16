using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;

namespace ChatGPTRevit;

public static class LocalServices
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BIMCopiloto");
    public static ImageAttachment Image(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidOperationException("Imagem maior que 20 MB.");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return Image(frame, Path.GetFileName(path));
    }
    public static ImageAttachment Image(System.Windows.Media.Imaging.BitmapSource source, string name)
    {
        if ((long)source.PixelWidth * source.PixelHeight > 100_000_000)
            throw new InvalidOperationException("Imagem grande demais. Reduza a resolução.");
        var factor = Math.Min(1, 2048d / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource bitmap = factor < 1 ? new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(factor, factor)) : source;
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream(); encoder.Save(memory);
        if (memory.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Imagem ainda muito pesada; use JPG ou reduza a resolução.");
        return new ImageAttachment { Name = name, DataUrl = "data:image/png;base64," + Convert.ToBase64String(memory.ToArray()),
            Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Preview = bitmap };
    }
    public static void RestorePreview(ImageAttachment image)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]));
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        image.Preview = bitmap;
    }
    public static ImageAttachment Capture(Document doc)
    {
        var directory = Path.Combine(Root, "captures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.CurrentView, FilePath = Path.Combine(directory, "vista"),
            HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
            ZoomType = ZoomFitType.FitToPage, PixelSize = 1800, FitDirection = FitDirectionType.Horizontal
        };
        doc.ExportImage(options);
        var path = Directory.GetFiles(directory, "*.png").SingleOrDefault()
            ?? throw new InvalidOperationException("Esta vista não pôde ser capturada.");
        var image = Image(path);
        image.Name = doc.ActiveView.Name + ".png";
        File.Delete(path); Directory.Delete(directory);
        return image;
    }
    public static string Context(ProjectSnapshot snapshot)
    {
        // No local paths are sent. Summaries are explicitly bounded, never cut mid-JSON.
        return JsonSerializer.Serialize(new
        {
            snapshot.ProjectTitle, snapshot.DocumentKey, snapshot.ActiveView, snapshot.CollectedAtUtc,
            snapshot.Selection, snapshot.Levels, snapshot.Types, snapshot.Materials, snapshot.CategoryCounts,
            snapshot.Elements, snapshot.Rooms, snapshot.Warnings, snapshot.OmittedElements
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
    public static void SaveHistory(string key, IEnumerable<ChatMessage> messages)
    {
        var dir = Path.Combine(Root, "history"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.Parse(key).ToString("N") + ".json");
        // Images stay in session memory. History stores their names, never credentials.
        var records = messages.Where(m => m.Complete).TakeLast(60).Select(m => new ChatMessage
        { Role = m.Role, Text = m.Text + (m.Images.Count > 0 ? "\n[Imagens da sessão anterior: " + string.Join(", ", m.Images.Select(i => i.Name)) + "]" : "") });
        AtomicWrite(path, JsonSerializer.Serialize(records));
    }
    public static List<ChatMessage> ReadHistory(string key)
    {
        var path = Path.Combine(Root, "history", Guid.Parse(key).ToString("N") + ".json");
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(path)) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public static void Log(string key, EditPlan plan, EditExecutionResult result)
    {
        var dir = Path.Combine(Root, "logs"); Directory.CreateDirectory(dir);
        AtomicWrite(Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"),
            JsonSerializer.Serialize(new { document = key, time = DateTime.UtcNow, plan, result }, EditPlanParser.Options));
    }
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        File.Move(tmp, path, true);
    }
}
