using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.Json;

namespace MacroWarzone;

    public static class ConfigLoader
    {
        public static ConfigRoot Load(string path)
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<ConfigRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ConfigRoot();

            return cfg;
        }
    }
