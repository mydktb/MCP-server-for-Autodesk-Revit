using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public static class Tools
    {
        public static string GetModelInfo(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            int viewCount = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate);

            return JsonSerializer.Serialize(new
            {
                title = doc.Title,
                path = doc.PathName,
                activeView = doc.ActiveView?.Name,
                viewCount = viewCount
            });
        }

        public static string ListViews(UIApplication app, string viewType, string nameContains)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            if (!string.IsNullOrWhiteSpace(viewType))
            {
                views = views.Where(v =>
                    v.ViewType.ToString().Equals(viewType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                views = views.Where(v =>
                    v.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = views
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .Select(v => new
                {
                    id = v.Id.Value,
                    name = v.Name,
                    type = v.ViewType.ToString()
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = result.Count, views = result });
        }

        public static string RenameViews(UIApplication app, List<RenamePair> renames)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (renames == null || renames.Count == 0)
                return JsonSerializer.Serialize(new { error = "No renames supplied." });

            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Rename views"))
            {
                tx.Start();

                foreach (var pair in renames)
                {
                    try
                    {
                        var view = doc.GetElement(new ElementId(pair.Id)) as View;

                        if (view == null)
                        {
                            failed.Add(new { id = pair.Id, reason = "Not a view, or id not found." });
                            continue;
                        }

                        string oldName = view.Name;
                        view.Name = pair.NewName;

                        succeeded.Add(new { id = pair.Id, oldName = oldName, newName = pair.NewName });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { id = pair.Id, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                renamed = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded,
                failed = failed
            });
        }

        public static string GetWarnings(UIApplication app, int maxIdsPerGroup)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (maxIdsPerGroup <= 0) maxIdsPerGroup = 20;

            var warnings = doc.GetWarnings();

            var groups = warnings
                .GroupBy(w => w.GetDescriptionText())
                .OrderByDescending(g => g.Count())
                .Select(g =>
                {
                    var ids = g.SelectMany(w => w.GetFailingElements())
                               .Select(id => id.Value)
                               .Distinct()
                               .ToList();

                    return new
                    {
                        description = g.Key,
                        occurrences = g.Count(),
                        severity = g.First().GetSeverity().ToString(),
                        elementCount = ids.Count,
                        elementIds = ids.Take(maxIdsPerGroup).ToList(),
                        truncated = ids.Count > maxIdsPerGroup
                    };
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                totalWarnings = warnings.Count,
                distinctTypes = groups.Count,
                groups = groups
            });
        }

        public static string SetSelection(UIApplication app, List<long> ids)
        {
            var uidoc = app.ActiveUIDocument;

            if (uidoc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (ids == null || ids.Count == 0)
            {
                uidoc.Selection.SetElementIds(new List<ElementId>());
                return JsonSerializer.Serialize(new { selected = 0, cleared = true });
            }

            var valid = new List<ElementId>();
            var missing = new List<long>();

            foreach (var raw in ids)
            {
                var elementId = new ElementId(raw);

                if (uidoc.Document.GetElement(elementId) != null)
                    valid.Add(elementId);
                else
                    missing.Add(raw);
            }

            uidoc.Selection.SetElementIds(valid);
            uidoc.ShowElements(valid);

            return JsonSerializer.Serialize(new
            {
                selected = valid.Count,
                notFound = missing.Count,
                missingIds = missing
            });
        }
        
        public static string GetCategoriesByKeywords(UIApplication app, List<string> keywords)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (keywords == null || keywords.Count == 0)
                return JsonSerializer.Serialize(new { error = "No keywords supplied." });

            var matches = new List<object>();

            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat == null) continue;

                bool hit = keywords.Any(k =>
                    !string.IsNullOrWhiteSpace(k) &&
                    cat.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!hit) continue;

                matches.Add(new
                {
                    id = cat.Id.Value,
                    name = cat.Name,
                    builtIn = cat.BuiltInCategory.ToString(),
                    canHaveElements = cat.AllowsBoundParameters
                });
            }

            return JsonSerializer.Serialize(new
            {
                count = matches.Count,
                categories = matches.OrderBy(m => ((dynamic)m).name).ToList()
            });
        }

        public static string CountElementsByCategory(UIApplication app, List<long> categoryIds)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (categoryIds == null || categoryIds.Count == 0)
                return JsonSerializer.Serialize(new { error = "No category ids supplied." });

            var results = new List<object>();

            foreach (var rawId in categoryIds)
            {
                try
                {
                    var bic = (BuiltInCategory)rawId;

                    int count = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .GetElementCount();

                    var cat = Category.GetCategory(doc, bic);

                    results.Add(new
                    {
                        categoryId = rawId,
                        name = cat?.Name ?? bic.ToString(),
                        count = count
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { categoryId = rawId, error = ex.Message });
                }
            }

            return JsonSerializer.Serialize(new { results = results });
        }

        public static string GetElementsByCategory(UIApplication app, long categoryId, int maxIdsPerType, bool includeTypeNames)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (maxIdsPerType <= 0) maxIdsPerType = 50;

            BuiltInCategory bic;
            try
            {
                bic = (BuiltInCategory)categoryId;
            }
            catch
            {
                return JsonSerializer.Serialize(new { error = "Invalid category id: " + categoryId });
            }

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            if (elements.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    categoryId = categoryId,
                    total = 0,
                    groups = new List<object>()
                });
            }

            if (!includeTypeNames)
            {
                var flatIds = elements.Select(e => e.Id.Value).ToList();

                return JsonSerializer.Serialize(new
                {
                    categoryId = categoryId,
                    total = flatIds.Count,
                    elementIds = flatIds.Take(maxIdsPerType).ToList(),
                    truncated = flatIds.Count > maxIdsPerType
                });
            }

            var groups = elements
                .GroupBy(e =>
                {
                    var typeId = e.GetTypeId();

                    if (typeId == null || typeId == ElementId.InvalidElementId)
                        return "(no type)";

                    var typeElement = doc.GetElement(typeId);
                    if (typeElement == null) return "(no type)";

                    string family = (typeElement as ElementType)?.FamilyName;

                    return string.IsNullOrWhiteSpace(family)
                        ? typeElement.Name
                        : $"{family} : {typeElement.Name}";
                })
                .OrderByDescending(g => g.Count())
                .Select(g =>
                {
                    var ids = g.Select(e => e.Id.Value).ToList();

                    return new
                    {
                        typeName = g.Key,
                        count = ids.Count,
                        elementIds = ids.Take(maxIdsPerType).ToList(),
                        truncated = ids.Count > maxIdsPerType
                    };
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                categoryId = categoryId,
                total = elements.Count,
                distinctTypes = groups.Count,
                groups = groups
            });
        }

        private static string ParamValueAsString(Parameter p)
        {
            if (p == null || !p.HasValue) return null;

            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString();
                case StorageType.Integer:
                    return p.AsInteger().ToString();
                case StorageType.Double:
                    return p.AsDouble().ToString("G17");
                case StorageType.ElementId:
                    return p.AsElementId().Value.ToString();
                default:
                    return null;
            }
        }

        public static string GetElementParameters(UIApplication app, long elementId, bool includeType)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var element = doc.GetElement(new ElementId(elementId));

            if (element == null)
                return JsonSerializer.Serialize(new { error = "Element not found: " + elementId });

            var instanceParams = element.Parameters
                .Cast<Parameter>()
                .Select(p => new
                {
                    name = p.Definition?.Name,
                    id = p.Id.Value,
                    storageType = p.StorageType.ToString(),
                    isReadOnly = p.IsReadOnly,
                    value = ParamValueAsString(p),
                    display = p.AsValueString()
                })
                .OrderBy(p => p.name)
                .ToList();

            object typeParams = null;
            long typeId = -1;

            if (includeType)
            {
                var tid = element.GetTypeId();

                if (tid != null && tid != ElementId.InvalidElementId)
                {
                    var typeElement = doc.GetElement(tid);

                    if (typeElement != null)
                    {
                        typeId = tid.Value;

                        typeParams = typeElement.Parameters
                            .Cast<Parameter>()
                            .Select(p => new
                            {
                                name = p.Definition?.Name,
                                id = p.Id.Value,
                                storageType = p.StorageType.ToString(),
                                isReadOnly = p.IsReadOnly,
                                value = ParamValueAsString(p),
                                display = p.AsValueString()
                            })
                            .OrderBy(p => p.name)
                            .ToList();
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                elementId = elementId,
                elementName = element.Name,
                category = element.Category?.Name,
                typeId = typeId,
                instanceParameters = instanceParams,
                typeParameters = typeParams
            });
        }

        public static string GetParameterValues(UIApplication app, List<long> elementIds, List<string> parameterNames, bool groupByValue)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (elementIds == null || elementIds.Count == 0)
                return JsonSerializer.Serialize(new { error = "No element ids supplied." });

            if (parameterNames == null || parameterNames.Count == 0)
                return JsonSerializer.Serialize(new { error = "No parameter names supplied." });

            var perParameter = new List<object>();

            foreach (var paramName in parameterNames)
            {
                var readings = new List<KeyValuePair<long, string>>();

                foreach (var rawId in elementIds)
                {
                    var element = doc.GetElement(new ElementId(rawId));
                    if (element == null) continue;

                    var p = element.LookupParameter(paramName);

                    if (p == null)
                    {
                        var tid = element.GetTypeId();
                        if (tid != null && tid != ElementId.InvalidElementId)
                            p = doc.GetElement(tid)?.LookupParameter(paramName);
                    }

                    string value = p == null ? "(parameter not found)" : (ParamValueAsString(p) ?? "(empty)");
                    readings.Add(new KeyValuePair<long, string>(rawId, value));
                }

                if (groupByValue)
                {
                    var grouped = readings
                        .GroupBy(r => r.Value)
                        .OrderByDescending(g => g.Count())
                        .Select(g => new
                        {
                            value = g.Key,
                            count = g.Count(),
                            elementIds = g.Select(r => r.Key).ToList()
                        })
                        .ToList();

                    perParameter.Add(new { parameter = paramName, groups = grouped });
                }
                else
                {
                    perParameter.Add(new
                    {
                        parameter = paramName,
                        values = readings.Select(r => new { id = r.Key, value = r.Value }).ToList()
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                elementsQueried = elementIds.Count,
                parameters = perParameter
            });
        }

        public static string SetParameterValue(UIApplication app, List<long> elementIds, string parameterName, string newValue, bool dryRun)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (elementIds == null || elementIds.Count == 0)
                return JsonSerializer.Serialize(new { error = "No element ids supplied." });

            if (string.IsNullOrWhiteSpace(parameterName))
                return JsonSerializer.Serialize(new { error = "No parameter name supplied." });

            var planned = new List<object>();
            var blocked = new List<object>();

            foreach (var rawId in elementIds)
            {
                var element = doc.GetElement(new ElementId(rawId));

                if (element == null)
                {
                    blocked.Add(new { id = rawId, reason = "Element not found." });
                    continue;
                }

                var p = element.LookupParameter(parameterName);

                if (p == null)
                {
                    blocked.Add(new { id = rawId, reason = "Parameter not found on instance." });
                    continue;
                }

                if (p.IsReadOnly)
                {
                    blocked.Add(new { id = rawId, reason = "Parameter is read-only." });
                    continue;
                }

                planned.Add(new
                {
                    id = rawId,
                    currentValue = ParamValueAsString(p),
                    storageType = p.StorageType.ToString()
                });
            }

            if (dryRun)
            {
                return JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    parameter = parameterName,
                    newValue = newValue,
                    wouldChange = planned.Count,
                    blockedCount = blocked.Count,
                    planned = planned,
                    blocked = blocked
                });
            }

            var succeeded = new List<object>();
            var failed = new List<object>(blocked);

            using (var tx = new Transaction(doc, $"Set {parameterName}"))
            {
                tx.Start();

                foreach (var entry in planned)
                {
                    long rawId = (long)((dynamic)entry).id;

                    try
                    {
                        var element = doc.GetElement(new ElementId(rawId));
                        var p = element.LookupParameter(parameterName);

                        string before = ParamValueAsString(p);
                        bool ok;

                        switch (p.StorageType)
                        {
                            case StorageType.String:
                                ok = p.Set(newValue);
                                break;
                            case StorageType.Integer:
                                ok = int.TryParse(newValue, out int i) && p.Set(i);
                                break;
                            case StorageType.Double:
                                ok = double.TryParse(newValue, out double d) && p.Set(d);
                                break;
                            case StorageType.ElementId:
                                ok = long.TryParse(newValue, out long l) && p.Set(new ElementId(l));
                                break;
                            default:
                                ok = false;
                                break;
                        }

                        if (ok)
                            succeeded.Add(new { id = rawId, before = before, after = newValue });
                        else
                            failed.Add(new { id = rawId, reason = "Set rejected — wrong value format for " + p.StorageType });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { id = rawId, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                dryRun = false,
                parameter = parameterName,
                changed = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded,
                failed = failed
            });
        }
        public class RenamePair
        {
            public long Id { get; set; }
            public string NewName { get; set; }
        }
    }
}