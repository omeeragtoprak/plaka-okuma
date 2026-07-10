"""Statik QDQ INT8 kuantalama reçetesi (2026-07-11'de doğrulandı).

ÖLÇÜLMÜŞ SONUÇLAR (bkz. gap-analizi 5l):
- OCR (cct_xs_v2_erz) Conv-only QDQ: dilim doğruluğu eşdeğer (485→488/625), diag4 birebir korunur
  (gürültü 9→11), C# ORT yükler+okur, ARM'da %17 hız (Intel/VNNI'de daha fazlası beklenir). Boyut 2.9→2.1MB.
- DEDEKTÖR QDQ: diag4 birebir 22→21 → ELENDİ (dedektör kuantına dikkat).
- TAM dinamik kuant: ConvInteger ORT C# CPU'da YOK (1.20 ve 1.23'te doğrulandı) → kullanılamaz.
- Per-channel MatMul kuantı: QLinearMatMul CPU çekirdeği skaler zero-point ister → MatMul'ları
  KUANTALAMA (op_types_to_quantize=['Conv']); transformer dikkati zaten kuanta duyarlı.
- Güvenli tip seti: aktivasyon QUInt8 + ağırlık QInt8 + reduce_range=True (AVX2 saturasyon koruması).

Kullanım: .venv-train/bin/python scripts/quantize_qdq.py <model.onnx> <çıktı.onnx> <kalibrasyon>
  kalibrasyon: OCR için crops CSV'si (image_path,plate_text), dedektör için kare klasörü.
Sonrasında MUTLAKA: /onnx-safety-gate + /anpr-regression (dilim-bazlı sıfır-regresyon şartı).
"""
import csv, glob, os, sys
import numpy as np
from PIL import Image
import onnxruntime as ort
from onnxruntime.quantization import quantize_static, CalibrationDataReader, QuantFormat, QuantType


class OcrCropReader(CalibrationDataReader):
    def __init__(self, csv_path, input_name, n=200):
        base = os.path.dirname(os.path.abspath(csv_path))
        self.paths = []
        for r in csv.DictReader(open(csv_path)):
            if (r["plate_text"] or "").strip():
                p = r["image_path"]
                self.paths.append(p if os.path.isabs(p) else os.path.join(base, p))
            if len(self.paths) >= n:
                break
        self.i, self.name = 0, input_name
    def get_next(self):
        if self.i >= len(self.paths): return None
        img = Image.open(self.paths[self.i]).convert("RGB").resize((128, 64))
        self.i += 1
        return {self.name: np.asarray(img, dtype=np.uint8)[None]}


class DetectorFrameReader(CalibrationDataReader):
    def __init__(self, frames_dir, input_name, n=100, size=384):
        fs = sorted(glob.glob(os.path.join(frames_dir, "**/*.jpg"), recursive=True))
        self.paths = fs[::max(1, len(fs)//n)][:n]
        self.i, self.name, self.size = 0, input_name, size
    def get_next(self):
        if self.i >= len(self.paths): return None
        img = Image.open(self.paths[self.i]).convert("RGB"); self.i += 1
        w, h = img.size; sc = min(self.size/w, self.size/h)
        canvas = Image.new("RGB", (self.size, self.size), (114, 114, 114))
        canvas.paste(img.resize((int(w*sc), int(h*sc))), ((self.size-int(w*sc))//2, (self.size-int(h*sc))//2))
        return {self.name: np.asarray(canvas, dtype=np.float32).transpose(2, 0, 1)[None] / 255.0}


def main():
    src, dst, calib = sys.argv[1], sys.argv[2], sys.argv[3]
    inp = ort.InferenceSession(src, providers=["CPUExecutionProvider"]).get_inputs()[0]
    reader = (OcrCropReader(calib, inp.name) if calib.endswith(".csv")
              else DetectorFrameReader(calib, inp.name, size=inp.shape[-1]))
    quantize_static(src, dst, reader,
                    quant_format=QuantFormat.QDQ,
                    op_types_to_quantize=["Conv"],
                    activation_type=QuantType.QUInt8,
                    weight_type=QuantType.QInt8,
                    per_channel=True, reduce_range=True)
    import onnx
    m = onnx.load(dst)
    ops = {n.op_type for n in m.graph.node}
    assert "ConvInteger" not in ops
    print("OK:", dst, os.path.getsize(dst)//1024, "KB; sonra /onnx-safety-gate + batarya koş!")


if __name__ == "__main__":
    main()
