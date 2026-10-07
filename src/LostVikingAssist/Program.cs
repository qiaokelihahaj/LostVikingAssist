using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LostVikingAssist;

internal sealed record OperationResult(bool Success, string Message);

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Get(string key)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (args.Contains("--build") || args.Contains("--install") || args.Contains("--restore") || args.Contains("--inspect"))
        {
            string? report = Get("--report");
            if (report is null) return 2;
            OperationResult result;
            try
            {
                string root = Get("--game") ?? AssistService.DetectGame() ?? "";
                string message;
                if (args.Contains("--build"))
                {
                    var options = new AssistOptions(!args.Contains("--no-bombs"), !args.Contains("--no-invulnerability"));
                    string output = Get("--output") ?? AssistService.DefaultOutput;
                    var built = Get("--map") is string source ? AssistService.BuildFromMap(source, output, options) : AssistService.BuildFromClient(root, output, options);
                    message = built.Map;
                }
                else if (args.Contains("--install")) message = AssistService.Install(root, Get("--map") ?? throw new IOException("缺少 --map"));
                else if (args.Contains("--restore")) message = AssistService.Restore(root);
                else message = JsonSerializer.Serialize(AssistService.InspectMap(Get("--map") ?? Path.Combine(root, "Maps", "Campaign", "TArcade.SC2Map")));
                result = new(true, message);
            }
            catch (Exception error) { result = new(false, error.Message); }
            File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return result.Success ? 0 : 1;
        }
        var app = new Application();
        var window = new MainWindow();
        if (Get("--preview") is string png)
        {
            // Render our own WPF view for visual review without interacting with the user's desktop.
            var view = (FrameworkElement)window.Content;
            window.Content = null;
            view.Resources = window.Resources;
            view.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
            view.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
            view.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, window.FontSize);
            int width = (int)window.Width, height = (int)window.Height;
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => {
                view.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(png); encoder.Save(output);
                app.Shutdown();
            });
            app.Run();
            return 0;
        }
        return app.Run(window);
    }
}
