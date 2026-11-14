using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI
{
    internal sealed partial class RevitCommandHandler
    {
        // ---------------- build.block ----------------

        private object BuildBlock(Document doc, IDictionary<string, object> args)
        {
            object originRaw;
            args.TryGetValue("origin", out originRaw);
            double[] origin = MmPair(originRaw, 0, 0);

            double length = Num(args, "length_mm", 48000);
            double depth = Num(args, "depth_mm", 18000);
            double corridor = Num(args, "corridor_width_mm", 2400);
            double bay = Num(args, "bay_mm", 8000);
            if (length < bay * 2) throw new ArgumentException("length_mm must be at least 2 bays");
            if (depth <= corridor + 4000) throw new ArgumentException("depth_mm must be greater than corridor_width_mm + 4000");

            string wallType = Str(args, "wall_type", null);
            string floorType = Str(args, "floor_type", null);
            string columnType = Str(args, "column_type", null);
            string doorType = Str(args, "door_type", null);
            string windowType = Str(args, "window_type", null);

            bool makeRooms = Bool(args, "make_rooms", true);
            bool makeDoors = Bool(args, "make_doors", true);
            bool makeWindows = Bool(args, "make_windows", true);
            bool makeColumns = Bool(args, "make_columns", true);
            bool makeRoof = Bool(args, "roof", true);
            string blockName = Str(args, "name", null);

            object levelsRaw;
            if (!args.TryGetValue("levels", out levelsRaw) || levelsRaw == null)
                throw new ArgumentException("'levels' is required: [ { name, elevation_mm, height_mm }, ... ]");
            var levelSpecs = levelsRaw as object[];
            if (levelSpecs == null || levelSpecs.Length == 0) throw new ArgumentException("'levels' must be a non-empty array");

            double x0 = origin[0], y0 = origin[1];
            double x1 = x0 + length, y1 = y0 + depth;
            double cy1 = y0 + (depth - corridor) / 2.0;
            double cy2 = y0 + (depth + corridor) / 2.0;

            int bays = (int)Math.Round(length / bay);
            double step = length / bays;

            int nWalls = 0, nDoors = 0, nWindows = 0, nColumns = 0, nRooms = 0, nSlabs = 0;
            var levelInfos = new List<object>();
            var roomSummaries = new List<object>();
            var failures = new List<object>();
            double topElevation = 0;

            foreach (object item in levelSpecs)
            {
                var spec = item as Dictionary<string, object>;
                if (spec == null) throw new ArgumentException("each level entry must be an object");

                string name = spec.ContainsKey("name") ? Convert.ToString(spec["name"]) : null;
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("level 'name' is required");
                double elev = spec.ContainsKey("elevation_mm") ? Convert.ToDouble(spec["elevation_mm"], CultureInfo.InvariantCulture) : 0;
                double height = spec.ContainsKey("height_mm") ? Convert.ToDouble(spec["height_mm"], CultureInfo.InvariantCulture) : 3600;

                EnsureLevel(doc, name, elev);
                topElevation = Math.Max(topElevation, elev + height);

                try
                {
                    int south = WallId(doc, x0, y0, x1, y0, name, wallType, height);
                    WallId(doc, x1, y0, x1, y1, name, wallType, height);
                    int north = WallId(doc, x1, y1, x0, y1, name, wallType, height);
                    WallId(doc, x0, y1, x0, y0, name, wallType, height);
                    nWalls += 4;

                    int corridorSouth = WallId(doc, x0, cy1, x1, cy1, name, wallType, height);
                    int corridorNorth = WallId(doc, x0, cy2, x1, cy2, name, wallType, height);
                    nWalls += 2;

                    for (int i = 1; i < bays; i++)
                    {
                        double px = x0 + i * step;
                        WallCreate(doc, WallArgs(px, y0, px, cy1, name, wallType, height));
                        WallCreate(doc, WallArgs(px, cy2, px, y1, name, wallType, height));
                        nWalls += 2;
                    }
                    doc.Regenerate();

                    FloorCreate(doc, new Dictionary<string, object>
                    {
                        { "points", new object[] { new object[] { x0, y0 }, new object[] { x1, y0 }, new object[] { x1, y1 }, new object[] { x0, y1 } } },
                        { "level", name }, { "type", floorType }
                    });
                    nSlabs++;

                    if (makeColumns)
                    {
                        for (int i = 0; i <= bays; i++)
                        {
                            double gx = x0 + i * step;
                            foreach (double gy in new[] { y0, cy1, cy2, y1 })
                            {
                                try
                                {
                                    var colResult = ColumnCreate(doc, new Dictionary<string, object>
                                    {
                                        { "point", new object[] { gx, gy } }, { "level", name }, { "type", columnType }
                                    }) as Dictionary<string, object>;
                                    if (colResult != null)
                                    {
                                        int colId = Convert.ToInt32(colResult["id"], CultureInfo.InvariantCulture);
                                        FamilyInstance fi = doc.GetElement(new ElementId(colId)) as FamilyInstance;
                                        if (fi != null)
                                        {
                                            Parameter top = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                                            if (top != null && !top.IsReadOnly) top.Set(height * MmToFt);
                                        }
                                        nColumns++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    failures.Add("column " + name + " (" + gx + "," + gy + "): " + ex.Message);
                                }
                            }
                        }
                    }
                    doc.Regenerate();

                    for (int i = 0; i < bays; i++)
                    {
                        double cx = x0 + (i + 0.5) * step;

                        if (makeDoors)
                        {
                            try
                            {
                                HostedCreate(doc, new Dictionary<string, object>
                                {
                                    { "wall_id", corridorSouth }, { "point", new object[] { cx, cy1 } }, { "type", doorType }, { "level", name }
                                }, BuiltInCategory.OST_Doors, "door");
                                nDoors++;
                            }
                            catch (Exception ex) { failures.Add("door S" + (i + 1) + " @" + name + ": " + ex.Message); }

                            try
                            {
                                HostedCreate(doc, new Dictionary<string, object>
                                {
                                    { "wall_id", corridorNorth }, { "point", new object[] { cx, cy2 } }, { "type", doorType }, { "level", name }
                                }, BuiltInCategory.OST_Doors, "door");
                                nDoors++;
                            }
                            catch (Exception ex) { failures.Add("door N" + (i + 1) + " @" + name + ": " + ex.Message); }
                        }

                        if (makeWindows)
                        {
                            try
                            {
                                HostedCreate(doc, new Dictionary<string, object>
                                {
                                    { "wall_id", south }, { "point", new object[] { cx, y0 } }, { "type", windowType }, { "level", name }
                                }, BuiltInCategory.OST_Windows, "window");
                                nWindows++;
                            }
                            catch (Exception ex) { failures.Add("window S" + (i + 1) + " @" + name + ": " + ex.Message); }

                            try
                            {
                                HostedCreate(doc, new Dictionary<string, object>
                                {
                                    { "wall_id", north }, { "point", new object[] { cx, y1 } }, { "type", windowType }, { "level", name }
                                }, BuiltInCategory.OST_Windows, "window");
                                nWindows++;
                            }
                            catch (Exception ex) { failures.Add("window N" + (i + 1) + " @" + name + ": " + ex.Message); }
                        }

                        if (makeRooms)
                        {
                            try
                            {
                                var southRoom = RoomCreate(doc, new Dictionary<string, object>
                                {
                                    { "point", new object[] { cx, (y0 + cy1) / 2.0 } }, { "level", name }, { "height_mm", height },
                                    { "name", RoomLabel(blockName, name, "S", i + 1) }
                                }) as Dictionary<string, object>;
                                if (southRoom != null) { roomSummaries.Add(southRoom); nRooms++; }
                            }
                            catch (Exception ex) { failures.Add("room S" + (i + 1) + " @" + name + ": " + ex.Message); }

                            try
                            {
                                var northRoom = RoomCreate(doc, new Dictionary<string, object>
                                {
                                    { "point", new object[] { cx, (cy2 + y1) / 2.0 } }, { "level", name }, { "height_mm", height },
                                    { "name", RoomLabel(blockName, name, "N", i + 1) }
                                }) as Dictionary<string, object>;
                                if (northRoom != null) { roomSummaries.Add(northRoom); nRooms++; }
                            }
                            catch (Exception ex) { failures.Add("room N" + (i + 1) + " @" + name + ": " + ex.Message); }
                        }
                    }

                    levelInfos.Add(new Dictionary<string, object>
                    {
                        { "name", name }, { "elevation_mm", elev }, { "height_mm", height }
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("level '" + name + "': " + ex.Message, ex);
                }
            }

            if (makeRoof)
            {
                string roofName = string.IsNullOrWhiteSpace(blockName) ? "屋顶" : "屋顶 " + blockName;
                EnsureLevel(doc, roofName, topElevation);
                FloorCreate(doc, new Dictionary<string, object>
                {
                    { "points", new object[] { new object[] { x0, y0 }, new object[] { x1, y0 }, new object[] { x1, y1 }, new object[] { x0, y1 } } },
                    { "level", roofName }, { "type", floorType }
                });
                nSlabs++;
            }

            double totalArea = roomSummaries.Sum(r => Convert.ToDouble(((Dictionary<string, object>)r)["area_m2"], CultureInfo.InvariantCulture));

            return new Dictionary<string, object>
            {
                { "block", new Dictionary<string, object>
                    {
                        { "origin_mm", new object[] { x0, y0 } }, { "length_mm", length }, { "depth_mm", depth },
                        { "bays", bays }, { "bay_mm", Math.Round(step, 1) }, { "corridor_mm", corridor }, { "floors", levelSpecs.Length }
                    }
                },
                { "levels", levelInfos },
                { "counts", new Dictionary<string, object>
                    {
                        { "walls", nWalls }, { "doors", nDoors }, { "windows", nWindows },
                        { "columns", nColumns }, { "rooms", nRooms }, { "slabs", nSlabs }
                    }
                },
                { "total_room_area_m2", Math.Round(totalArea, 1) },
                { "sample_rooms", roomSummaries.Take(6).ToList() },
                { "failures", failures.Take(10).ToList() },
                { "failure_count", failures.Count }
            };
        }

        private static string RoomLabel(string block, string level, string side, int index)
        {
            string prefix = string.IsNullOrWhiteSpace(block) ? "" : block + "-";
            return "教室 " + prefix + level + "-" + side + index;
        }

        private static Dictionary<string, object> WallArgs(double ax, double ay, double bx, double by, string level, string type, double heightMm)
        {
            return new Dictionary<string, object>
            {
                { "p1", new object[] { ax, ay } }, { "p2", new object[] { bx, by } },
                { "level", level }, { "height_mm", heightMm }, { "type", type }
            };
        }

        private static int WallId(Document doc, double ax, double ay, double bx, double by, string level, string type, double heightMm)
        {
            var result = WallCreate(doc, WallArgs(ax, ay, bx, by, level, type, heightMm)) as Dictionary<string, object>;
            return Convert.ToInt32(result["id"], CultureInfo.InvariantCulture);
        }

        private static Level EnsureLevel(Document doc, string name, double elevationMm)
        {
            Level existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            Level level = Level.Create(doc, elevationMm * MmToFt);
            try { level.Name = name; } catch { }
            return level;
        }

        private static double[] MmPair(object raw, double dx, double dy)
        {
            var arr = raw as object[];
            if (arr == null || arr.Length < 2) return new[] { dx, dy };
            return new[]
            {
                Convert.ToDouble(arr[0], CultureInfo.InvariantCulture),
                Convert.ToDouble(arr[1], CultureInfo.InvariantCulture)
            };
        }

        private static bool Bool(IDictionary<string, object> args, string key, bool fallback)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }


        // ---------------- views.list ----------------

        private static object ViewsList(Document doc)
        {
            var views = new List<object>();
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name))
            {
                views.Add(new Dictionary<string, object>
                {
                    { "name", v.Name }, { "type", v.ViewType.ToString() }, { "id", v.Id.IntegerValue }
                });
            }
            return new Dictionary<string, object> { { "views", views } };
        }

        // ---------------- build.school ----------------

        private object BuildSchool(Document doc, IDictionary<string, object> args)
        {
            object blocksRaw;
            if (!args.TryGetValue("blocks", out blocksRaw) || blocksRaw == null)
                throw new ArgumentException("'blocks' is required: [ { name?, origin:[x,y], length_mm, depth_mm, levels:[...] }, ... ]");
            var blocks = blocksRaw as object[];
            if (blocks == null || blocks.Length == 0) throw new ArgumentException("'blocks' must be a non-empty array");

            bool site = Bool(args, "site", true);
            double margin = Num(args, "site_margin_mm", 12000);
            string siteFloorType = Str(args, "site_floor_type", null);

            var blockResults = new List<object>();
            int walls = 0, doors = 0, windows = 0, columns = 0, rooms = 0, slabs = 0, failures = 0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            double totalArea = 0;
            string baseLevel = null;

            foreach (object item in blocks)
            {
                var spec = item as Dictionary<string, object>;
                if (spec == null) throw new ArgumentException("each block must be an object");

                var result = BuildBlock(doc, spec) as Dictionary<string, object>;
                blockResults.Add(result);

                var counts = result["counts"] as Dictionary<string, object>;
                walls += Convert.ToInt32(counts["walls"], CultureInfo.InvariantCulture);
                doors += Convert.ToInt32(counts["doors"], CultureInfo.InvariantCulture);
                windows += Convert.ToInt32(counts["windows"], CultureInfo.InvariantCulture);
                columns += Convert.ToInt32(counts["columns"], CultureInfo.InvariantCulture);
                rooms += Convert.ToInt32(counts["rooms"], CultureInfo.InvariantCulture);
                slabs += Convert.ToInt32(counts["slabs"], CultureInfo.InvariantCulture);
                failures += Convert.ToInt32(result["failure_count"], CultureInfo.InvariantCulture);
                totalArea += Convert.ToDouble(result["total_room_area_m2"], CultureInfo.InvariantCulture);

                var blockInfo = result["block"] as Dictionary<string, object>;
                var origin = (object[])blockInfo["origin_mm"];
                double bx = Convert.ToDouble(origin[0], CultureInfo.InvariantCulture);
                double by = Convert.ToDouble(origin[1], CultureInfo.InvariantCulture);
                double bw = Convert.ToDouble(blockInfo["length_mm"], CultureInfo.InvariantCulture);
                double bd = Convert.ToDouble(blockInfo["depth_mm"], CultureInfo.InvariantCulture);
                minX = Math.Min(minX, bx); minY = Math.Min(minY, by);
                maxX = Math.Max(maxX, bx + bw); maxY = Math.Max(maxY, by + bd);

                if (baseLevel == null)
                {
                    var levels = result["levels"] as List<object>;
                    if (levels != null && levels.Count > 0)
                    {
                        baseLevel = Convert.ToString(((Dictionary<string, object>)levels[0])["name"]);
                    }
                }
            }

            var siteInfo = new Dictionary<string, object>();
            if (site && baseLevel != null)
            {
                double sx0 = minX - margin, sy0 = minY - margin, sx1 = maxX + margin, sy1 = maxY + margin;
                FloorCreate(doc, new Dictionary<string, object>
                {
                    { "points", new object[] { new object[] { sx0, sy0 }, new object[] { sx1, sy0 }, new object[] { sx1, sy1 }, new object[] { sx0, sy1 } } },
                    { "level", baseLevel }, { "type", siteFloorType }
                });
                slabs++;
                siteInfo["rect_mm"] = new object[] { sx0, sy0, sx1, sy1 };
                siteInfo["area_m2"] = Math.Round((sx1 - sx0) * (sy1 - sy0) / 1000000.0, 1);
            }

            return new Dictionary<string, object>
            {
                { "blocks", blockResults.Count },
                { "counts", new Dictionary<string, object>
                    {
                        { "walls", walls }, { "doors", doors }, { "windows", windows },
                        { "columns", columns }, { "rooms", rooms }, { "slabs", slabs }
                    }
                },
                { "total_room_area_m2", Math.Round(totalArea, 1) },
                { "site", siteInfo },
                { "failure_count", failures }
            };
        }

        // ---------------- view.export ----------------

        private static object ViewExport(UIApplication app, Document doc, IDictionary<string, object> args)
        {
            string prefix = Str(args, "path", null);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "revitai_view");
            }
            string dir = System.IO.Path.GetDirectoryName(prefix);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

            View view = doc.ActiveView;
            string viewName = Str(args, "view", null);
            string viewType = Str(args, "view_type", null);

            if (!string.IsNullOrWhiteSpace(viewName))
            {
                string wanted = NormalizeViewName(viewName);
                View found = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => NormalizeViewName(v.Name) == wanted);
                if (found != null) view = found;
            }
            else if (!string.IsNullOrWhiteSpace(viewType))
            {
                if (string.Equals(viewType, "3d", StringComparison.OrdinalIgnoreCase))
                {
                    View found = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate);
                    if (found != null) view = found;
                }
            }

            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.CurrentView,
                FilePath = prefix,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1600
            };

            if (view.Id != doc.ActiveView.Id && app.ActiveUIDocument != null)
            {
                try { app.ActiveUIDocument.ActiveView = view; } catch { }
            }

            DisplayStyle oldStyle = DisplayStyle.Undefined;
            bool styleChanged = false;
            if (Bool(args, "shaded", false))
            {
                try
                {
                    oldStyle = view.DisplayStyle;
                    RunTx(doc, "RevitAI: export style", delegate { view.DisplayStyle = DisplayStyle.ShadingWithEdges; return null; });
                    styleChanged = true;
                }
                catch { }
            }

            bool cropWasActive = view.CropBoxActive;
            if (cropWasActive)
            {
                RunTx(doc, "RevitAI: export (crop off)", delegate { view.CropBoxActive = false; return null; });
            }
            try
            {
                doc.ExportImage(options);
            }
            finally
            {
                if (cropWasActive)
                {
                    RunTx(doc, "RevitAI: export (crop on)", delegate { view.CropBoxActive = true; return null; });
                }
                if (styleChanged)
                {
                    try { RunTx(doc, "RevitAI: export (style back)", delegate { view.DisplayStyle = oldStyle; return null; }); } catch { }
                }
            }

            string stem = System.IO.Path.GetFileName(prefix);
            var files = System.IO.Directory.GetFiles(dir, stem + "*")
                .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                .ToList();

            return new Dictionary<string, object>
            {
                { "view", view.Name }, { "files", files.Take(3).ToList() }
            };
        }

        private static string NormalizeViewName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return new string(name.Where(ch => ch != '{' && ch != '}' && ch != ' ' && ch != '　').ToArray()).ToLowerInvariant();
        }
    }
}
