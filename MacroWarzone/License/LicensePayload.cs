using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone.License
{
    public sealed class LicensePayload
    {
        public string sub { get; set; } = "";        // nome/cliente
        public string plan { get; set; } = "";       // "trial" | "pro"
        public DateTime iat { get; set; }            // issued-at UTC
        public DateTime exp { get; set; }            // expiry UTC
        public string id { get; set; } = "";         // id cosmetico (es: 79879-46546-ABCDE)
        public string? hw { get; set; }              // opzionale
    }
}
