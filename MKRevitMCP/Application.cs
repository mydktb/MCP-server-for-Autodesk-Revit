using System.Reflection;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public class Application : IExternalApplication
    {
        public static PushButton ServerButton;

        public Result OnStartup(UIControlledApplication application)
        {
            RevitTask.Initialize();

            application.CreateRibbonTab("MK Revit MCP");
            var panel = application.CreateRibbonPanel("MK Revit MCP", "Server");

            var buttonData = new PushButtonData(
                "ToggleServer",
                "Start\nServer",
                Assembly.GetExecutingAssembly().Location,
                "MKRevitMCP.Commands.ToggleServerCommand");

            buttonData.ToolTip = "Start or stop the MCP listener on port 5566.";

            ServerButton = panel.AddItem(buttonData) as PushButton;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            McpServer.Stop();
            return Result.Succeeded;
        }
    }
}