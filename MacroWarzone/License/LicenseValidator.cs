using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
namespace MacroWarzone.License
{
       public static class LicenseKeyValidator
    {
        public static LicensePayload ValidateKeyOrThrow(string key, string publicKeyPem, Func<string>? getHw = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new Exception("Key mancante.");

            // TRIAL-72H-79879-46546-ABCDE-<PAYLOAD>-<SIG>
            // PRO-2Y-79879-46546-ABCDE-<PAYLOAD>-<SIG>
            var parts = key.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7)
                throw new Exception("Key non valida (formato).");

            var header1 = parts[0].ToUpperInvariant();   // TRIAL / PRO
            var header2 = parts[1].ToUpperInvariant();   // 72H / 2Y (solo “decorazione”)
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

            if (!string.Equals(payload.id, id, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Key non valida (ID mismatch).");

            if (header1 == "TRIAL" && payload.plan != "trial")
                throw new Exception("Key non valida (plan mismatch).");

            if (header1 == "PRO" && payload.plan != "pro")
                throw new Exception("Key non valida (plan mismatch).");

            if (DateTime.UtcNow > payload.exp)
                throw new Exception("Licenza scaduta.");

            if (!string.IsNullOrWhiteSpace(payload.hw) && getHw != null)
            {
                var localHw = getHw();
                if (!string.Equals(localHw, payload.hw, StringComparison.OrdinalIgnoreCase))
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
