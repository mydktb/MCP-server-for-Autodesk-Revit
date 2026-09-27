using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MKRevitMCP.McpTools
{
    // Reads arguments out of the JSON a tool receives.
    // Missing or null arguments fall back to defaults; wrong types give a clear error.
    public static class Args
    {
        private static bool TryGet(JsonElement args, string name, out JsonElement value)
        {
            if (args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty(name, out value) &&
                value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }

            value = default;
            return false;
        }

        public static string GetString(JsonElement args, string name, string fallback = null)
        {
            return TryGet(args, name, out var v) ? v.ToString() : fallback;
        }

        public static string RequireString(JsonElement args, string name)
        {
            return GetString(args, name)
                ?? throw new ArgumentException($"Missing required argument '{name}'.");
        }

        public static long RequireLong(JsonElement args, string name)
        {
            if (!TryGet(args, name, out var v))
                throw new ArgumentException($"Missing required argument '{name}'.");

            return ToLong(v, name);
        }

        public static long? GetLongOrNull(JsonElement args, string name)
        {
            return TryGet(args, name, out var v) ? ToLong(v, name) : (long?)null;
        }

        public static double? GetDoubleOrNull(JsonElement args, string name)
        {
            if (!TryGet(args, name, out var v)) return null;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                return d;

            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                return s;

            throw new ArgumentException($"Argument '{name}' must be a number.");
        }

        public static int GetInt(JsonElement args, string name, int fallback)
        {
            return TryGet(args, name, out var v) ? (int)ToLong(v, name) : fallback;
        }

        public static bool GetBool(JsonElement args, string name, bool fallback)
        {
            if (!TryGet(args, name, out var v)) return fallback;

            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;

            if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b))
                return b;

            throw new ArgumentException($"Argument '{name}' must be true or false.");
        }

        public static List<long> GetLongList(JsonElement args, string name)
        {
            var list = new List<long>();
            if (!TryGet(args, name, out var v)) return list;

            if (v.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"Argument '{name}' must be an array.");

            foreach (var item in v.EnumerateArray())
                list.Add(ToLong(item, name));

            return list;
        }

        public static List<string> GetStringList(JsonElement args, string name)
        {
            var list = new List<string>();
            if (!TryGet(args, name, out var v)) return list;

            if (v.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"Argument '{name}' must be an array.");

            foreach (var item in v.EnumerateArray())
                list.Add(item.ToString());

            return list;
        }

        private static long ToLong(JsonElement v, string name)
        {
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt64(out var l)) return l;
                if (v.TryGetDouble(out var d)) return (long)d;
            }

            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s))
                return s;

            throw new ArgumentException($"Argument '{name}' must be a whole number.");
        }
    }
}
