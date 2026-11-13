using System.Collections.Generic;

namespace RevitAI
{
    internal interface IBridgeHandler
    {
        object Handle(string cmd, IDictionary<string, object> args);
    }
}
