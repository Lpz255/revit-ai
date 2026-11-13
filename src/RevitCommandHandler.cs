using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI
{
    internal sealed class RevitCommandHandler : IBridgeHandler
    {
        private const double FeetToMm = 304.8;
        private readonly RevitEventBridge _bridge;

        public RevitCommandHandler(RevitEventBridge bridge)
        {
            _bridge = bridge;
        }

        public object Handle(string cmd, IDictionary<string, object> args)
        {
            switch (cmd)
            {
                case "ping":
                    return Ping();
                case "hello":
                    return _bridge.Run(Hello, 20000);
                default:
                    throw new ArgumentException("unknown command '" + cmd + "' (available: ping, hello)");
            }
        }

        private static object Ping()
        {
            return new Dictionary<string, object>
            {
                { "pong", true },
                { "server", "revit-ai" },
                { "version", BuildInfo.Version },
                { "pid", Process.GetCurrentProcess().Id },
                { "in_revit", true }
            };
        }

        private static object Hello(UIApplication app)
        {
            Document doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;
            if (doc == null)
            {
                throw new InvalidOperationException("no active document: open a project (.rvt) first");
            }

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

            var levels = new List<object>();
            var levelElements = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation);

            foreach (Level level in levelElements)
            {
                levels.Add(new Dictionary<string, object>
                {
                    { "name", level.Name },
                    { "elevation_mm", Math.Round(level.Elevation * FeetToMm, 1) }
                });
            }

            data["levels"] = levels;
            data["level_count"] = levels.Count;
            data["element_count"] = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
            return data;
        }
    }
}
