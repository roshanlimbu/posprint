using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PosPrintService.Models
{
    /// <summary>
    /// Represents the configuration settings for the POS Print Service.
    /// Configured specifically for POS-76 80mm thermal printers (42 columns standard).
    /// </summary>
    public class Config
    {
        [JsonPropertyName("PrinterName")]
        public string PrinterName { get; set; } = "POS-76";

        [JsonPropertyName("ListenPort")]
        public int ListenPort { get; set; } = 9111;

        [JsonPropertyName("AutoCut")]
        public bool AutoCut { get; set; } = true;

        [JsonPropertyName("OpenCashDrawer")]
        public bool OpenCashDrawer { get; set; } = false;

        [JsonPropertyName("CharacterEncoding")]
        public int CharacterEncoding { get; set; } = 437;

        [JsonPropertyName("ReceiptWidth")]
        public int ReceiptWidth { get; set; } = 42;

        [JsonPropertyName("LogRequests")]
        public bool LogRequests { get; set; } = true;

        private static string GetConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "config.json");
        }

        public static Config Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                    var config = JsonSerializer.Deserialize<Config>(json, options);
                    if (config != null)
                        return config;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to read config.json ({ex.Message}). Using default POS-76 settings.");
            }

            var defaultConfig = new Config();
            defaultConfig.Save();
            return defaultConfig;
        }

        public bool Save()
        {
            try
            {
                string path = GetConfigPath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to save config.json: {ex.Message}");
                return false;
            }
        }
    }
}
