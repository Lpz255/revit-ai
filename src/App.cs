using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace RevitAI
{
    [Transaction(TransactionMode.Manual)]
    public class App : IExternalApplication
    {
        internal static RevitEventBridge Bridge;
        internal static BridgeServer Server;

        public Result OnStartup(UIControlledApplication application)
        {
            Log.Info("OnStartup begin (v" + BuildInfo.Version + ")");
            try
            {
                string token = TokenStore.LoadOrCreate();
                Bridge = new RevitEventBridge();
                Server = new BridgeServer(new RevitCommandHandler(Bridge), 8787, token);
                Server.Start();
                Log.Info("RevitAI ready on 127.0.0.1:8787; token file " + TokenStore.TokenPath);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("OnStartup failed", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (Server != null) Server.Stop();
                Log.Info("RevitAI stopped.");
            }
            catch (Exception ex)
            {
                Log.Error("OnShutdown failed", ex);
            }
            return Result.Succeeded;
        }
    }
}
