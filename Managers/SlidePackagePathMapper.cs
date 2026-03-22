using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageColorChanger.Database.Models.DTOs;

namespace ImageColorChanger.Managers
{
    internal static class SlidePackagePathMapper
    {
        private static readonly JsonSerializerOptions SplitRegionJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static void RemapPaths(SlideProjectExportData exportData, Func<string, string> pathMapper)
        {
            if (exportData?.Projects == null || pathMapper == null)
            {
                return;
            }

            foreach (var project in exportData.Projects)
            {
                if (project == null)
                {
                    continue;
                }

                project.BackgroundImagePath = RemapPath(project.BackgroundImagePath, "BackgroundImagePath", pathMapper);

                if (project.Slides == null)
                {
                    continue;
                }

                foreach (var slide in project.Slides)
                {
                    if (slide == null)
                    {
                        continue;
                    }

                    slide.BackgroundImagePath = RemapPath(slide.BackgroundImagePath, "BackgroundImagePath", pathMapper);
                    slide.SplitRegionsData = RemapSplitRegionsData(slide.SplitRegionsData, pathMapper);

                    if (slide.Elements == null)
                    {
                        continue;
                    }

                    foreach (var element in slide.Elements)
                    {
                        if (element == null)
                        {
                            continue;
                        }

                        element.ComponentConfigJson = RemapComponentConfigJson(element.ComponentConfigJson, pathMapper);
                    }
                }
            }
        }

        private static string RemapSplitRegionsData(string splitRegionsData, Func<string, string> pathMapper)
        {
            if (string.IsNullOrWhiteSpace(splitRegionsData) || pathMapper == null)
            {
                return splitRegionsData;
            }

            try
            {
                var regionDataList = JsonSerializer.Deserialize<List<SplitRegionData>>(splitRegionsData, SplitRegionJsonOptions);
                if (regionDataList == null || regionDataList.Count == 0)
                {
                    return splitRegionsData;
                }

                bool changed = false;
                foreach (var regionData in regionDataList)
                {
                    if (regionData == null || string.IsNullOrWhiteSpace(regionData.ImagePath))
                    {
                        continue;
                    }

                    string mapped = RemapPath(regionData.ImagePath, "ImagePath", pathMapper);
                    if (!string.Equals(mapped, regionData.ImagePath, StringComparison.Ordinal))
                    {
                        regionData.ImagePath = mapped;
                        changed = true;
                    }
                }

                return changed ? JsonSerializer.Serialize(regionDataList) : splitRegionsData;
            }
            catch
            {
                return splitRegionsData;
            }
        }

        private static string RemapComponentConfigJson(string componentConfigJson, Func<string, string> pathMapper)
        {
            if (string.IsNullOrWhiteSpace(componentConfigJson) || pathMapper == null)
            {
                return componentConfigJson;
            }

            try
            {
                var node = JsonNode.Parse(componentConfigJson);
                if (node == null)
                {
                    return componentConfigJson;
                }

                bool changed = RemapJsonNode(node, null, pathMapper);
                return changed ? node.ToJsonString() : componentConfigJson;
            }
            catch
            {
                return componentConfigJson;
            }
        }

        private static bool RemapJsonNode(JsonNode node, string parentPropertyName, Func<string, string> pathMapper)
        {
            if (node is JsonObject obj)
            {
                bool changed = false;
                var keys = new List<string>(obj.Count);
                foreach (var kvp in obj)
                {
                    keys.Add(kvp.Key);
                }

                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        if (!IsPathCandidate(key, text))
                        {
                            continue;
                        }

                        string mapped = pathMapper(text) ?? text;
                        if (!string.Equals(mapped, text, StringComparison.Ordinal))
                        {
                            obj[key] = mapped;
                            changed = true;
                        }
                    }
                    else if (child != null)
                    {
                        changed |= RemapJsonNode(child, key, pathMapper);
                    }
                }

                return changed;
            }

            if (node is JsonArray array)
            {
                bool changed = false;
                for (int i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        if (!IsPathCandidate(parentPropertyName, text))
                        {
                            continue;
                        }

                        string mapped = pathMapper(text) ?? text;
                        if (!string.Equals(mapped, text, StringComparison.Ordinal))
                        {
                            array[i] = mapped;
                            changed = true;
                        }
                    }
                    else if (child != null)
                    {
                        changed |= RemapJsonNode(child, parentPropertyName, pathMapper);
                    }
                }

                return changed;
            }

            return false;
        }

        private static string RemapPath(string value, string propertyName, Func<string, string> pathMapper)
        {
            if (!IsPathCandidate(propertyName, value))
            {
                return value;
            }

            string mapped = pathMapper(value);
            return string.IsNullOrWhiteSpace(mapped) ? value : mapped;
        }

        private static bool IsPathCandidate(string propertyName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            if (normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.StartsWith("data/slide-assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("data\\slide-assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Path.IsPathRooted(normalized))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            string key = propertyName.Trim().ToLowerInvariant();
            return key.Contains("path") || key.Contains("file") || key.Contains("uri");
        }
    }
}
