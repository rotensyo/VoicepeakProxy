using System;
using System.IO;
using System.Web.Script.Serialization;

namespace BouyomiVoicepeakBridge.Shared
{
    // JSONファイルの読み書きを提供
    public static class JsonFileStore
    {
        // JSONファイルを読み込む
        public static T LoadOrDefault<T>(string filePath, Func<T> defaultFactory) where T : class
        {
            if (defaultFactory == null)
            {
                throw new ArgumentNullException("defaultFactory");
            }

            if (string.IsNullOrEmpty(filePath))
            {
                return defaultFactory();
            }

            if (!File.Exists(filePath))
            {
                return defaultFactory();
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
            {
                return defaultFactory();
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            T value = serializer.Deserialize<T>(json);
            return value ?? defaultFactory();
        }

        // JSONファイルへ保存
        public static void Save<T>(string filePath, T value) where T : class
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("filePath は空にできません", "filePath");
            }

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(value);
            File.WriteAllText(filePath, json);
        }
    }
}
