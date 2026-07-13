---
name: onnx-safety-gate
description: Bir ONNX modelini dağıtım öncesi güvenlik kapısından geçirir — op envanteri (ConvInteger/opset avı), IR sürümü, ve orijinal-vs-dönüştürülmüş çıktı eşitliği. Her model dönüşümünden (simplify/quantize/export) sonra zorunlu.
argument-hint: <model.onnx> [referans_model.onnx]
disable-model-invocation: true
allowed-tools: Bash(.venv-train/bin/python *), Read
---

## Bilinen tuzaklar (ölçülmüş/kaynaklı)
- `ConvInteger`: ORT C# CPU'da YOK (1.20 ve 1.23'te doğrulandı) — model yüklenmez.
- IR > 10: ORT C# yüklemez — `m.ir_version=10` post-patch gerekir (tf2onnx IR8 üretir, sorun yok).
- `onnxoptimizer` varsayılan geçişleri TEHLİKELİ (OODTE arXiv 2505.01892: tespit modellerinde %16.6
  çıktı sapması, YOLO'da −14.31 mAP; başlıca suçlu fuse_bn_into_conv). Kullanma.
- `onnxslim` MHA Gemm bias bozar (ölçüldü — cct export'unda --no-simplify şart).
- opset > 17 kural dışı (hedef ORT uyumluluğu).

## Görev
1. Op/IR taraması:
   `.venv-train/bin/python -c "import onnx; m=onnx.load('$0'); ops={n.op_type for n in m.graph.node}; print('ir',m.ir_version,'opset',[o.version for o in m.opset_import if o.domain=='']); print('ops',sorted(ops)); assert 'ConvInteger' not in ops"`
2. IR>10 ise patch'le ve YENİDEN tara.
3. Referans model verildiyse çıktı eşitliği: aynı rastgele girdiyle iki modeli onnxruntime'da koş,
   max |fark| < 1e-4 doğrula (gerçek kırpıntıyla tekrar önerilir).
4. GEÇTİ/KALDI raporla; KALdıysa hangi kural, hangi op.
