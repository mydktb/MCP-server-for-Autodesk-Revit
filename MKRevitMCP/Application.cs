using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public class Application : IExternalApplication
    {
        public static PushButton ServerButton;

        public Result OnStartup(UIControlledApplication application)
        {
            RevitTask.Initialize();
            ToolRegistry.Initialize();

            application.CreateRibbonTab("MK Revit MCP");
            var panel = application.CreateRibbonPanel("MK Revit MCP", "Server");

            var buttonData = new PushButtonData(
                "ToggleServer",
                "Start",
                Assembly.GetExecutingAssembly().Location,
                "MKRevitMCP.Commands.ToggleServerCommand");

            buttonData.ToolTip = "Start or stop the MCP listener on port 5566.";
            buttonData.LongDescription = "Lets Claude read from and write to this model while the server is running.";

            buttonData.Image = LoadIcon("server16.png");
            buttonData.LargeImage = LoadIcon("server32.png");

            ServerButton = panel.AddItem(buttonData) as PushButton;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            McpServer.Stop();
            return Result.Succeeded;
        }

        private static ImageSource LoadIcon(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"MKRevitMCP.Resources.{fileName}";

            try
            {
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        var available = string.Join(", ", assembly.GetManifestResourceNames());
                        Log.Write($"ICON NOT FOUND: '{resourceName}'. Embedded resources are: [{available}]");
                        return null;
                    }

                    var decoder = new PngBitmapDecoder(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    Log.Write($"Icon loaded: {resourceName}");
                    return decoder.Frames[0];
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Icon load failed for '{resourceName}': {ex.Message}");
                return null;
            }
        }
    }
}