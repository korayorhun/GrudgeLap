# GrudgeLap — devir notu (v0.4 gerçekçilik turu)

Tarih: 2026-10-22 · Önceki makine oturumu: `aeedde33` (Claude Code, VS Code) · Plan modu: kapalı · Önceki devir dosyası: yok.
Bu dosya GEÇİCİDİR: başka bilgisayarda kaldığı yerden devam etmek için repoyla taşınır; v0.4.0 yayınlanınca silinir.

> **Yeni thread için prompt (kopyala-yapıştır):**
> Kişisel GitHub projem GrudgeLap üzerinde çalışıyoruz (1989 Hard Drivin' saygı duruşu; lisans temiz, olabildiğince fotogerçekçi, gerçek sesli). Repo `C:\_app\_tools\GrudgeLap`, dal `wip/v0.4-realism`. Şunu oku ve içindeki okuma listesini sırayla yükle, kaldığımız yerden sürdür: `C:\_app\_tools\GrudgeLap\docs\handoff\DEVAM-BURADAN.md`. İlk iş: kokpit görünümünde sol A-direği çok kalın ve baskın (docs/handoff/cockpit-v06.jpg); düzeltip görünmez pencereyle ekran görüntüsü al, sonra dokümanlar ve v0.4.0 yayını.

## 1. Okuma listesi (sırayla)

1. Bu dosya.
2. `README.md` — ürün ne, kurulum, tuşlar, derleme ve yayın komutları.
3. `docs/design.md` — mimari (`physics/` motor-bağımsız simülasyon, `game/` Godot görünümü), konvansiyonlar (Z ekseni ters), komut satırı bayrakları, yol haritası.
4. `docs/feel-checklist.md` — "kabin gibi sürüyor" demenin sayısal karşılığı; her satır bir test.
5. `THIRD_PARTY.md`, `game/assets/audio/SOURCES.md`, `game/assets/textures/SOURCES.md` — lisans disiplini: yalnız CC0 varlık, her dosyanın kaynağı yazılı.
6. `docs/handoff/01-arastirma.md`, `docs/handoff/02-plan-taslagi.md` — orijinal oyun araştırması ve ilk plan (Türkçe); orijinale sadakat sorularında.
7. Kod giriş noktaları: `game/src/Bootstrap.cs` (akış, bayraklar, `HideWindow`), `game/src/CarView.cs` (araç lofti + `BuildInterior`), `game/src/CameraRig.cs` (kokpit kamerası), `game/src/WorldBuilder.cs` / `PropBuilder.cs` / `FoliageBuilder.cs` / `TrainView.cs` (dünya), `game/src/EngineAudio.cs` / `SampledEngine.cs` / `TyreAudio.cs` (ses), `physics/Track/OriginalInspiredTrack.cs` (pist + dekor + tren), `physics/Race/RaceSession.cs` (kurallar, olaylar).
8. `tools/build.ps1`, `tools/release.ps1`, `.github/workflows/build.yml` — derleme/yayın hattı (şablon indirme adımı CI dosyasında).

## 2. İş ne

Koray'ın kişisel, açık kaynak projesi: https://github.com/korayorhun/GrudgeLap . Godot 4.7.2 .NET (C#) görünüm + motor-bağımsız C# fizik kütüphanesi (`physics/`, net8.0, xunit testleri). Hedef: 1989 arcade sürüş simülatörünün hissini (fizik, kurallar, pist düzeni, dekor) marka/varlık kopyalamadan, mümkün olduğunca gerçek dünya gibi görünen ve gerçek kayıtlarla seslendirilen, Windows'ta klavyeyle oynanan, kendini güncelleyen bir oyunda yeniden yaratmak. Dağıtım deseni kullanıcının SesliOkuma projesindeki gibi: GitHub Releases + Inno Setup kurulum + taşınabilir zip + Scoop bucket (`korayorhun/scoop-bucket`) + winget (`KorayOrhun.GrudgeLap`) + oyun içi F10 güncelleyici (SHA-256 doğrulamalı).

## 3. Kullanıcının istekleri (kronolojik, kısa)

1. Araştırma: Hard Drivin' hakkında her şey (`01-arastirma.md`).
2. "Lisans ve dava konusu olmayacak şekilde, orijinaline his olarak en çok benzeyen, en fotorealistik, klavyeyle oynanan Windows oyunu yap."
3. Repoyu `C:\_app\_tools\GrudgeLap` altına taşı (kişisel GitHub projeleri hep `C:\_app\_tools\<Proje>`).
4. "Araba havada uçuyor, Testarossa'ya benzemiyor, pist orijinale benzemiyor" → düzeltildi (v0.2).
5. "Yarım kalanı tamamla, korayorhun GitHub'a public yayınla, SesliOkuma gibi yaygınlaştır (kurulum, Scoop, winget), kendini güncelleyen uygulama olsun, sıradaki tüm maddeleri yapıp sonra yayınla." → v0.2.0.
6. "Ses komik; gerçek Testarossa ve Novitec egzozlu 458 sesi olsun; iki araba seçeneği olsun." → iki araba, CC0 gerçek kayıtlar (flat-12 için temiz kayıt yok, Countach CC0 kaydı belgelenerek kullanıldı).
7. "Bu bilgisayara kurdun mu, oynanıyor mu? Görüntü fotorealistik olmaktan uzak, pist gerçek gözükmüyor; her şey gerçek dünya gibi olsun mümkün mü?" → kuruldu; dürüst cevap: tam fotogerçekçilik mümkün değil, büyük adım mümkün.
8. "Pist orijinaldeki gibi olamaz mı (inek, viraj sonrası çiftlik evi…)? Tüm önerilerini kabul ediyorum, her şeyi uygula." → v0.3.0.
9. "Debug'da oyunu açarken ekranda gösterme, görünmez olsun." → `--invisible` bayrağı (K6).
10. "Lastik sesleri de gerçekçi olsun; araba da gerçeğine daha çok benzesin; oyun değil gerçek dünya gibi hissedilsin; elinden ne geliyorsa sonuna kadar yap." → v0.4 (sürüyor).
11. "Pause." → duruldu. Sonra: "Bu thread'i aynen başka bilgisayarda devam ettirmek istiyorum; git'e commit ederek de olur." → bu not + `wip/v0.4-realism` dalı.

## 4. Kesin kararlar (K) ve açık kararlar (A)

- K1 **Lisans**: marka/model adı yok, orijinalden kopya varlık yok; dış varlıklar yalnız CC0 (ambientCG, Poly Haven, Internet Archive CC0 öğeleri, Freesound CC0, BigSoundBank CC0) ve her biri SOURCES.md + THIRD_PARTY.md'de kayıtlı. Araba adları tanımdır: "FLAT-12 GT '84", "V8 GTB '12 SPORT EXHAUST". Pist, araçlar, dekor kendi üretimimiz.
- K2 **His = orijinal**: pist düzeni ve dekor orijinali andırır (START kapısı, hemzemin geçit + tren, ahır "33", inekler, turuncu/beyaz çiftlik evleri, GAS benzinci + pompalar, THE HUT, çiçekler, devrilen sarı levhalar, çizgili tahta bariyerler, tarlalar, çit ağaçları, gizli skid pad); kurallar kabin seçenek tablosundan (90 s, +45 kontrol noktası/tur, 10 s yol dışı); kaza → anlık tekrar → anahtar; 1989 modu (F8) her sürümde korunur.
- K3 **Görsel hedef**: her turda bir basamak daha gerçek dünya; mevcut araç: cross-section loft (CarView), dekor: dokulu primitifler. Gerçek 3B modele geçiş yol haritasında (design.md madde 5).
- K4 **Sesler gerçek kayıt**: motor loop'ları (v0.2), lastik loop'ları (v0.4); sentetik olanlar yalnız 1989 modu ve yardımcı katmanlar (yol uğultusu, rüzgâr, çukur, egzoz patlaması, inek, levha, tren, melodi).
- K5 **Git/yayın**: kullanıcı BU repoda commit/push'u açıkça yetkilendirdi (ekip kuralı md.1'in istisnası, yalnız bu repo). Commit mesajı ve PR gövdesi sonunda `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Yayın tek komut: `.\tools\release.ps1 -Version x.y.z -Notes $notes` (temiz ağaç ister; sürüm damgaları, testler, export, Inno, zip, sha256, winget manifestleri, commit+tag+push, GitHub release, Scoop PUT, wingetcreate PR).
- K6 **Debug koşuları görünmez**: her otomatik koşu `--position -32000,-32000 -- --invisible …` ile (pencere ekran dışında, odaksız, gizli; motor çizmeye devam ettiği için `--screenshot` çalışır). Kullanıcı pencerenin görünmesini istemiyor.
- K7 **Cevap biçimi**: çok kısa, madde madde, Türkçe; dosya linkleri workspace-göreli (mutlak `C:\` link yazma); sohbete kod bloğu yapıştırma; "yoruldunuz" deme; yes-man olma.
- K8 **Hafıza**: Claude hafızasına kullanıcıya sormadan yazılmaz.
- A1 (kullanıcı) Hafıza notu: "GrudgeLap dağıtım deseni + görünmez debug tercihi" kaydedilsin mi? Soruldu, cevap yok.
- A2 (kullanıcı) v0.4'te kokpit kamerası gerçek sürücü gözüne alındı (0.67 m geride, 1.00 m yükseklik, FOV 62°+6° hızla; eskisi ön cam dibinde 72°) ve direksiyon/ön panel görünür oldu (orijinalde yoktu). Kullanıcı henüz görmedi; beğenmezse `CameraRig.FollowCar` kokpit dalı eski değerlere döner, iç mekân `_cockpitOnly`/`_outsideOnly` listeleriyle gizlenir.
- A3 (kullanıcı) HUD gösterge şeridi ekranın alt %20'sini kaplıyor; 3B ön panele taşınsın mı (gerçekçilik) yoksa kalsın mı (orijinal his)?

## 5. Neredeyiz

- ✅ **v0.2.0** (yayında): iki araba, gerçek motor sesleri, trafik (drone'lar, sollama), hayalet tur (grudge match), arazi alanı (`TerrainField`), skid pad, 1989 modu, güncelleyici, kurulum/Scoop/winget, CI.
- ✅ **v0.3.0** (yayında, 2026-10-22): orijinal dekor + tren (`Train`, `TrainView`), çim/ağaç/tarla (`FoliageBuilder`), dokulu dekor (`PropBuilder`), aşınmış tekerlek izleri + çakıl banket + çelik korkuluk (`WorldBuilder`), tepe halkası, AgX/SSR/SSIL/hacimsel sis/TAA, yeni sesler (möö, levha çınlaması, tren), `--invisible`. Release: https://github.com/korayorhun/GrudgeLap/releases/tag/v0.3.0 ; winget PR https://github.com/microsoft/winget-pkgs/pull/458744 ; önceki makineye kuruldu (0.3.0).
- 🔄 **v0.4** (bu dalda, commit'li, YAYINLANMADI; derleme geçiyor, 37/37 test yeşil):
  - `TyreAudio.cs` + `game/assets/audio/tyres/{squeal,squeal_hard,skid,gravel}.wav` (CC0, SOURCES.md'de kesim yerleri); yanal kayma açısı > 0,07 rad squeal, boyuna kayma > 0,15 kilitlenme/patinaj, çimde çakıl loop'u.
  - `EngineAudio.cs`: `Road()` (yol uğultusu + rüzgâr + çukur vuruşu), `Pops()` (V8 gazı kesince 2-4 egzoz patlaması); sentetik squeal yalnız 1989 modunda.
  - `CarView.cs`: `BuildInterior` (döşeme, ön panel, gösterge kaportası, 22° yatık kolon üzerinde direksiyonla dönen direksiyon simidi `TurnSteeringWheel`, iki koltuk, A-direkleri `Strut`, silecekler), tonlu şeffaf cam, kaput/kapı/motor kapağı dikiş çizgileri (`TopSeams`/`FlankSeams` + `SeamWidth` istasyon çifti), plaka, sinyaller, kapı kolları, flat-12 burun yarığı, fren diski + kırmızı kaliper, lastik dokusu (triplanar cellular normal).
  - `CameraRig.cs`: sürücü gözü (−0,38; 1,00; −0,05), FOV 62°+6°, hız/çim titreşimi (`Roughness`, Bootstrap'tan besleniyor).
  - `Bootstrap.cs`: `--no-ghost` (araç fotoğrafı için hayaletsiz), `--invisible`.
  - Ekran görüntüleri: `docs/handoff/cockpit-v06.jpg`, `docs/handoff/araba-flat12-v06.jpg`.
- ⏸ **Durulan adım**: kokpit görüntüsünde sol A-direği (CarView `BuildInterior` → `Strut(body, a, b, 0.045f, trim)`) kameraya 0,2 m mesafede olduğu için ekranın solunda kalın siyah çapraz şerit olarak çıkıyor. Kullanıcı "pause" dedi; kullanıcıdan bekleyen soru yok (A1-A3 dışında).

## 6. Kanıtlı bulgular / tuzaklar (yeni thread yeniden keşfetmesin)

- Godot koordinatı: simülasyon +Z ileri, Godot −Z ileri; görünüm yalnız Z'yi çevirir (`Convert.ToGodot`). Godot ön yüzler saat yönünde; loft quad'ları ring teğetinin saat yönüne çevrilmişiyle yönlendirilir (centroid testi yanlış sonuç veriyordu).
- Export öncesi `--import` şart (taze checkout'ta `.godot/imported` yok → paket boş çıkar); `build.ps1` bunu yapıyor. `.uid` ve `.import` dosyaları git'te.
- Çıkışta "leaked reference" + SIGILL: statik Godot kaynakları motor kapanmadan bırakılmalı → `WorldBuilder.ReleaseShared()` (Foliage/Prop'u da çağırır) + GC, `Bootstrap.Shutdown()`.
- Pencere gizleme: `HideWindow()` → `Unfocusable`, `Position(-32000,-32000)`, `ShowWindow(SW_HIDE)`; MINIMIZE edilirse Godot çizmeyi durdurur, gizlemek durdurmaz.
- Inno `ISCC /Q` Git Bash'te bozuluyor → PowerShell'den çağır. `git commit -F -` PowerShell here-string'iyle çalışmaz → mesajı dosyaya yaz, `-F dosya`. Uzun bash heredoc'lar tırnaklarla bozuluyor → patch script'ini dosyaya yaz, `python -I dosya`.
- Lastik kaydı aramak: Freesound sayfası `https://freesound.org/s/<id>/` HTML'inde `cdn.freesound.org/previews/…-hq.mp3` önizleme linki var (girişsiz, 128 kbps; lisans sayfada "Creative Commons 0"). BigSoundBank: `https://bigsoundbank.com/UPLOAD/mp3/<id>.mp3`. Internet Archive: `https://archive.org/metadata/<item>` JSON'unda `licenseurl` + dosya listesi; Wayzata "Sound_Effects_2" öğesindeki .wav'lar aslında AIFF.
- Loop kesme: `tools/make-loop.py <in> <start> <dur> <out.wav> [xf] [peak]` (ffmpeg + numpy, 22,05 kHz mono); motor devri analizi `tools/analyze-engine.py <file> <cylinders>`; foliage atlasları `tools/make-foliage.py`.
- V8 0-60 4,5 s'nin kökü debriyaj senkron torkunun araç kütlesini içermesiydi (`Drivetrain`, `axleInertia = 2*WheelInertia + Mass*r²`); `TireGrip 1.25`, `TractionControl`, fırlatma tavanı.
- Tren: `OriginalInspiredTrack` → `new Train(crossingPoint, lateral, halfLength:160, speed:17, cars:4, pause:30, phase:33)`; `ScriptedDriver.PlanCrossing` koşusu yaklaşırken 14 m önce durur; `RaceSession.HitTrain()` kaza.
- Dekor çarpışması: `TrackProp.Solid` (ahır, ev, dükkân, kulübe, ağaç, kule, bariyer, tren, pompa) kaza; `Soft` (inek, levha, crossbuck) olay (`CowHit`, `SignHit` tek sefer; levha `Bootstrap.UpdateTipping` ile devrilir).
- Çim tuftları 150 m'de dither ile söner (`FoliageBuilder.Tuft` DistanceFade 110-170); tarlalarda tuft yok (`InField`).
- Köprü ayakları güvertenin 0,25 m altında biter (üstleri rampanın alçak ucunda yoldan çıkıyordu); korkuluk duvar değil, iki ince ray + 6 m'de bir direk.

## 7. Backlog (dokunulmadı)

- Araç gövdesi hâlâ 20 noktalı ring loftu (köşeli görünüm); daha çok ring noktası ya da gerçek 3B model (CC0 uyumlu bulunmalı) sıradaki büyük adım.
- Araçta `ReflectionProbe` (gerçek çevre yansıması) denenmedi — maliyet ölçülmeli.
- Egzoz patlaması için kayıt: Freesound #797835 "Car Antilag" CC0 (önizleme sessiz, ham); şimdilik sentetik.
- Hayalet ve drone'lar aynı loftu kullanıyor (iyi); drone iç mekânı da çizilir (maliyet küçük).
- CI yalnız `main` ve `v*` etiketlerinde koşar; `wip/*` dalı CI görmez.
- `docs/images/*.jpg` v0.3 görüntüleri; v0.4 sonrası yenilenmeli (`cockpit`, `chase`, `flat12`, `v8`, `retro`, `farm`).

## 8. Sıradaki adım (onay gelirse ilk iş)

Dalı klonla (`git checkout wip/v0.4-realism`), `dotnet test physics.tests/GrudgeLap.Physics.Tests.csproj` ve `dotnet build game/GrudgeLap.csproj` yeşil mi bak. Sonra A-direğini düzelt: `Strut` kalınlığını 0,03'e indir ve/veya direkleri yalnız dıştan görünür yap (`_outsideOnly` listesine ekle; kokpitte zaten ön cam çerçevesi yok) — gerçekçilik için ince ve geride kalması tercih; görünmez koşuyla `--attract --no-traffic --no-ghost --screenshot=… --at=20` al, bak. Ardından README (lastik kayıtları, iç mekân, kamera, `--no-ghost`), `docs/design.md` (ses/kamera/araç bölümleri), `docs/feel-checklist.md` (değişiklik yoksa dokunma), `docs/images` yenile; `git checkout main && git merge --ff-only wip/v0.4-realism`; `.\tools\release.ps1 -Version 0.4.0 -Notes $notes`; CI ve release'i doğrula; bu dosyayı sil; kullanıcıya kısa madde madde rapor (A1-A3'ü sor).

## 9. Kurallar hatırlatma

- Bu repoda git commit/push serbest (kullanıcı yetkisi); diğer repolarda ekip kuralı md.1 geçerli.
- Build/test/ekran görüntüsünü AI kendisi koşar (kişisel proje, kullanıcı istedi) — her zaman `--invisible`.
- Repo UTF-8 + LF; Türkçe karakter sorunu yok. Kod ve yorumlar İngilizce, yorum disiplini md.10 (neden/değişmez/tuzak yazılır, kod tekrarı yazılmaz).
- Kısa, madde madde, Türkçe cevap; göreli linkler; memory'ye sormadan yazma.

## 10. Erişim ve kurulum (yeni bilgisayar)

- Klon: `git clone https://github.com/korayorhun/GrudgeLap C:\_app\_tools\GrudgeLap` → `git checkout wip/v0.4-realism`.
- Araçlar: .NET 8 SDK; Godot 4.7.2 .NET (`winget install GodotEngine.GodotEngine.Mono`; `build.ps1` WinGet paket yolundaki `Godot_v*_mono_win64_console.exe`'yi kendisi bulur, değilse `-Godot <yol>`); export şablonları `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\` (indirme adımı `.github/workflows/build.yml` satır ~29-40, `.tpz` zip'tir); Inno Setup 6 (`winget install JRSoftware.InnoSetup`); GitHub CLI (`winget install GitHub.cli`, `gh auth login` → korayorhun, `gh auth token` wingetcreate için); `winget install Microsoft.WingetCreate`; ffmpeg (`winget install Gyan.FFmpeg`); Python 3 + `pip install --user pillow numpy` (scriptler `python -I` ile koşar ve kullanıcı site paketlerini kendileri ekler).
- Ekran görüntüsü: `<godot_console.exe> --path game --position -32000,-32000 -- --invisible --attract --no-traffic --no-ghost [--chase | --orbit=yaw,pitch,dist] [--car=v8|flat12] [--retro] [--speed-route] --screenshot=<png> --at=<s> --quit-after=<s>`; log'lar kişisel `_ai\log` altına (repoya girmez).
- Yerel kurulu kopya: `%LOCALAPPDATA%\Programs\GrudgeLap` (sessiz kurulum `GrudgeLap-Setup-x.y.z.exe /SILENT /NORESTART /SP-`); ayarlar ve en iyi tur `%APPDATA%\Godot\app_userdata\GrudgeLap`.
- Hesaplar/uzak: GitHub `korayorhun/GrudgeLap`, `korayorhun/scoop-bucket` (bucket/grudgelap.json), winget `KorayOrhun.GrudgeLap` (PR #458667 v0.2.0, #458744 v0.3.0).
- Önceki makinede kalan, GEREKMEYEN yerel dosyalar: ham CC0 indirmeler (`C:\temp\grudgelap-tyre`, `C:\temp\grudgelap-tex`), operatör kılavuzu PDF'i ve afişler (`_ai\hard-drivin\kaynak`; linkler `01-arastirma.md` sonunda), eski ekran görüntüleri.
