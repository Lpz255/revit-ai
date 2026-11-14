using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace RevitAI
{
    internal sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor msg in failuresAccessor.GetFailureMessages())
            {
                if (msg.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(msg);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    internal sealed partial class RevitCommandHandler : IBridgeHandler
    {
        private const double MmToFt = 1.0 / 304.8;
        private readonly RevitEventBridge _bridge;

        private static readonly HashSet<string> NoTransaction = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hello", "levels.list", "types.list", "views.list", "view.export", "apply_plan"
        };

        private static readonly Dictionary<string, int> Timeouts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "hello", 30000 }, { "levels.list", 30000 }, { "types.list", 30000 },
            { "apply_plan", 300000 }, { "build.block", 900000 }, { "build.school", 1500000 }
        };

        public RevitCommandHandler(RevitEventBridge bridge)
        {
            _bridge = bridge;
        }

        public object Handle(string cmd, IDictionary<string, object> args)
        {
            if (string.Equals(cmd, "ping", StringComparison.OrdinalIgnoreCase)) return Ping();
            if (string.Equals(cmd, "help", StringComparison.OrdinalIgnoreCase)) return Help();

            int timeout;
            if (!Timeouts.TryGetValue(cmd, out timeout)) timeout = 60000;
            return _bridge.Run(app => Exec(cmd, app, args), timeout);
        }

        private static object Ping()
        {
            return new Dictionary<string, object>
            {
                { "pong", true }, { "server", "revit-ai" }, { "version", BuildInfo.Version }, { "in_revit", true }
            };
        }

        private static object Help()
        {
            return new Dictionary<string, object>
            {
                { "commands", new List<string>
                    {
                        "ping", "help", "hello", "levels.list", "types.list",
                        "level.create", "grid.create", "wall.create", "floor.create", "column.create",
                        "door.create", "window.create", "room.create", "family.place", "element.delete",
                        "apply_plan", "build.block", "build.school", "views.list", "view.export"
                    }
                },
                { "units", "all lengths are millimetres (mm)" },
                { "apply_plan", "{ ops: [ { cmd, args }, ... ] } -> one transaction; any failure rolls back the whole plan" }
            };
        }

        // ---------------- dispatch ----------------

        private object Exec(string cmd, UIApplication app, IDictionary<string, object> args)
        {
            if (!NoTransaction.Contains(cmd))
            {
                Document doc = Doc(app);
                return RunTx(doc, "RevitAI: " + cmd, delegate { return ExecInner(cmd, app, args); });
            }
            return ExecInner(cmd, app, args);
        }

        private object ExecInner(string cmd, UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            switch (cmd)
            {
                case "hello": return Hello(app, doc);
                case "levels.list": return new Dictionary<string, object> { { "levels", Levels(doc) } };
                case "types.list": return TypesList(doc, args);

                case "level.create": return LevelCreate(doc, args);
                case "grid.create": return GridCreate(doc, args);
                case "wall.create": return WallCreate(doc, args);
                case "floor.create": return FloorCreate(doc, args);
                case "column.create": return ColumnCreate(doc, args);
                case "door.create": return HostedCreate(doc, args, BuiltInCategory.OST_Doors, "door");
                case "window.create": return HostedCreate(doc, args, BuiltInCategory.OST_Windows, "window");
                case "room.create": return RoomCreate(doc, args);
                case "family.place": return FamilyPlace(doc, args);
                case "element.delete": return ElementDelete(doc, args);
                case "apply_plan": return ApplyPlan(app, doc, args);
                case "views.list": return ViewsList(doc);
                case "build.block": return BuildBlock(doc, args);
                case "build.school": return BuildSchool(doc, args);
                case "view.export": return ViewExport(app, doc, args);

                default:
                    throw new ArgumentException("unknown command '" + cmd + "' (run 'help' for the list)");
            }
        }

        // ---------------- queries ----------------

        private static object Hello(UIApplication app, Document doc)
        {
            var data = new Dictionary<string, object>();
            data["revit_version"] = app.Application.VersionNumber;
            data["revit_build"] = app.Application.VersionBuild;
            data["username"] = app.Application.Username;
            data["document"] = new Dictionary<string, object>
            {
                { "title", doc.Title }, { "path", doc.PathName },
                { "is_modified", doc.IsModified }, { "is_family", doc.IsFamilyDocument }
            };
            data["levels"] = Levels(doc);
            data["element_count"] = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            return data;
        }

        private static List<object> Levels(Document doc)
        {
            var levels = new List<object>();
            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                levels.Add(new Dictionary<string, object>
                {
                    { "name", level.Name }, { "elevation_mm", Round(level.Elevation / MmToFt) }
                });
            }
            return levels;
        }

        private static object TypesList(Document doc, IDictionary<string, object> args)
        {
            string category = Str(args, "category", "walls");
            switch (category.ToLowerInvariant())
            {
                case "walls":
                    return Wrap("walls", new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .Where(w => w.Kind == WallKind.Basic).Select(w => (object)w.Name).OrderBy(n => n).ToList());
                case "floors":
                    return Wrap("floors", new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                        .Where(f => !f.IsFoundationSlab).Select(f => (object)f.Name).OrderBy(n => n).ToList());
                case "columns":
                    return Wrap("columns", Symbols(doc, BuiltInCategory.OST_StructuralColumns));
                case "doors":
                    return Wrap("doors", Symbols(doc, BuiltInCategory.OST_Doors));
                case "windows":
                    return Wrap("windows", Symbols(doc, BuiltInCategory.OST_Windows));
                default:
                    throw new ArgumentException("unknown category '" + category + "' (walls|floors|columns|doors|windows)");
            }
        }

        private static object Wrap(string category, List<object> types)
        {
            return new Dictionary<string, object> { { "category", category }, { "types", types } };
        }

        private static List<object> Symbols(Document doc, BuiltInCategory bic)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(bic).Cast<FamilySymbol>()
                .Select(s => (object)(s.Family.Name + ":" + s.Name)).OrderBy(n => n).ToList();
        }

        // ---------------- modelling (caller owns the transaction) ----------------

        private static object LevelCreate(Document doc, IDictionary<string, object> args)
        {
            string name = Str(args, "name", null);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("'name' is required");
            double elevationMm = Num(args, "elevation_mm", double.NaN);
            if (double.IsNaN(elevationMm)) throw new ArgumentException("'elevation_mm' is required");

            Level level = Level.Create(doc, elevationMm * MmToFt);
            if (!string.Equals(level.Name, name, StringComparison.Ordinal)) level.Name = name;
            return new Dictionary<string, object>
            {
                { "id", level.Id.IntegerValue }, { "name", level.Name }, { "elevation_mm", Round(level.Elevation / MmToFt) }
            };
        }

        private static object GridCreate(Document doc, IDictionary<string, object> args)
        {
            XYZ p1 = Point(args, "p1");
            XYZ p2 = Point(args, "p2");
            string name = Str(args, "name", null);
            if (p1.DistanceTo(p2) < 10 * MmToFt) throw new ArgumentException("p1 and p2 are too close (min 10 mm)");

            Grid grid = Grid.Create(doc, Line.CreateBound(p1, p2));
            if (!string.IsNullOrWhiteSpace(name))
            {
                try { grid.Name = name; } catch { }
            }
            return new Dictionary<string, object> { { "id", grid.Id.IntegerValue }, { "name", grid.Name } };
        }

        private static object WallCreate(Document doc, IDictionary<string, object> args)
        {
            XYZ p1 = Point(args, "p1");
            XYZ p2 = Point(args, "p2");
            double heightMm = Num(args, "height_mm", 3000);
            double baseMm = Num(args, "base_offset_mm", 0);
            Level level = ResolveLevel(doc, Str(args, "level", null));
            WallType type = ResolveWallType(doc, Str(args, "type", null));
            if (p1.DistanceTo(p2) < 10 * MmToFt) throw new ArgumentException("p1 and p2 are too close (min 10 mm)");
            if (heightMm <= 0) throw new ArgumentException("'height_mm' must be > 0");

            Curve curve = Line.CreateBound(p1, p2);
            Wall wall = Wall.Create(doc, curve, type.Id, level.Id, heightMm * MmToFt, baseMm * MmToFt, false, false);
            return new Dictionary<string, object>
            {
                { "id", wall.Id.IntegerValue }, { "type", type.Name }, { "level", level.Name },
                { "length_mm", Round(curve.Length / MmToFt) }, { "height_mm", Round(heightMm) }
            };
        }

        private static object FloorCreate(Document doc, IDictionary<string, object> args)
        {
            List<XYZ> points = Points(args, "points");
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FloorType type = ResolveFloorType(doc, Str(args, "type", null));
            if (points.Count < 3) throw new ArgumentException("'points' needs at least 3 XY points");

            var profile = new CurveArray();
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                if (a.DistanceTo(b) < 1 * MmToFt) continue;
                profile.Append(Line.CreateBound(a, b));
            }
            Floor floor = doc.Create.NewFloor(profile, type, level, false);
            return new Dictionary<string, object>
            {
                { "id", floor.Id.IntegerValue }, { "type", type.Name }, { "level", level.Name }, { "vertex_count", points.Count }
            };
        }

        private static object ColumnCreate(Document doc, IDictionary<string, object> args)
        {
            XYZ point = Point(args, "point");
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FamilySymbol symbol = ResolveSymbol(doc, Str(args, "type", null), BuiltInCategory.OST_StructuralColumns);

            Activate(doc, symbol);
            FamilyInstance column = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.Column);
            return new Dictionary<string, object>
            {
                { "id", column.Id.IntegerValue }, { "type", symbol.Family.Name + ":" + symbol.Name }, { "level", level.Name }
            };
        }

        private static object HostedCreate(Document doc, IDictionary<string, object> args, BuiltInCategory category, string kind)
        {
            int wallId = Int(args, "wall_id", 0);
            if (wallId <= 0) throw new ArgumentException("'wall_id' is required (id returned by wall.create)");

            Wall wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall == null) throw new ArgumentException("wall " + wallId + " not found");

            XYZ point = Point(args, "point");
            FamilySymbol symbol = ResolveSymbol(doc, Str(args, "type", null), category);

            Level level = null;
            string levelName = Str(args, "level", null);
            if (!string.IsNullOrWhiteSpace(levelName)) level = ResolveLevel(doc, levelName);
            if (level == null) level = doc.GetElement(wall.LevelId) as Level;
            if (level == null) level = ResolveLevel(doc, null);

            XYZ onWall = point;
            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve != null)
            {
                IntersectionResult projection = locationCurve.Curve.Project(point);
                if (projection != null) onWall = projection.XYZPoint;
            }

            Activate(doc, symbol);
            FamilyInstance instance = doc.Create.NewFamilyInstance(onWall, symbol, wall, level, StructuralType.NonStructural);
            return new Dictionary<string, object>
            {
                { "id", instance.Id.IntegerValue }, { "type", symbol.Family.Name + ":" + symbol.Name },
                { "host_wall", wallId }, { "level", level.Name }, { "kind", kind }
            };
        }

        private static object RoomCreate(Document doc, IDictionary<string, object> args)
        {
            XYZ point = Point(args, "point");
            Level level = ResolveLevel(doc, Str(args, "level", null));
            string name = Str(args, "name", null);
            string number = Str(args, "number", null);

            Level upperLevel = null;
            string upperName = Str(args, "upper_level", null);
            if (!string.IsNullOrWhiteSpace(upperName)) upperLevel = ResolveLevel(doc, upperName);
            double roomHeightMm = Num(args, "height_mm", 0);

            Room room = doc.Create.NewRoom(level, new UV(point.X, point.Y));
            if (upperLevel != null) room.UpperLimit = upperLevel;
            if (roomHeightMm > 0) room.LimitOffset = roomHeightMm * MmToFt;
            if (!string.IsNullOrWhiteSpace(number)) SetParameter(room, BuiltInParameter.ROOM_NUMBER, number);
            if (!string.IsNullOrWhiteSpace(name)) SetParameter(room, BuiltInParameter.ROOM_NAME, name);

            double areaM2 = Math.Round(room.Area * 0.09290304, 2);
            var result = new Dictionary<string, object>
            {
                { "id", room.Id.IntegerValue }, { "name", room.Name }, { "number", room.Number },
                { "level", level.Name }, { "area_m2", areaM2 }
            };
            if (areaM2 <= 0.01)
            {
                result["warning"] = "room has no area: the point is not fully enclosed by walls on this level";
            }
            return result;
        }

        private static object FamilyPlace(Document doc, IDictionary<string, object> args)
        {
            XYZ point = Point(args, "point");
            double rotationDeg = Num(args, "rotation_deg", 0);
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FamilySymbol symbol = ResolveSymbol(doc, Str(args, "type", null), BuiltInCategory.OST_Doors);

            Activate(doc, symbol);
            FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
            if (Math.Abs(rotationDeg) > 0.001)
            {
                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, instance.Id, axis, rotationDeg * Math.PI / 180.0);
            }
            return new Dictionary<string, object>
            {
                { "id", instance.Id.IntegerValue }, { "type", symbol.Family.Name + ":" + symbol.Name }, { "level", level.Name }
            };
        }

        private static object ElementDelete(Document doc, IDictionary<string, object> args)
        {
            List<int> ids = Ints(args, "ids");
            if (ids.Count == 0) throw new ArgumentException("'ids' is required");

            var deleted = new List<object>();
            foreach (int id in ids)
            {
                ElementId eid = new ElementId(id);
                if (doc.GetElement(eid) == null) continue;
                doc.Delete(eid);
                deleted.Add(id);
            }
            return new Dictionary<string, object> { { "deleted", deleted }, { "count", deleted.Count } };
        }

        private object ApplyPlan(UIApplication app, Document doc, IDictionary<string, object> args)
        {
            object raw;
            if (!args.TryGetValue("ops", out raw) || raw == null) throw new ArgumentException("'ops' array is required");
            var ops = raw as object[];
            if (ops == null || ops.Length == 0) throw new ArgumentException("'ops' must be a non-empty array");

            return RunTx(doc, "RevitAI: apply_plan", delegate
            {
                var results = new List<object>();
                foreach (object item in ops)
                {
                    var spec = item as Dictionary<string, object>;
                    if (spec == null) throw new ArgumentException("each op must be an object like { cmd, args }");

                    object cmdValue;
                    string cmd = spec.TryGetValue("cmd", out cmdValue) && cmdValue != null ? Convert.ToString(cmdValue) : null;
                    if (string.IsNullOrWhiteSpace(cmd)) throw new ArgumentException("op is missing 'cmd'");
                    if (string.Equals(cmd, "apply_plan", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("apply_plan cannot be nested");

                    IDictionary<string, object> opArgs = null;
                    object argsValue;
                    if (spec.TryGetValue("args", out argsValue)) opArgs = argsValue as IDictionary<string, object>;

                    results.Add(ExecInner(cmd, app, opArgs ?? new Dictionary<string, object>()));
                }
                return new Dictionary<string, object> { { "count", results.Count }, { "results", results } };
            });
        }

        // ---------------- helpers ----------------

        private static Document Doc(UIApplication app)
        {
            Document doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;
            if (doc == null) throw new InvalidOperationException("no active document: open a project (.rvt) first");
            if (doc.IsFamilyDocument) throw new InvalidOperationException("active document is a family (.rfa), open a project (.rvt)");
            return doc;
        }

        private static void Activate(Document doc, FamilySymbol symbol)
        {
            if (symbol.IsActive) return;
            symbol.Activate();
            doc.Regenerate();
        }

        private static void SetParameter(Element element, BuiltInParameter bic, string value)
        {
            Parameter parameter = element.get_Parameter(bic);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set(value);
        }

        private static object RunTx(Document doc, string title, Func<object> body)
        {
            using (Transaction tx = new Transaction(doc, title))
            {
                FailureHandlingOptions options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                tx.SetFailureHandlingOptions(options);

                tx.Start();
                try
                {
                    object result = body();
                    tx.Commit();
                    return result;
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    throw;
                }
            }
        }

        private static Level ResolveLevel(Document doc, string name)
        {
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0) throw new InvalidOperationException("document has no levels");
            if (string.IsNullOrWhiteSpace(name)) return levels[0];

            Level level = levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? levels.FirstOrDefault(l => l.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (level == null)
            {
                throw new ArgumentException("level '" + name + "' not found; available: " + string.Join(", ", levels.Select(l => l.Name)));
            }
            return level;
        }

        private static WallType ResolveWallType(Document doc, string name)
        {
            List<WallType> types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .Where(w => w.Kind == WallKind.Basic).ToList();
            if (types.Count == 0) throw new InvalidOperationException("document has no basic wall types");
            if (string.IsNullOrWhiteSpace(name)) return types.First();
            WallType type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? types.FirstOrDefault(t => t.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (type == null) throw new ArgumentException("wall type '" + name + "' not found; run types.list category=walls");
            return type;
        }

        private static FloorType ResolveFloorType(Document doc, string name)
        {
            List<FloorType> types = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .Where(f => !f.IsFoundationSlab).ToList();
            if (types.Count == 0) throw new InvalidOperationException("document has no floor types");
            if (string.IsNullOrWhiteSpace(name)) return types.First();
            FloorType type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type == null) throw new ArgumentException("floor type '" + name + "' not found; run types.list category=floors");
            return type;
        }

        private static FamilySymbol ResolveSymbol(Document doc, string typeName, BuiltInCategory fallbackCategory)
        {
            List<FamilySymbol> symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            if (symbols.Count == 0) throw new InvalidOperationException("document has no loaded families");

            if (string.IsNullOrWhiteSpace(typeName))
            {
                FamilySymbol any = symbols.FirstOrDefault(s => s.Category != null && s.Category.Id.IntegerValue == (int)fallbackCategory);
                if (any == null) throw new ArgumentException("no symbol in category " + fallbackCategory + "; pass 'type' explicitly");
                return any;
            }

            FamilySymbol symbol = symbols.FirstOrDefault(s => string.Equals(s.Family.Name + ":" + s.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? symbols.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? symbols.FirstOrDefault(s => string.Equals(s.Family.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (symbol == null) throw new ArgumentException("family type '" + typeName + "' not found; run types.list");
            return symbol;
        }

        private static XYZ Point(IDictionary<string, object> args, string key)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) throw new ArgumentException("'" + key + "' is required");
            return ToXyz(value);
        }

        private static List<XYZ> Points(IDictionary<string, object> args, string key)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) throw new ArgumentException("'" + key + "' is required");
            var arr = value as object[];
            if (arr == null) throw new ArgumentException("'" + key + "' must be an array of [x, y] points");
            return arr.Select(ToXyz).ToList();
        }

        private static XYZ ToXyz(object value)
        {
            var arr = value as object[];
            if (arr == null || arr.Length < 2) throw new ArgumentException("point must be [x, y] in mm");
            double x = Convert.ToDouble(arr[0], CultureInfo.InvariantCulture);
            double y = Convert.ToDouble(arr[1], CultureInfo.InvariantCulture);
            double z = arr.Length > 2 ? Convert.ToDouble(arr[2], CultureInfo.InvariantCulture) : 0;
            return new XYZ(x * MmToFt, y * MmToFt, z * MmToFt);
        }

        private static string Str(IDictionary<string, object> args, string key, string fallback)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) return fallback;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static double Num(IDictionary<string, object> args, string key, double fallback)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static int Int(IDictionary<string, object> args, string key, int fallback)
        {
            object value;
            if (!args.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static List<int> Ints(IDictionary<string, object> args, string key)
        {
            var result = new List<int>();
            object value;
            if (!args.TryGetValue(key, out value) || value == null) return result;
            var arr = value as object[];
            if (arr != null)
            {
                foreach (object item in arr) result.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                return result;
            }
            result.Add(Convert.ToInt32(value, CultureInfo.InvariantCulture));
            return result;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 1);
        }
    }
}
