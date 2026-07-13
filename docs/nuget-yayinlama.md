# NuGet Yayınlama (Trusted Publishing) — Anpr.Sdk

Paketler: **Anpr.Sdk** (gömülebilir çalışma zamanı) + **Anpr.Core** (çekirdek, Sdk'nın bağımlılığı).
Feed: herkese açık **nuget.org**. Yöntem: **Trusted Publishing** (kimlik-anahtarı yok; GitHub OIDC
token'ı karşılığında 1 saatlik geçici anahtar alınır). Workflow: `.github/workflows/publish.yml`.

> ⚠️ **Kalıcılık uyarısı**: nuget.org'a yayınlanan bir sürüm **geri alınamaz** (yalnız "unlist" =
> listeden gizlenir, indirilmesi mümkün kalır). Pakette fine-tune modeliniz `cct_xs_v2_erz.onnx` ve
> tüm işleme hattı gömülüdür; herkese açık feed'de dünyadaki herkes indirebilir. İç kullanım
> yeterliyse ileride özel GitHub Packages'e geçiş tek workflow değişikliğidir.

## Adım 1 — nuget.org'da Trusted Publishing politikası (BİR KEZ)

nuget.org → kullanıcı adı → **Trusted Publishing** → **Create**. Alanları **AYNEN** şöyle doldur
(ekranındaki değerlerde düzeltilmesi gerekenler işaretli):

| Alan | Değer | Not |
|---|---|---|
| Policy Name | `anpr-sdk-publish` | Serbest; tanıman için |
| Package Owner | `omeeragtoprak` | ✓ zaten doğruydu |
| **Repository Owner** | `omeeragtoprak` | ⚠️ ekranında `plate_recognition` yazıyordu — YANLIŞ. Buraya GitHub **kullanıcı adın** gelir |
| **Repository** | `plaka-okuma` | ⚠️ ekranında `plate_recognition` yazıyordu — deponun gerçek adı `plaka-okuma` |
| **Workflow File** | `publish.yml` | ⚠️ boştu — ZORUNLU. Yalnız dosya adı (`.github/workflows/` yazma) |
| Environment | (boş bırak) | Workflow environment kullanmıyor |

Kaydet. Depo **özel** ise politika ilk başarılı yayına kadar 7 gün "geçici aktif" görünür — bu
normaldir (resurrection-attack koruması); ilk push'ta kalıcı olur.

> `user:` (workflow'daki) = nuget.org **profil adın** (e-posta DEĞİL). `omeeragtoprak` değilse:
> GitHub deposu → Settings → Secrets and variables → Actions → **Variables** → `NUGET_USER` ekle.

## Adım 2 — Yeni sürüm yayınlama (her seferinde)

Sürüm etiketten türetilir; `csproj`'daki `<Version>` yalnız yerel yedek. İki yol:

**A) Etiketle (önerilen):**
```bash
git tag v1.0.0
git push origin v1.0.0     # -> workflow tetiklenir, 1.0.0 yayınlanır
```

**B) Elle:** GitHub → Actions → **publish** → Run workflow → sürüm gir (ör. `1.0.1`).

Workflow sırayla: testleri koşar → `Anpr.Core` + `Anpr.Sdk`'yı aynı sürümle paketler → OIDC ile
geçici anahtar alır → nuget.org'a push eder (`--skip-duplicate`). Birkaç dakika sonra paket
indekslenir.

Sürüm artışı kuralı (SemVer): geriye-uyumlu düzeltme `1.0.1`, yeni özellik `1.1.0`, kıran değişiklik
`2.0.0`. **Aynı sürüm iki kez yayınlanamaz** — her yayında numarayı artır.

## Adım 3 — Otopark yazılımında (PMS) tüketme

PMS projesine (net8.0+):
```xml
<ItemGroup>
  <PackageReference Include="Anpr.Sdk" Version="1.0.0" />
</ItemGroup>
```
`dotnet restore` → `Anpr.Core` + ONNX Runtime + SkiaSharp + **modeller** otomatik gelir; derlemede
`models/` çıktı dizinine kopyalanır. Kullanım `docs/entegrasyon-kilavuzu.md` "Seçenek B"de.

**Yeni sürüme geçiş:** `Version="1.1.0"` yap → `dotnet restore`. Native ikililer ve modeller sürümle
birlikte güncellenir; elle DLL/model kopyalama YOK.

## İlk yayın öncesi kontrol listesi
- [ ] `main` push'lu ve yeşil (bu commit'te `.github/workflows/publish.yml` var).
- [ ] nuget.org Trusted Publishing politikası yukarıdaki değerlerle kayıtlı.
- [ ] `NUGET_USER` değişkeni (profil adın `omeeragtoprak` değilse) tanımlı.
- [ ] `git tag v1.0.0 && git push origin v1.0.0`.
- [ ] Actions → publish yeşil; nuget.org/packages/Anpr.Sdk görünür.
