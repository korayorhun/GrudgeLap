# GrudgeLap: devam talimatı

Yeni bir thread bu dosyayı okuyup buradaki talimatla devam eder. Sahibin yapıştıracağı tek satır:

> GrudgeLap'e devam: https://github.com/korayorhun/GrudgeLap/blob/main/docs/DEVAM.md dosyasını oku (yerelde C:\_app\_tools\GrudgeLap\docs\DEVAM.md) ve oradaki talimatla devam et.

## Talimat

1. **Bu proje kişiseldir ve her şeyden bağımsız ilerler.** İş ortamına ait skill, mod ve kuralları bu projede kullanma. Bu repo dışındaki hiçbir projeye, depoya ya da sisteme dokunma ve oralara bu projeyle ilgili bir şey yazma. Geçici dosyalar ve kayıtlar için yerel bir log klasörü kullanılabilir.
2. **Repo:** yerelde `C:\_app\_tools\GrudgeLap`; yoksa https://github.com/korayorhun/GrudgeLap oraya klonlanır. Eksik araçlar README'nin "Build from source" bölümündeki gibi kurulur. Bu repoda commit, push ve GitHub Release yetkin var; bu yetki başka hiçbir repoya geçmez.
3. **İlk iş isterleri oku:** [ISTERLER.md](ISTERLER.md). İsterler, durumları, açık işler ve çalışma kuralları orada; 11. bölümdeki kurallara uy.
4. **İsterleri güncel tut:** sahip yeni bir istek verince önce ISTERLER.md'ye satır olarak ekle. İş bitip yayınlanınca o satırın durumunu ve sürümünü güncelle ve aynı commit'le gönder.
5. **Kurallar:**
   - Oyunu her zaman gizli ve sessiz çalıştır: Unreal `-RenderOffScreen -NoSound` (ses kaydı gerekiyorsa `-GlpMute`) ve `t.MaxFPS 60`; Godot (yalnız dünyayı dışa aktarırken) `--audio-driver Dummy --position -32000,-32000 -- --invisible`.
   - Tek sürüm Unreal (sahibin 14 Kasım 2026 kararı, ISTERLER H9): geliştirme yalnız `unreal/` ile ortak `physics/` ve `audio/` içinde. `game/` (Godot) dondurulmuştur; ISTERLER bölüm 12'deki taşıma listesi bitene kadar kaynak ve dünya dışa aktarım aracı olarak durur.
   - Her iş bitince derle, test et ve yeni sürüm olarak yayınla.
   - Model ve ses yalnız CC0 ya da CC BY olur ve kaynağı güvenilir olur; her kaynak [THIRD_PARTY.md](../THIRD_PARTY.md)'de anılır. Sahibin 8 Kasım 2026 izniyle ("izin veriyorum"; oyun ücretsiz) BlendSwap'in "Fan Art" işaretli (ticari kullanım yasak) CC BY ve CC BY-SA modelleri de kullanılabilir; kısıtları THIRD_PARTY.md'de yazılır (ISTERLER A8).
   - Cevaplar Türkçe, kısa ve madde madde.
6. **Başlarken:** ISTERLER.md'nin 10. bölümündeki açık işleri kısaca özetle. Sahip yeni bir istek verirse onunla başla.

Bu talimat değişirse bu dosya güncellenir; yeni thread hep buradaki son hâli okur.
