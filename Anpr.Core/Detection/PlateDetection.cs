namespace Anpr.Core.Detection;

/// <summary>Dedektörün ürettiği tek bir plaka kutusu (orijinal görüntü piksel uzayında).</summary>
public sealed record PlateDetection(float Confidence, int X, int Y, int Width, int Height, int ClassId);
