using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MacroWarzone.License
{
    public static class LicenseKey
    {
        // Parsiamo key tipo:
        // TRIAL-72H-79879-46546-ABCDE-<PAYLOAD>-<SIG>
        // PRO-2Y-79879-46546-ABCDE-<PAYLOAD>-<SIG>
        public static LicensePayload ValidateKeyOrThrow(string key, string publicKeyPem, Func<string>? getHw = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new Exception("Key vuota.");

            var parts = key.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7)
                throw new Exception("Key non valida (formato).");

            var header1 = parts[0];            // TRIAL / PRO
            var header2 = parts[1];            // 72H / 2Y
            var id = $"{parts[2]}-{parts[3]}-{parts[4]}";

            var payloadB64 = parts[5];
            var sigB64 = parts[6];

            var payloadBytes = Base64UrlDecode(payloadB64);
            var sigBytes = Base64UrlDecode(sigB64);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            var ok = rsa.VerifyData(payloadBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!ok)
                throw new Exception("Key non valida (firma).");

            var json = Encoding.UTF8.GetString(payloadBytes);
            var payload = JsonSerializer.Deserialize<LicensePayload>(json) ?? throw new Exception("Key non valida (json).");

            // Coerenza “estetica”
            if (!string.Equals(payload.id, id, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Key non valida (ID mismatch).");

            // Coerenza plan con header (non serve per sicurezza, serve per evitare key “mischiate”)
            if (header1.Equals("TRIAL", StringComparison.OrdinalIgnoreCase) && payload.plan != "trial")
                throw new Exception("Key non valida (plan mismatch).");
            if (header1.Equals("PRO", StringComparison.OrdinalIgnoreCase) && payload.plan != "pro")
                throw new Exception("Key non valida (plan mismatch).");

            // Scadenza
            var now = DateTime.UtcNow;
            if (now > payload.exp)
                throw new Exception("Licenza scaduta.");

            // HW binding (opzionale)
            if (!string.IsNullOrWhiteSpace(payload.hw) && getHw != null)
            {
                var local = getHw();
                if (!string.Equals(local, payload.hw, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Licenza non valida per questa macchina.");
            }

            return payload;
        }

        private static byte[] Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
