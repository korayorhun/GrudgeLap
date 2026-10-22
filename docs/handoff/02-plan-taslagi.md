# Hard Drivin' ruhunda modern oyun — plan taslağı (tartışmaya açık)

Dayanak: `01-arastirma.md`. Bu bir öneri; önce 3 karar gerekiyor (sonda).

## Hedef cümlesi
"1989 kokpitinin sürüş hissini (fizik + direksiyon kuvveti + ses) bugünün grafikleriyle, dağıtılabilir ve yasal bir oyunda yeniden yaratmak."

## Araştırmanın plana söyledikleri
1. **His = fizik + kuvvet döngüsü + ses, grafik değil.** Orijinalde fizik ayrı işlemcide 25-30 ms, kuvvet döngüsü 4 ms. Biz de fiziği motor/çizimden ayırıp sabit adımlı (≥250 Hz, kuvvet 1 kHz) çalıştırmalıyız. Görüntü 120+ Hz, hareket bulanıklığı kapalı.
2. **Kuvvet geri beslemesinin reçetesi patentte hazır ve serbest:** model kuvveti + sönümleme + sürtünme (yay-blok) + servo merkezleme; alçak geçiren filtre; koltuk/operatör ölçeği. Doğrudan uygulanabilir.
3. **Orijinal araba modeli "bisiklet + yalnız pitch"ti.** Birebir kopya zaten mümkün değil (denklemler yok). Doğru yol: Milliken tabanlı 4 tekerlekli model + Pacejka lastik, sonra *davranış hedeflerine* ayar (loop ≥60 mph, köprü 60-80 mph, 70+ mph spin, yol dışı "çamur", sert direksiyonda kayma, stop etme).
4. **Pist birebir çıkarılamaz, ama segment tablosu + harita elimizde.** 88 ft'lik slab ölçüsü, viraj açıları, 180° eğimli viraj, loop, açık köprü, kavşaklar, dönüş/kontrol noktaları, drone başlangıçları → "ruhen aynı" pisti kendi geometrimizle kurarız. Yayın için bu zaten gerekli.
5. **Hukuki sınır net:** ad, logo, 3B modeller, sesler, pist verisi WB'nin; mekanikler ve patentler serbest. Ferrari adı/şekli zaten orijinalde de lisanssızdı → kendi tasarımımız.
6. **Referans makine mümkün:** MAME + FFB Arcade Plugin + tüketici direksiyonu (yasal ROM ile) → "orijinal nasıl hissettiriyordu" ölçüm düzeneği.

## Önerilen teknoloji
| Katman | Öneri | Neden |
|---|---|---|
| Motor | **Unity 6 (C#)** | Siz C#'ta hızlısınız; FFB için hazır açık kaynak (Unity-DirectInput) + gerekirse kendi ince DirectInput sarmalayıcımız. Alternatif: UE5 (en iyi grafik, C++, "Simple Controller" FFB eklentisi). Godot'ta FFB yok (GDExtension yazmak gerekir). |
| Fizik | **Kendi C# kütüphanemiz** (motordan bağımsız, .NET Standard) | Hazır araç fiziği "aynı hissi" vermez; ayrı kütüphane = birim test + başsız (headless) ölçüm + ileride motor değişimi. |
| Direksiyon | Doğrudan tahrikli (Moza R5/R9, Fanatec CSL DD) tercih; Logitech G923/G29 minimum | Orijinal 1 ft·lb/A motor, 3 tur kilitten kilide; kayışlı tekerler sert "stiff" hissi tam veremez. |
| Pedal/vites | Yük hücreli fren (orijinal gerinim ölçerli = kuvvet), H-desenli vites + debriyaj (Logitech/Moza HGP) | Orijinalin fren hissi kuvvet tabanlı; vites direnci tüketici donanımında yok (kabul). |
| Ses | FMOD; motor sesi gerçek kayıtlardan devir/yük ile harmanlama (REV veya granüler) | "1989 DAC" filtresi seçenek olarak. |
| Grafik | Unity HDRP (ya da URP + iyi ışık) | Modern görünüm; ayrıca tek tuşla "1989 modu" (düz renk, 512×384, 15 fps kilit, pus paleti). |

## Fazlar
### F0 — Karar ve düzenek (1-2 hafta)
- 3 kararı ver (aşağıda). Repo/şablon, Unity sürümü, direksiyon temini.
- Referans düzeneği: MAME + FFB plugin + direksiyon (yasal ROM varsa). "His kontrol listesi" yaz: loop giriş hızı, köprü penceresi, spin eşiği, yol dışı davranışı, stop, direksiyon ağırlığı (Stiff/Medium), kaza→tekrar→dönüş noktası ritmi.

### F1 — His prototipi (4-6 hafta) — grafiksiz
- Fizik kütüphanesi: 4 tekerlek, Pacejka-lite lastik, motor tork eğrisi, 4 ileri + R + debriyaj + stop, süspansiyon (pitch/roll), yol dışı sürtünme, havada uçuş + iniş hasar eşiği, loop/eğimli yüzeylerde merkezcil tutunma.
- Kuvvet döngüsü: patent reçetesi (force + damping + friction + servo), 1 kHz, filtre, Very Light…Stiff ölçekleri.
- Basit debug çizimi (düz poligon) + telemetri kaydı/oynatma (kayıtlı girdilerle tekrarlanabilir test).
- Çıkış kriteri: kontrol listesindeki davranışlar kör testte "orijinal gibi" deniyor.

### F2 — Pist ve dünya iskeleti (3-4 hafta)
- Pist biçimi: segment listesi (düz / viraj açı+yarıçap / eğim / tepe / loop / açık köprü / kavşak) + slab = 88 ft; iki kol; kontrol/dönüş noktaları; görünmez bariyerler; drone başlangıç tanımları.
- "Original Track" ruhu: harita + segment tablosuna göre kendi geometrimiz (speed kolu uzun, stunt kolu: tepe → açık köprü → loop → eğimli 180° → birleşme). Skid pad gizli pisti dahil.
- Editör: basit, Stunts'tan ders alarak (undo, isimli dosyalar, ortadan segment değiştirme).

### F3 — Oyun kuralları (3 hafta)
- Jeton/başlat → vites tipi → kontak; süre tablosu (90/45/45/45 + zorluk ön ayarları); kontrol noktası ek süresi; tur uzatmaları.
- Kaza → 10 sn kuşbakışı tekrar (atlanabilir) → dönüş noktası; yol dışı 10 sn; ters yön; abort.
- Trafik: slab'e bağlı kayıtlı yörüngeli drone'lar (12'ye kadar), gelen/giden.
- Championship Lap: hayalet kaydı (konum+yön, ~60 Hz), yeterlilik süresi, tel kafes hayalet, isim girişi.
- "Operatör menüsü" saygı duruşu: zorluk, direksiyon kuvveti, mph/kmh, istatistikler.

### F4 — Ses (2-3 hafta)
- Motor (devir/yük), lastik, çarpma, bump, levha çınlaması, inek, tren rayı, kontrol noktası bleep'leri, stop.
- Özgün besteler: tekrar müziği, rekor girişi, şampiyonluk melodisi (orijinaller telifli).
- "Retro DAC" seçeneği: 12-bit, ~10 kHz, hafif kırpma.

### F5 — Modern grafik (6-10 hafta)
- Çiftlik dünyası: ahır, evler, dükkân, "kulübe", tren, inek, çiçek, tarla, kule/skid pad.
- Kendi araba tasarımı (kırmızı spor; Testarossa'nın şekil işaretlerini kopyalamadan).
- Kayan gösterge paneli (yanal G'ye göre; patent reçetesi serbest), çalışan göstergeler.
- Düşük gecikme: fizik ayrı iş parçacığı, 120+ Hz, interpolasyon; LOD; "1989 modu".

### F6 — Doğrulama ve cila (sürekli)
- Referans makineyle yan yana ölçüm (aynı girdiler → benzer tur süreleri/eşikler).
- Regresyon: his kontrol listesi + telemetri karşılaştırması her sürümde.

## Yasal kontrol listesi
- Kendi ad ve logo (öneri: "Hard Drivin'" / "Atari" geçmesin; alt başlıkta "1989 arcade klasiğine saygı" demek serbest).
- Hiçbir orijinal varlık yok: ROM verisi, 3B model, ses, müzik, pist koordinatı, afiş metni.
- Sızan ev kaynağından kod/veri kopyası yok (okumak ayrı, kopyalamak ayrı).
- Patentler süresi dolmuş → hayalet, kuvvet reçetesi, kayan pano, vites direnci serbest.
- Ferrari: ad, logo, at figürü, karakteristik yan ızgara/arka çizgiler yok.

## Riskler
- **Tüketici direksiyonunda sadakat:** kayışlı tekerle "stiff" hissi eksik → doğrudan tahrik ile başla.
- **Fizik ayarı veri olmadan:** aylar sürebilir; referans düzeneği ve kontrol listesi olmadan başlanmamalı.
- **FFB eklentisi bakımı:** Unity-DirectInput son doğrulama 2022.2 → gerekirse kendi ince sarmalayıcımız (P/Invoke dinput8).
- **Kapsam şişmesi:** modern grafik fazı F1-F3'ten önce başlarsa his kaybolur. Sıra: his → pist → kurallar → ses → grafik.

## Karar isteyen 3 soru
1. **Hedef:** yalnız kendimiz için mi (o zaman yasal ROM varsa arcade verisini de deşeriz), yoksa yayın (itch.io/Steam) mı?
2. **Motor:** Unity (C#, önerim) mi, Unreal (en iyi grafik, C++) mı?
3. **Donanım:** hangi direksiyon/pedal/vites seti var ya da alınacak? (Referans düzeneği için yasal bir Hard Drivin' kopyanız var mı?)

## Durum — 2026-10-22 (uygulandı)

Plan GrudgeLap olarak hayata geçti: https://github.com/korayorhun/GrudgeLap (MIT, public). Kaynak
`C:\_app\_tools\GrudgeLap`. Godot 4.7.2 .NET; fizik motor-bağımsız `physics/` kütüphanesinde (34 headless test).

- Yapıldı: iki araba (FLAT-12 GT '84, V8 GTB '12 SPORT EXHAUST; CC0 motor kayıtları), speed/stunt rotaları
  (köprü, loop, eğimli 180, çukurlar), iki yönlü trafik, en iyi turun hayaleti (grudge match), arazi alanı
  (toprak dolgu/yarma), gizli skid pad, 1989 modu, CC0 doku + HDRI, otomatik güncelleme, installer + portable
  zip, Scoop bucket, winget manifesti, CI.
- Yapılmadı: force-feedback direksiyon (donanım yok; klavye kapsamı), gerçek flat-12 kaydı (lisanslı kayıt
  bulunamadı; Countach V12 CC0 kaydı kullanıldı), 2B/UI dışı ek dil desteği.
- Ekran görüntüleri: `grudgelap/*-v04.png`.
