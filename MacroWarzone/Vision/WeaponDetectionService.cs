using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using Tesseract;
using Tesseract.Interop;
using System.IO;

namespace MacroWarzone.Vision;

/// <summary>
/// Weapon detection via OCR su zona HUD arma.
/// 
/// ZONA WARZONE (1920x1080):
/// - Arma primaria: bottom-right (1650, 950, 250, 80)
/// - Munizioni: sotto arma
/// 
/// SAFE: Solo lettura screenshot, no memory access.
/// </summary>
public sealed class WeaponDetectionService : IDisposable
{
    private TesseractEngine? _ocrEngine;
    private readonly object _lock = new();

    // Cache ultimo weapon rilevato
    private string _lastDetectedWeapon = "Unknown";
    private DateTime _lastDetectionTime = DateTime.MinValue;
    private const double CACHE_DURATION_SEC = 2.0; // Cache valida per 2 sec

    // Regione HUD arma Warzone (1920x1080)
    private const int WEAPON_HUD_X = 1650;
    private const int WEAPON_HUD_Y = 950;
    private const int WEAPON_HUD_WIDTH = 250;
    private const int WEAPON_HUD_HEIGHT = 80;

    /// <summary>
    /// Inizializza Tesseract OCR.
    /// Richiede tessdata folder nella directory progetto.
    /// </summary>
    public bool Initialize(string tessdataPath = "./tessdata")
    {
        lock (_lock)
        {
            try
            {
                // Crea engine con lingua inglese
                _ocrEngine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);

                // Configura per testo HUD gaming
                _ocrEngine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-");
                _ocrEngine.SetVariable("load_system_dawg", "false");
                _ocrEngine.SetVariable("load_freq_dawg", "false");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WEAPON DETECTION] Init failed: {ex.Message}");
                return false;
            }
        }
    }

    public string DetectWeapon(Bitmap screenshot)
    {
        lock (_lock)
        {
            // Check cache
            if ((DateTime.Now - _lastDetectionTime).TotalSeconds < CACHE_DURATION_SEC)
            {
                return _lastDetectedWeapon;
            }

            if (_ocrEngine == null)
            {
                return "Unknown";
            }

            try
            {
                // Crop zona HUD arma
                using var weaponRegion = CropWeaponHUD(screenshot);
                if (weaponRegion == null)
                    return _lastDetectedWeapon;

                // Preprocessing
                Pix processed = PreprocessImage(weaponRegion);

                // Se preprocessing fallisce, usa immagine originale
                if (processed == null)
                {
                    // Fallback: usa bitmap diretto (meno accurato ma funziona)
                    string tempPath = System.IO.Path.GetTempFileName() + ".png";
                    weaponRegion.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                    processed = Pix.LoadFromFile(tempPath);
                    try { System.IO.File.Delete(tempPath); } catch { }
                }

                if (processed == null)
                    return _lastDetectedWeapon;

                // OCR
                using (processed)
                using (var page = _ocrEngine.Process(processed))
                {
                    string rawText = page.GetText().Trim();
                    string weaponName = ParseWeaponName(rawText);

                    // Update cache
                    if (!string.IsNullOrEmpty(weaponName) && weaponName != "Unknown")
                    {
                        _lastDetectedWeapon = weaponName;
                        _lastDetectionTime = DateTime.Now;
                    }

                    return _lastDetectedWeapon;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WEAPON DETECTION] Detection failed: {ex.Message}");
                return _lastDetectedWeapon;
            }
        }
    }

    /// <summary>
    /// Crop regione HUD arma da full screenshot.
    /// </summary>
    private Bitmap? CropWeaponHUD(Bitmap screenshot)
    {
        try
        {
            // Scala coordinate se risoluzione diversa da 1920x1080
            double scaleX = screenshot.Width / 1920.0;
            double scaleY = screenshot.Height / 1080.0;

            int x = (int)(WEAPON_HUD_X * scaleX);
            int y = (int)(WEAPON_HUD_Y * scaleY);
            int w = (int)(WEAPON_HUD_WIDTH * scaleX);
            int h = (int)(WEAPON_HUD_HEIGHT * scaleY);

            // Clamp to bounds
            x = Math.Max(0, Math.Min(x, screenshot.Width - w));
            y = Math.Max(0, Math.Min(y, screenshot.Height - h));
            w = Math.Min(w, screenshot.Width - x);
            h = Math.Min(h, screenshot.Height - y);

            var rect = new System.Drawing.Rectangle(x, y, w, h);
            return screenshot.Clone(rect, screenshot.PixelFormat);
        }
        catch
        {
            return null;
        }
    }


    /// </summary>
    /// <summary>
    /// Preprocessing semplificato senza metodi deprecati.
    /// </summary>
    private Pix PreprocessImage(Bitmap bitmap)
    {
        try
        {
            // Converti Bitmap → byte array
            using var ms = new System.IO.MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] imageBytes = ms.ToArray();

            // Carica come Pix da byte array
            var pix = Pix.LoadFromMemory(imageBytes);

            if (pix == null)
                return null;

            // Solo grayscale (minimo necessario)
            if (pix.Depth > 8)
            {
                var grayPix = pix.ConvertRGBToGray();
                pix.Dispose();
                return grayPix;
            }

            return pix;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WEAPON DETECTION] Preprocess error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse weapon name da testo OCR grezzo.
    /// </summary>
    private string ParseWeaponName(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "Unknown";

        // Rimuovi whitespace extra
        rawText = Regex.Replace(rawText, @"\s+", " ").Trim();

        // Dizionario armi comuni Warzone (case-insensitive match)
        var weaponPatterns = new[]
        {
            // Assault Rifles
            @"M4A?1", @"AK-?47", @"GRAU\s*5\.?56", @"RAM-?7", @"KILO\s*141",
            @"M13", @"FN\s*SCAR", @"ODEN", @"FAL", @"CR-?56\s*AMAX",
            
            // SMGs
            @"MP5", @"MP7", @"AUG", @"P90", @"BIZON", @"UZI", @"STRIKER\s*45",
            
            // LMGs
            @"PKM", @"SA87", @"M91", @"MG34", @"HOLGER-?26", @"BRUEN\s*MK9",
            
            // Snipers
            @"HDR", @"AX-?50", @"DRAGUNOV", @"RYTEC\s*AMR", @"SPR\s*208",
            
            // Marksman
            @"EBR-?14", @"MK2\s*CARBINE", @"CROSSBOW", @"SKS", @"KAR98K"
        };

        foreach (var pattern in weaponPatterns)
        {
            var match = Regex.Match(rawText, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Normalizza nome
                return NormalizeWeaponName(match.Value);
            }
        }

        // Fallback: ritorna raw text se non match
        return string.IsNullOrEmpty(rawText) ? "Unknown" : rawText;
    }

    /// <summary>
    /// Normalizza nome arma (es: "M4A 1" → "M4A1").
    /// </summary>
    private string NormalizeWeaponName(string name)
    {
        name = name.ToUpperInvariant();
        name = name.Replace(" ", "");
        name = name.Replace(".", "");

        return name switch
        {
            "M4A1" or "M4" => "M4A1",
            "AK47" or "AK-47" => "AK-47",
            "GRAU556" or "GRAU" => "GRAU",
            "RAM7" or "RAM-7" => "RAM-7",
            "KILO141" or "KILO" => "KILO",
            _ => name
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _ocrEngine?.Dispose();
            _ocrEngine = null;
        }
    }
}
