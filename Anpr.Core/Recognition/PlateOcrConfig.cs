using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Anpr.Core.Recognition;

public enum OcrColorMode { Grayscale, Rgb }

/// <summary>
/// fast-plate-ocr modelinin yaml config'i (AlprNet'in kullandığı şema ile aynı).
/// Alfabe, slot sayısı, giriş boyutu vb. bilgileri taşır.
/// </summary>
public sealed record PlateOcrConfig
{
    public int MaxPlateSlots { get; init; }
    public string Alphabet { get; init; } = string.Empty;
    public string PadChar { get; init; } = string.Empty;
    public int ImgHeight { get; init; }
    public int ImgWidth { get; init; }
    public bool KeepAspectRatio { get; init; }
    public OcrColorMode ImageColorMode { get; init; } = OcrColorMode.Rgb;

    public int NumChannels => ImageColorMode == OcrColorMode.Rgb ? 3 : 1;
    public int VocabularySize => Alphabet.Length;

    public static PlateOcrConfig FromYaml(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"OCR config bulunamadı: {full}");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(full))
                  ?? throw new InvalidDataException($"OCR config okunamadı: {full}");

        var cfg = new PlateOcrConfig
        {
            MaxPlateSlots = GetInt(raw, "max_plate_slots"),
            Alphabet = GetString(raw, "alphabet"),
            PadChar = GetString(raw, "pad_char"),
            ImgHeight = GetInt(raw, "img_height"),
            ImgWidth = GetInt(raw, "img_width"),
            KeepAspectRatio = GetBool(raw, "keep_aspect_ratio"),
            ImageColorMode = string.Equals(GetString(raw, "image_color_mode"), "grayscale", StringComparison.OrdinalIgnoreCase)
                ? OcrColorMode.Grayscale
                : OcrColorMode.Rgb,
        };

        if (string.IsNullOrEmpty(cfg.Alphabet)) throw new InvalidDataException("Alphabet boş olamaz.");
        if (string.IsNullOrEmpty(cfg.PadChar) || !cfg.Alphabet.Contains(cfg.PadChar))
            throw new InvalidDataException("PadChar alfabede yer almalı.");
        if (cfg.MaxPlateSlots <= 0 || cfg.ImgWidth <= 0 || cfg.ImgHeight <= 0)
            throw new InvalidDataException("max_plate_slots / img_width / img_height geçersiz.");

        return cfg;
    }

    private static string GetString(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && v != null ? v.ToString()! : string.Empty;

    private static int GetInt(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && v != null && int.TryParse(v.ToString(), out var i) ? i : 0;

    private static bool GetBool(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && v != null && bool.TryParse(v.ToString(), out var b) && b;

    public override string ToString() =>
        $"PlateOcrConfig(Slots={MaxPlateSlots}, Vocab={VocabularySize}, {ImgWidth}x{ImgHeight}, {ImageColorMode}, keepAspect={KeepAspectRatio})";
}
