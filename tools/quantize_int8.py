#!/usr/bin/env python3
"""
OtoparkAnpr — ONNX modellerini INT8'e (dinamik kuantizasyon) çevirir.

Amaç: zayıf x86 Windows CPU'da (VNNI varsa) 2-4× hız. Bu OFFLINE bir adımdır; üretilen *_int8.onnx
dosyaları models/'a konur ve appsettings'te DetectorModelPath/OcrModelPath olarak gösterilir.
Kazancı MUTLAKA hedef PC'de Anpr.Bench evaluate ile ölçün (eski donanımda kazanç sınırlı olabilir,
ufak doğruluk kaybı olabilir). Faydalı değilse FP32'de kalın.

Gereksinim:  pip install onnx onnxruntime
Kullanım:    python tools/quantize_int8.py
"""
import os
from pathlib import Path

try:
    from onnxruntime.quantization import quantize_dynamic, QuantType
except ImportError:
    raise SystemExit("Önce: pip install onnx onnxruntime")

MODELS = Path(__file__).resolve().parent.parent / "models"

# Kuantize edilecek modeller (dedektör en çok faydalanır; OCR'ı da deneyebilirsiniz).
TARGETS = [
    "yolo-v9-t-384-license-plates-end2end.onnx",
    "yolo-v9-t-256-license-plates-end2end.onnx",
    "cct_xs_v2_global.onnx",
    "cct_s_v2_global.onnx",
]

for name in TARGETS:
    src = MODELS / name
    if not src.exists():
        print(f"atlandı (yok): {name}")
        continue
    dst = MODELS / name.replace(".onnx", "_int8.onnx")
    print(f"kuantize: {name} -> {dst.name}")
    # QUInt8: ağırlıkları 8-bit'e indirir; dinamik kuantizasyon kalibrasyon verisi gerektirmez.
    quantize_dynamic(str(src), str(dst), weight_type=QuantType.QUInt8)
    print(f"   {src.stat().st_size//1024} KB -> {dst.stat().st_size//1024} KB")

print("\nBitti. Hedef PC'de A/B:")
print("  dotnet run -c Release --project Anpr.Bench -- evaluate dataset/s1 dataset/s1/labels.json \\")
print("      --det yolo-v9-t-384-license-plates-end2end_int8.onnx --ocr cct_xs_v2_global_int8.onnx")
