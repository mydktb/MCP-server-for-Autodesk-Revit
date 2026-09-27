using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MKRevitMCP
{
    public static class ToolRegistry
    {
        private static readonly Dictionary<string, ITool> _tools =
            new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            var toolTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = (ITool)Activator.CreateInstance(type);
                    _tools[tool.Name] = tool;
                    Log.Write($"Tool registered: {tool.Name}");
                }
                catch (Exception ex)
                {
                    Log.Write($"Failed to register {type.Name}: {ex.Message}");
                }
            }

            Log.Write($"ToolRegistry ready: {_tools.Count} tools.");
        }

        public static bool TryGet(string name, out ITool tool)
        {
            return _tools.TryGetValue(name, out tool);
        }
    }
}