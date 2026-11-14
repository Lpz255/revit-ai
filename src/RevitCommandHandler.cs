using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
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

    internal sealed class RevitCommandHandler : IBridgeHandler
    {
        private const double MmToFt = 1.0 / 304.8;
        private readonly RevitEventBridge _bridge;

        public RevitCommandHandler(RevitEventBridge bridge)
        {
            _bridge = bridge;
        }

        public object Handle(string cmd, IDictionary<string, object> args)
        {
            switch (cmd)
            {
                case "ping": return Ping();
                case "help": return Help();

                case "hello": return _bridge.Run(app => Hello(app, args), 20000);
                case "levels.list": return _bridge.Run(app => LevelsList(app), 20000);
                case "types.list": return _bridge.Run(app => TypesList(app, args), 20000);

                case "level.create": return _bridge.Run(app => LevelCreate(app, args), 30000);
                case "grid.create": return _bridge.Run(app => GridCreate(app, args), 30000);
                case "wall.create": return _bridge.Run(app => WallCreate(app, args), 30000);
                case "floor.create": return _bridge.Run(app => FloorCreate(app, args), 30000);
                case "column.create": return _bridge.Run(app => ColumnCreate(app, args), 30000);
                case "family.place": return _bridge.Run(app => FamilyPlace(app, args), 30000);
                case "element.delete": return _bridge.Run(app => ElementDelete(app, args), 30000);

                default:
                    throw new ArgumentException("unknown command '" + cmd + "' (run 'help' for the list)");
            }
        }

        // ---------------- meta ----------------

        private static object Ping()
        {
            return new Dictionary<string, object>
            {
                { "pong", true },
                { "server", "revit-ai" },
                { "version", BuildInfo.Version },
                { "in_revit", true }
            };
        }

        private static object Help()
        {
            return new Dictionary<string, object>
            {
                { "commands", new List<string>
                    {
                        "ping", "help", "hello", "levels.list", "types.list",
                        "level.create", "grid.create", "wall.create", "floor.create",
                        "column.create", "family.place", "element.delete"
                    }
                },
                { "units", "all lengths are millimetres (mm)" },
                { "note", "every mutating command runs in one Revit transaction and rolls back on error" }
            };
        }

        private static object Hello(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            var data = new Dictionary<string, object>();
            data["revit_version"] = app.Application.VersionNumber;
            data["revit_build"] = app.Application.VersionBuild;
            data["username"] = app.Application.Username;
            data["document"] = new Dictionary<string, object>
            {
                { "title", doc.Title },
                { "path", doc.PathName },
                { "is_modified", doc.IsModified },
                { "is_family", doc.IsFamilyDocument }
            };
            data["levels"] = Levels(doc);
            data["element_count"] = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            return data;
        }

        private static object LevelsList(UIApplication app)
        {
            Document doc = Doc(app);
            return new Dictionary<string, object> { { "levels", Levels(doc) } };
        }

        private static List<object> Levels(Document doc)
        {
            var levels = new List<object>();
            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                levels.Add(new Dictionary<string, object>
                {
                    { "name", level.Name },
                    { "elevation_mm", Round(level.Elevation / MmToFt) }
                });
            }
            return levels;
        }

        private static object TypesList(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            string category = Str(args, "category", "walls");

            switch (category.ToLowerInvariant())
            {
                case "walls":
                    return new Dictionary<string, object>
                    {
                        { "category", "walls" },
                        { "types", new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                            .Where(w => w.Kind == WallKind.Basic).Select(w => (object)w.Name).OrderBy(n => n).ToList() }
                    };
                case "floors":
                    return new Dictionary<string, object>
                    {
                        { "category", "floors" },
                        { "types", new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                            .Where(f => !f.IsFoundationSlab).Select(f => (object)f.Name).OrderBy(n => n).ToList() }
                    };
                case "columns":
                    return new Dictionary<string, object>
                    {
                        { "category", "columns" },
                        { "types", Symbols(doc, BuiltInCategory.OST_StructuralColumns) }
                    };
                case "doors":
                    return new Dictionary<string, object> { { "category", "doors" }, { "types", Symbols(doc, BuiltInCategory.OST_Doors) } };
                case "windows":
                    return new Dictionary<string, object> { { "category", "windows" }, { "types", Symbols(doc, BuiltInCategory.OST_Windows) } };
                default:
                    throw new ArgumentException("unknown category '" + category + "' (walls|floors|columns|doors|windows)");
            }
        }

        private static List<object> Symbols(Document doc, BuiltInCategory bic)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(bic)
                .Cast<FamilySymbol>()
                .Select(s => (object)(s.Family.Name + ":" + s.Name))
                .OrderBy(n => n)
                .ToList();
        }

        // ---------------- modelling ----------------

        private static object LevelCreate(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            string name = Str(args, "name", null);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("'name' is required");
            double elevationMm = Num(args, "elevation_mm", double.NaN);
            if (double.IsNaN(elevationMm)) throw new ArgumentException("'elevation_mm' is required");

            return RunTx(doc, "RevitAI: create level", delegate
            {
                Level level = Level.Create(doc, elevationMm * MmToFt);
                if (!string.Equals(level.Name, name, StringComparison.Ordinal)) level.Name = name;
                return new Dictionary<string, object>
                {
                    { "id", level.Id.IntegerValue },
                    { "name", level.Name },
                    { "elevation_mm", Round(level.Elevation / MmToFt) }
                };
            });
        }

        private static object GridCreate(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            XYZ p1 = Point(args, "p1");
            XYZ p2 = Point(args, "p2");
            string name = Str(args, "name", null);
            if (p1.DistanceTo(p2) < 10 * MmToFt) throw new ArgumentException("p1 and p2 are too close (min 10 mm)");

            return RunTx(doc, "RevitAI: create grid", delegate
            {
                Grid grid = Grid.Create(doc, Line.CreateBound(p1, p2));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    try { grid.Name = name; } catch { }
                }
                return new Dictionary<string, object>
                {
                    { "id", grid.Id.IntegerValue },
                    { "name", grid.Name }
                };
            });
        }

        private static object WallCreate(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            XYZ p1 = Point(args, "p1");
            XYZ p2 = Point(args, "p2");
            double heightMm = Num(args, "height_mm", 3000);
            Level level = ResolveLevel(doc, Str(args, "level", null));
            WallType type = ResolveWallType(doc, Str(args, "type", null));
            if (p1.DistanceTo(p2) < 10 * MmToFt) throw new ArgumentException("p1 and p2 are too close (min 10 mm)");
            if (heightMm <= 0) throw new ArgumentException("'height_mm' must be > 0");

            return RunTx(doc, "RevitAI: create wall", delegate
            {
                Curve curve = Line.CreateBound(p1, p2);
                Wall wall = Wall.Create(doc, curve, type.Id, level.Id, heightMm * MmToFt, 0, false, false);
                return new Dictionary<string, object>
                {
                    { "id", wall.Id.IntegerValue },
                    { "type", type.Name },
                    { "level", level.Name },
                    { "length_mm", Round(curve.Length / MmToFt) },
                    { "height_mm", Round(heightMm) }
                };
            });
        }

        private static object FloorCreate(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            List<XYZ> points = Points(args, "points");
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FloorType type = ResolveFloorType(doc, Str(args, "type", null));
            if (points.Count < 3) throw new ArgumentException("'points' needs at least 3 XY points");

            return RunTx(doc, "RevitAI: create floor", delegate
            {
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
                    { "id", floor.Id.IntegerValue },
                    { "type", type.Name },
                    { "level", level.Name },
                    { "vertex_count", points.Count }
                };
            });
        }

        private static object ColumnCreate(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            XYZ point = Point(args, "point");
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FamilySymbol symbol = ResolveSymbol(doc, Str(args, "type", null), BuiltInCategory.OST_StructuralColumns);

            return RunTx(doc, "RevitAI: create column", delegate
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }
                FamilyInstance column = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.Column);
                return new Dictionary<string, object>
                {
                    { "id", column.Id.IntegerValue },
                    { "type", symbol.Family.Name + ":" + symbol.Name },
                    { "level", level.Name }
                };
            });
        }

        private static object FamilyPlace(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            XYZ point = Point(args, "point");
            double rotationDeg = Num(args, "rotation_deg", 0);
            Level level = ResolveLevel(doc, Str(args, "level", null));
            FamilySymbol symbol = ResolveSymbol(doc, Str(args, "type", null), BuiltInCategory.OST_Doors);

            return RunTx(doc, "RevitAI: place family", delegate
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }
                FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
                if (Math.Abs(rotationDeg) > 0.001)
                {
                    Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, rotationDeg * Math.PI / 180.0);
                }
                return new Dictionary<string, object>
                {
                    { "id", instance.Id.IntegerValue },
                    { "type", symbol.Family.Name + ":" + symbol.Name },
                    { "level", level.Name }
                };
            });
        }

        private static object ElementDelete(UIApplication app, IDictionary<string, object> args)
        {
            Document doc = Doc(app);
            List<int> ids = Ints(args, "ids");
            if (ids.Count == 0) throw new ArgumentException("'ids' is required");

            return RunTx(doc, "RevitAI: delete elements", delegate
            {
                var deleted = new List<object>();
                foreach (int id in ids)
                {
                    ElementId eid = new ElementId(id);
                    Element element = doc.GetElement(eid);
                    if (element == null) continue;
                    doc.Delete(eid);
                    deleted.Add(id);
                }
                return new Dictionary<string, object> { { "deleted", deleted }, { "count", deleted.Count } };
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

            Level level = null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                level = levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
                if (level == null) level = levels.FirstOrDefault(l => l.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (level == null)
                {
                    throw new ArgumentException("level '" + name + "' not found; available: " +
                        string.Join(", ", levels.Select(l => l.Name)));
                }
            }
            else
            {
                level = levels.FirstOrDefault();
            }
            return level;
        }

        private static WallType ResolveWallType(Document doc, string name)
        {
            List<WallType> types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .Where(w => w.Kind == WallKind.Basic).ToList();
            if (types.Count == 0) throw new InvalidOperationException("document has no basic wall types");
            if (string.IsNullOrWhiteSpace(name)) return types.First();
            WallType type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type == null) type = types.FirstOrDefault(t => t.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (type == null) throw new ArgumentException("wall type '" + name + "' not found; available: " +
                string.Join(", ", types.Select(t => t.Name).Take(30)));
            return type;
        }

        private static FloorType ResolveFloorType(Document doc, string name)
        {
            List<FloorType> types = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .Where(f => !f.IsFoundationSlab).ToList();
            if (types.Count == 0) throw new InvalidOperationException("document has no floor types");
            if (string.IsNullOrWhiteSpace(name)) return types.First();
            FloorType type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type == null) throw new ArgumentException("floor type '" + name + "' not found; available: " +
                string.Join(", ", types.Select(t => t.Name).Take(30)));
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
