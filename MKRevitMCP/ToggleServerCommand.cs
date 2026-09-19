using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ToggleServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (McpServer.IsRunning)
            {
                McpServer.Stop();
                Application.ServerButton.ItemText = "Start\nServer";
            }
            else
            {
                McpServer.Start();
                Application.ServerButton.ItemText = "Stop\nServer";
            }

            return Result.Succeeded;
        }
    }
}