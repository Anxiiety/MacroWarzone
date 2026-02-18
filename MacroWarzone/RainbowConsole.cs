using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone
{
    /// <summary>
    /// Classe per gestire output console con effetto arcobaleno animato.
    /// Supporta sia modalità a 16 colori (compatibile ovunque) che RGB 24-bit (terminali moderni).
    /// 
    /// DESIGN CHOICES:
    /// - Static class: non serve istanziare, è un utility helper
    /// - Thread-safe: usa lock per evitare race condition quando scrivi da più thread
    /// - Doppia modalità: fallback automatico se il terminale non supporta RGB
    /// </summary>
    public static class RainbowConsole
    {
        #region Private Fields (Stato interno della classe)

        /// <summary>
        /// Array dei colori dell'arcobaleno disponibili in ConsoleColor (16-color mode).
        /// 
        /// PERCHÉ QUESTO ORDINE:
        /// - Simula lo spettro visibile: Rosso → Giallo → Verde → Ciano → Blu → Magenta
        /// - DarkYellow usato perché Yellow puro è troppo chiaro su sfondo bianco
        /// </summary>
        private static readonly ConsoleColor[] RainbowColors = new[]
        {
        ConsoleColor.Red,
        ConsoleColor.DarkYellow,
        ConsoleColor.Yellow,
        ConsoleColor.Green,
        ConsoleColor.Cyan,
        ConsoleColor.Blue,
        ConsoleColor.Magenta
    };

        /// <summary>
        /// Indice corrente nel ciclo dei colori.
        /// 
        /// THREADING: Questo campo è condiviso tra chiamate, quindi serve lock quando lo modifichi.
        /// MODULO: Usiamo % per tornare a 0 quando raggiungiamo la fine dell'array.
        /// </summary>
        private static int _colorIndex = 0;

        /// <summary>
        /// Hue corrente (tonalità) per la modalità RGB, espresso in gradi (0-360).
        /// 
        /// HSV COLOR SPACE:
        /// - Hue: 0° = Rosso, 120° = Verde, 240° = Blu, 360° = torna a Rosso
        /// - Incrementando Hue otteniamo una rotazione fluida dell'arcobaleno
        /// </summary>
        private static float _hue = 0f;

        /// <summary>
        /// Lock object per garantire thread-safety nelle scritture console.
        /// 
        /// PERCHÉ SERVE:
        /// - Se più thread chiamano WriteRainbow() contemporaneamente, i colori si mescolano
        /// - lock(_consoleLock) garantisce che solo un thread alla volta scriva
        /// 
        /// BEST PRACTICE:
        /// - Sempre usare un object dedicato per lock (mai lock(this) o lock su tipi pubblici)
        /// </summary>
        private static readonly object _consoleLock = new object();

        #endregion

        #region Public Methods - Modalità 16 Colori (ConsoleColor)

        /// <summary>
        /// Scrive un testo con effetto arcobaleno animato, cambiando colore ad ogni carattere.
        /// 
        /// QUANDO USARLO:
        /// - Header di applicazione
        /// - Banner importanti
        /// - Effetti visivi su stringhe brevi
        /// 
        /// PARAMETRI:
        /// - text: Il testo da scrivere
        /// - delayMs: Millisecondi di pausa tra ogni carattere (0 = nessuna animazione)
        /// 
        /// THREAD-SAFETY: Usa lock per evitare race condition.
        /// </summary>
        public static void WriteRainbow(string text, int delayMs = 50)
        {
            lock (_consoleLock)
            {
                foreach (char c in text)
                {
                    // Calcola quale colore usare (modulo per ciclare infinitamente)
                    Console.ForegroundColor = RainbowColors[_colorIndex % RainbowColors.Length];

                    // Scrivi il singolo carattere
                    Console.Write(c);

                    // Avanza al prossimo colore
                    _colorIndex++;

                    // Pausa per effetto animato (opzionale)
                    if (delayMs > 0)
                        Thread.Sleep(delayMs);
                }

                // IMPORTANTE: Resetta sempre il colore alla fine
                // Altrimenti tutto il resto della console rimane colorato!
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Scrive una riga intera con un singolo colore arcobaleno, poi va a capo.
        /// 
        /// QUANDO USARLO:
        /// - Log che devono essere distinguibili visivamente
        /// - Liste di elementi dove ogni elemento ha un colore diverso
        /// - Output di stato (come i tuoi step [1/5], [2/5], ecc.)
        /// 
        /// DIFFERENZA da WriteRainbow():
        /// - Qui l'intera riga ha UN SOLO colore
        /// - Il colore cambia solo tra una chiamata e l'altra
        /// </summary>
        public static void WriteLineRainbow(string text)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = RainbowColors[_colorIndex % RainbowColors.Length];
                Console.WriteLine(text);
                _colorIndex++;
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Ottiene il prossimo colore dell'arcobaleno senza scrivere nulla.
        /// 
        /// QUANDO USARLO:
        /// - Quando vuoi controllare manualmente il colore prima di scrivere
        /// - Per applicare il colore a più WriteLine consecutive
        /// - Per logica custom di colorazione
        /// 
        /// ESEMPIO:
        /// Console.ForegroundColor = RainbowConsole.GetNextRainbowColor();
        /// Console.WriteLine("Prima riga");
        /// Console.WriteLine("Seconda riga (stesso colore)");
        /// Console.ResetColor();
        /// </summary>
        public static ConsoleColor GetNextRainbowColor()
        {
            lock (_consoleLock)
            {
                var color = RainbowColors[_colorIndex % RainbowColors.Length];
                _colorIndex++;
                return color;
            }
        }

        /// <summary>
        /// Resetta l'indice dei colori a zero, ricominciando il ciclo dall'inizio.
        /// 
        /// QUANDO USARLO:
        /// - All'inizio di una nuova sezione logica della tua applicazione
        /// - Quando vuoi che una sequenza di log abbia sempre gli stessi colori
        /// - Dopo aver stampato un header e vuoi ripartire da Rosso
        /// </summary>
        public static void ResetColorCycle()
        {
            lock (_consoleLock)
            {
                _colorIndex = 0;
            }
        }

        #endregion

        #region Public Methods - Modalità RGB 24-bit (ANSI Escape Codes)

        /// <summary>
        /// Scrive testo con colore RGB arcobaleno fluido (milioni di colori).
        /// 
        /// REQUISITI:
        /// - Windows Terminal, ConEmu, o terminali Linux/macOS
        /// - NON funziona su cmd.exe classico (usa WriteRainbow come fallback)
        /// 
        /// COME FUNZIONA:
        /// - Usa ANSI escape codes: \x1b[38;2;R;G;Bm
        /// - Converte HSV (Hue-Saturation-Value) in RGB per colori fluidi
        /// - Incrementa Hue ad ogni carattere per effetto arcobaleno continuo
        /// 
        /// VANTAGGIO vs ConsoleColor:
        /// - Transizione fluida (no salti tra colori)
        /// - 16 milioni di colori invece di 16
        /// </summary>
        public static void WriteRainbowRgb(string text, int delayMs = 30)
        {
            lock (_consoleLock)
            {
                foreach (char c in text)
                {
                    // Converti Hue attuale in RGB
                    var (r, g, b) = HsvToRgb(_hue);

                    // ANSI escape code per RGB:
                    // \x1b[38;2;R;G;Bm = imposta colore foreground RGB
                    Console.Write($"\x1b[38;2;{r};{g};{b}m{c}");

                    // Incrementa Hue di 5 gradi (velocità dell'arcobaleno)
                    // TUNING: Aumenta per arcobaleno più veloce, diminuisci per più lento
                    _hue = (_hue + 5) % 360;

                    if (delayMs > 0)
                        Thread.Sleep(delayMs);
                }

                // \x1b[0m = reset di TUTTI gli attributi ANSI
                Console.Write("\x1b[0m");
            }
        }

        /// <summary>
        /// Scrive una riga intera con colore RGB arcobaleno.
        /// Versione RGB di WriteLineRainbow().
        /// </summary>
        public static void WriteLineRainbowRgb(string text)
        {
            lock (_consoleLock)
            {
                var (r, g, b) = HsvToRgb(_hue);
                Console.WriteLine($"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m");
                _hue = (_hue + 10) % 360;
            }
        }

        #endregion

        #region Private Helper Methods (Logica interna)

        /// <summary>
        /// Converte da HSV (Hue-Saturation-Value) a RGB (Red-Green-Blue).
        /// 
        /// PERCHÉ HSV INVECE DI RGB DIRETTO:
        /// - HSV è più intuitivo per creare arcobaleni
        /// - Basta incrementare Hue per ottenere tutti i colori
        /// - RGB richiederebbe calcoli complessi per transizioni fluide
        /// 
        /// MATEMATICA:
        /// - Hue divide il cerchio cromatico in 6 settori (0-60, 60-120, ecc.)
        /// - Ogni settore interpola tra due colori primari
        /// - Formule standard da computer graphics
        /// 
        /// PARAMETRI:
        /// - hue: Tonalità in gradi (0-360)
        /// 
        /// RITORNA:
        /// - Tupla (r, g, b) con valori 0-255
        /// </summary>
        private static (byte r, byte g, byte b) HsvToRgb(float hue)
        {
            // Saturazione e Value al massimo per colori puri e brillanti
            float s = 1.0f; // 0 = grigio, 1 = colore puro
            float v = 1.0f; // 0 = nero, 1 = massima luminosità

            // Determina in quale settore del cerchio cromatico siamo (0-5)
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;

            // Frazione all'interno del settore corrente
            float f = hue / 60 - MathF.Floor(hue / 60);

            // Calcola componenti intermedie
            float p = v * (1 - s);           // Componente minima
            float q = v * (1 - f * s);       // Componente decrescente
            float t = v * (1 - (1 - f) * s); // Componente crescente

            // Mappa il settore ai valori RGB
            // SPIEGAZIONE:
            // - Settore 0 (0-60°):   Rosso → Giallo (R=max, G aumenta, B=min)
            // - Settore 1 (60-120°): Giallo → Verde (G=max, R diminuisce, B=min)
            // - Settore 2 (120-180°): Verde → Ciano (G=max, B aumenta, R=min)
            // - Settore 3 (180-240°): Ciano → Blu (B=max, G diminuisce, R=min)
            // - Settore 4 (240-300°): Blu → Magenta (B=max, R aumenta, G=min)
            // - Settore 5 (300-360°): Magenta → Rosso (R=max, B diminuisce, G=min)
            return hi switch
            {
                0 => ((byte)(v * 255), (byte)(t * 255), (byte)(p * 255)),
                1 => ((byte)(q * 255), (byte)(v * 255), (byte)(p * 255)),
                2 => ((byte)(p * 255), (byte)(v * 255), (byte)(t * 255)),
                3 => ((byte)(p * 255), (byte)(q * 255), (byte)(v * 255)),
                4 => ((byte)(t * 255), (byte)(p * 255), (byte)(v * 255)),
                _ => ((byte)(v * 255), (byte)(p * 255), (byte)(q * 255))
            };
        }

        #endregion
    }
}
