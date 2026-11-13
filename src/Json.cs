using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace RevitAI
{
    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = 16 * 1024 * 1024
        };

        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        public static Dictionary<string, object> DeserializeObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
            object parsed = Serializer.DeserializeObject(json);
            var dict = parsed as Dictionary<string, object>;
            return dict ?? new Dictionary<string, object>();
        }
    }
}
