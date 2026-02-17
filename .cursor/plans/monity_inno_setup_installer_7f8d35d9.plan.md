---
name: Monity Inno Setup Installer
overview: Inno Setup ile Monity için kurulum programı (Monity-Setup-x.x.x.exe) oluşturulacak. Kurulum masaüstü ve Başlat menüsü kısayolları ekleyecek, Programlar listesinde görünecek; kaldırma da desteklenecek.
todos: []
isProject: false
---

# Monity Inno Setup Installer Planı

## Hedef

Kullanıcı **Monity-Setup-1.0.0.exe** çalıştırınca:

- Kurulum klasörüne (varsayılan: `%LocalAppData%\Monity`) tüm dosyalar kopyalanacak
- Masaüstü kısayolu oluşturulacak
- Başlat menüsünde "Monity" klasörü ve uygulama kısayolu olacak
- Programlar ve Özellikler listesinde görünecek
- Kaldırma ile tüm dosyalar silinebilecek

---

## 1. Inno Setup Kurulumu (manuel)

- [Inno Setup](https://jrsoftware.org/isinfo.php) indirilip kurulacak (ücretsiz, ~5 MB)
- Sadece geliştirici makinede gerekli; kullanıcılar sadece Setup exe çalıştırır

---

## 2. Proje Yapısı

Yeni dizin: `installer/` (çözüm kökünde)

```
monity/
├── installer/
│   └── Monity.iss          # Inno Setup script
├── src/
│   ├── Monity.App/
│   └── Monity.Updater/
└── ...
```

---

## 3. Inno Setup Script (Monity.iss)

Script şunları yapacak:


| Bölüm               | Açıklama                                                                                                                                             |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[Setup]`           | AppId, AppName (Monity), AppVersion (1.0.0), DefaultDirName (`{localappdata}\Monity`), OutputBaseFilename (Monity-Setup-1.0.0), UninstallDisplayName |
| `[Files]`           | Publish klasöründeki tüm dosyaları `{app}` altına kopyala; Updater.exe'yi de ekle                                                                    |
| `[Icons]`           | Masaüstü kısayolu: `{commondesktop}\Monity.lnk` → `{app}\Monity.App.exe`                                                                             |
| `[Icons]`           | Başlat menüsü: `{group}\Monity.lnk` → `{app}\Monity.App.exe`                                                                                         |
| `[UninstallDelete]` | Kaldırırken `{localappdata}\Monity\Update`, `{localappdata}\Monity\Logs` vb. temizle (isteğe bağlı)                                                  |


**Kaynak yolu:** Script, App ve Updater publish klasörlerine göre path alacak. Örn.:

- App: `..\src\Monity.App\bin\Release\net8.0-windows\win-x64\publish\`
- Updater: `..\src\Monity.Updater\bin\Release\net8.0\win-x64\publish\Monity.Updater.exe` → `{app}\Updater.exe` olarak kopyalanacak

**Sürüm:** Script içinde `#define MyAppVersion "1.0.0"` gibi sabit; her release’te manuel güncellenebilir veya MSBuild/script ile otomatik enjekte edilebilir (opsiyonel).

---

## 4. Build Akışı

```mermaid
flowchart LR
    A[1. Publish App] --> B[2. Publish Updater]
    B --> C[3. Inno Setup compile]
    C --> D[Monity-Setup-x.x.x.exe]
```



**Adımlar:**

1. `dotnet publish src/Monity.App/... -c Release -r win-x64 --self-contained`
2. `dotnet publish src/Monity.Updater/... -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
3. Inno Setup GUI’den `installer/Monity.iss` açıp **Build → Compile** veya komut satırından: `iscc installer/Monity.iss`

Çıktı: `installer/Output/Monity-Setup-1.0.0.exe`

---

## 5. Release Workflow Güncellemesi

GitHub release’e eklenecek dosyalar:


| Dosya                      | Açıklama                                             |
| -------------------------- | ---------------------------------------------------- |
| `Monity-Setup-1.0.0.exe`   | Kurulum programı (öncelikli, yeni kullanıcılar için) |
| `Monity-1.0.0-win-x64.zip` | Portable zip (mevcut; in-app güncelleme için)        |


- **İlk kurulum:** Kullanıcı `Monity-Setup-1.0.0.exe` indirip çalıştırır
- **Otomatik güncelleme:** Uygulama içi güncelleme zip’i indirip Updater ile uygular (mevcut akış aynı kalır)

---

## 6. Kurulum Konumu

Varsayılan: `%LocalAppData%\Monity` (`C:\Users\<user>\AppData\Local\Monity`)

- Admin gerektirmez
- Kullanıcı bazlı
- Veritabanı (`monity.db`) zaten `%LocalAppData%\Monity` altında; kurulumla uyumlu

---

## 7. Yapılacaklar Özeti


| Adım                     | İçerik                                                                               |
| ------------------------ | ------------------------------------------------------------------------------------ |
| Inno Setup kurulumu      | Geliştirici makinede Inno Setup indir/kur                                            |
| Monity.iss oluşturma     | installer/Monity.iss dosyası; [Setup], [Files], [Icons], [UninstallDelete] bölümleri |
| Path yapılandırması      | App publish ve Updater publish yollarını script’e göre ayarla                        |
| Build script (opsiyonel) | Tek komutla publish + Inno compile yapan batch/ps1 (isteğe bağlı)                    |
| README güncelleme        | İndirme talimatında Setup exe vurgulanacak                                           |


---

## 8. Opsiyonel: Otomatik Build

`build-release.ps1` veya `build-release.bat`:

1. dotnet publish App
2. dotnet publish Updater
3. `& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Monity.iss`

Bu sayede tek komutla Setup exe üretilebilir.