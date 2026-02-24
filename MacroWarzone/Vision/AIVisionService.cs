using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MacroWarzone.Vision;

/// <summary>
/// AI Vision per enemy detection usando YOLO v8 Nano.
/// 
/// MODEL: YOLOv8n (6MB, 60+ FPS su GPU moderna)
/// INPUT: 640x640 RGB
/// OUTPUT: Bounding boxes + confidence
/// 
/// TRAINING: Custom dataset Warzone enemies
/// (Vedi TRAINING_GUIDE.md per procedura training)
/// 
/// SAFE: 
/// - No memory access Warzone
/// - Solo analisi screenshot
/// - No wallhack (rileva solo nemici visibili)
/// </summary>
public sealed class AIVisionService : IDisposable
{
    private InferenceSession? _session;
    private readonly object _lock = new();

    // Model input/output names
    private const string INPUT_NAME = "images";
    private const string OUTPUT_NAME = "output0";

    // Model parameters
    private const int INPUT_WIDTH = 640;
    private const int INPUT_HEIGHT = 640;
    private const float CONFIDENCE_THRESHOLD = 0.5f;
    private const float NMS_THRESHOLD = 0.4f; // Non-Maximum Suppression

    public struct Detection
    {
        public float X;           // Centro X normalized [0,1]
        public float Y;           // Centro Y normalized [0,1]
        public float Width;       // Width normalized [0,1]
        public float Height;      // Height normalized [0,1]
        public float Confidence;  // Confidence [0,1]
        public int ClassId;       // 0 = enemy
    }

    /// <summary>
    /// Inizializza YOLO model.
    /// Richiede file yolov8n_warzone.onnx nella directory.
    /// </summary>
    public bool Initialize(string modelPath = "C:\\Users\\ari63\\Desktop\\Macro-master\\Macro-master\\MacroWarzone\\models\\yolov8n_warzone.onnx", bool useGPU = true)
    {
        lock (_lock)
        {
            try
            {
                var options = new SessionOptions();

                if (useGPU)
                {
                    // Prova GPU (CUDA o DirectML)
                    try
                    {
                        // DirectML (Windows 10+, supporta qualsiasi GPU)
                        options.AppendExecutionProvider_DML(0);
                        System.Diagnostics.Debug.WriteLine("[AI VISION] Using DirectML GPU acceleration");
                    }
                    catch
                    {
                        // Fallback CPU
                        System.Diagnostics.Debug.WriteLine("[AI VISION] GPU not available, using CPU");
                    }
                }

                // Ottimizzazioni
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                options.ExecutionMode = ExecutionMode.ORT_PARALLEL;

                _session = new InferenceSession(modelPath, options);

                System.Diagnostics.Debug.WriteLine($"[AI VISION] Model loaded: {modelPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI VISION] Init failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Rileva nemici in screenshot.
    /// Ritorna lista detections ordinate per distanza dal centro schermo.
    /// </summary>
    public List<Detection> DetectEnemies(Bitmap screenshot)
    {
        lock (_lock)
        {
            if (_session == null)
                return new List<Detection>();

            try
            {
                // 1. Preprocessing: resize + normalize
                var inputTensor = PreprocessImage(screenshot);

                // 2. Inference
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(INPUT_NAME, inputTensor)
                };

                using var results = _session.Run(inputs);
                var output = results.First(r => r.Name == OUTPUT_NAME).AsTensor<float>();

                // 3. Postprocessing: parse detections
                var detections = ParseDetections(output);

                // 4. Non-Maximum Suppression (rimuovi overlap)
                detections = ApplyNMS(detections);

                // 5. Ordina per distanza dal centro (più vicino = priorità)
                detections = detections.OrderBy(d =>
                {
                    float dx = d.X - 0.5f;
                    float dy = d.Y - 0.5f;
                    return dx * dx + dy * dy; // Distanza quadrata dal centro
                }).ToList();

                return detections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI VISION] Detection failed: {ex.Message}");
                return new List<Detection>();
            }
        }
    }

    /// <summary>
    /// Trova target più vicino al centro schermo.
    /// Ritorna null se nessun enemy rilevato.
    /// </summary>
    public Detection? GetClosestTargetToCenter(Bitmap screenshot)
    {
        var detections = DetectEnemies(screenshot);
        return detections.Count > 0 ? detections[0] : null;
    }

    /// <summary>
    /// Preprocessing: Bitmap → Tensor normalizzato.
    /// </summary>
    private DenseTensor<float> PreprocessImage(Bitmap image)
    {
        // Resize a 640x640
        using var resized = new Bitmap(image, new Size(INPUT_WIDTH, INPUT_HEIGHT));

        // Crea tensor [1, 3, 640, 640] (batch, channels, height, width)
        var tensor = new DenseTensor<float>(new[] { 1, 3, INPUT_HEIGHT, INPUT_WIDTH });

        // Converti pixel a tensor normalizzato [0,1]
        for (int y = 0; y < INPUT_HEIGHT; y++)
        {
            for (int x = 0; x < INPUT_WIDTH; x++)
            {
                var pixel = resized.GetPixel(x, y);

                // RGB channels normalizzati [0,1]
                tensor[0, 0, y, x] = pixel.R / 255f; // R
                tensor[0, 1, y, x] = pixel.G / 255f; // G
                tensor[0, 2, y, x] = pixel.B / 255f; // B
            }
        }

        return tensor;
    }

    /// <summary>
    /// Parse output YOLO → lista Detection.
    /// </summary>
    private List<Detection> ParseDetections(Tensor<float> output)
    {
        var detections = new List<Detection>();

        // YOLO v8 output shape: [1, 84, 8400]
        // 84 = 4 bbox coords + 80 classes
        // 8400 = numero predizioni (grid cells)

        int numClasses = 80;
        int numPredictions = output.Dimensions[2];

        for (int i = 0; i < numPredictions; i++)
        {
            // Bbox coords (center x, center y, width, height)
            float cx = output[0, 0, i];
            float cy = output[0, 1, i];
            float w = output[0, 2, i];
            float h = output[0, 3, i];

            // Trova classe con max confidence
            float maxConf = 0;
            int maxClass = 0;

            for (int c = 0; c < numClasses; c++)
            {
                float conf = output[0, 4 + c, i];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    maxClass = c;
                }
            }

            // Filtra per confidence threshold
            if (maxConf < CONFIDENCE_THRESHOLD)
                continue;

            // Normalizza coordinate [0,1]
            cx /= INPUT_WIDTH;
            cy /= INPUT_HEIGHT;
            w /= INPUT_WIDTH;
            h /= INPUT_HEIGHT;

            detections.Add(new Detection
            {
                X = cx,
                Y = cy,
                Width = w,
                Height = h,
                Confidence = maxConf,
                ClassId = maxClass
            });
        }

        return detections;
    }

    /// <summary>
    /// Non-Maximum Suppression: rimuovi bounding box overlapping.
    /// </summary>
    private List<Detection> ApplyNMS(List<Detection> detections)
    {
        if (detections.Count == 0)
            return detections;

        // Ordina per confidence (più alto prima)
        detections = detections.OrderByDescending(d => d.Confidence).ToList();

        var keep = new List<Detection>();

        while (detections.Count > 0)
        {
            // Prendi detection con max confidence
            var best = detections[0];
            keep.Add(best);
            detections.RemoveAt(0);

            // Rimuovi overlap
            detections = detections.Where(d =>
            {
                float iou = CalculateIoU(best, d);
                return iou < NMS_THRESHOLD;
            }).ToList();
        }

        return keep;
    }

    /// <summary>
    /// Calcola Intersection over Union tra due bbox.
    /// </summary>
    private float CalculateIoU(Detection a, Detection b)
    {
        // Converti center-width-height → min-max
        float a_xmin = a.X - a.Width / 2;
        float a_ymin = a.Y - a.Height / 2;
        float a_xmax = a.X + a.Width / 2;
        float a_ymax = a.Y + a.Height / 2;

        float b_xmin = b.X - b.Width / 2;
        float b_ymin = b.Y - b.Height / 2;
        float b_xmax = b.X + b.Width / 2;
        float b_ymax = b.Y + b.Height / 2;

        // Calcola intersezione
        float inter_xmin = Math.Max(a_xmin, b_xmin);
        float inter_ymin = Math.Max(a_ymin, b_ymin);
        float inter_xmax = Math.Min(a_xmax, b_xmax);
        float inter_ymax = Math.Min(a_ymax, b_ymax);

        float inter_width = Math.Max(0, inter_xmax - inter_xmin);
        float inter_height = Math.Max(0, inter_ymax - inter_ymin);
        float inter_area = inter_width * inter_height;

        // Calcola union
        float a_area = a.Width * a.Height;
        float b_area = b.Width * b.Height;
        float union_area = a_area + b_area - inter_area;

        return union_area > 0 ? inter_area / union_area : 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
