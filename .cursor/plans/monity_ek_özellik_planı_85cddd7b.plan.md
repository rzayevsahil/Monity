---
name: Monity Ek Özellik Planı
overview: Monity uygulaması için mevcut yol haritasının ötesinde, kullanıcı deneyimini ve işlevselliği artıracak ek özellik önerilerinin planlanması.
todos: []
isProject: false
---

# Monity Ek Özellik Önerileri Planı

## Mevcut Özellik Özeti

Monity şu anda şunları sunuyor:

- Foreground pencere takibi (1 sn polling), idle tespiti
- Dashboard: günlük toplam, saatlik grafik, uygulama listesi
- İstatistikler: dönem seçimi (günlük/haftalık/aylık/yıllık), özet kartlar, uygulama listesi
- Ayarlar: tema (açık/koyu), boşta kalma süresi, hariç tutulan uygulamalar, günlük süre kısıtları
- Tray bildirimleri (limit aşıldığında)
- Otomatik güncelleme

Mevcut yol haritasında (v2.3–2.7): İstatistik grafikleri, Dark mode, hariç tutulanların filtrelenmesi, başlangıç saati, Hakkında bölümü var.

---

## Önerilen Yeni Özellikler

### 1. Sistem Başlangıcında Otomatik Başlatma

**Neden:** Uygulama her açılışta takip yapmalı; kullanıcılar genelde bunu ister.

**Yapılacaklar:**

- Ayarlara "Windows başlangıcında otomatik başlat" checkbox
- Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` veya Task Scheduler
- `app_settings` tablosuna `start_with_windows` kaydı

**Etkilenen:** [SettingsPage.xaml](src/Monity.App/Views/SettingsPage.xaml), yeni `StartupService` (veya `UpdateService` benzeri servis)

---

### 2. Veri Dışa Aktarma (CSV / JSON)

**Neden:** Raporlama, yedekleme, dış analiz, veri taşıma.

**Yapılacaklar:**

- Ayarlar veya İstatistikler sayfasına "Dışa aktar" butonu
- Tarih aralığı seçimi (başlangıç–bitiş)
- CSV: tarih, uygulama, süre, oturum sayısı kolonları
- JSON: aynı veri yapısal formatta
- `SaveFileDialog` ile dosya yolu seçimi

**Veri:** Mevcut `GetDailyUsageAsync`, `GetWeeklyUsageAsync` ile uyumlu; yeni `ExportUsageToCsvAsync`, `ExportUsageToJsonAsync` metodları.

---

### 3. Eski Veri Silme / Veri Temizleme

**Neden:** Gizlilik, disk kullanımı, veritabanı boyutunun azaltılması.

**Yapılacaklar:**

- Ayarlara "Veri yönetimi" bölümü
- Seçenekler: "X günden eski verileri sil" (ör. 30, 90, 365 gün) veya "Tüm verileri sil"
- Onay dialog: "Bu işlem geri alınamaz. Emin misiniz?"
- `UsageRepository`: `DeleteDataOlderThanAsync(DateTime cutoff)`, `DeleteAllDataAsync()`

---

### 4. Tray İkon Tooltip’inde Güncel Kullanım

**Neden:** Hızlı bakışta bugünkü kullanımı görmek.

**Yapılacaklar:**

- Periyodik (örn. her 1 dk) `GetDailyTotalAsync(DateTime.Today)` çağrısı
- `NotifyIcon.Text`: `"Monity – Bugün: 3s 20dk"` benzeri metin
- Timer veya `DailyLimitCheckService` benzeri arka plan görevi ile güncelleme

**Etkilenen:** [MainWindow.xaml.cs](src/Monity.App/MainWindow.xaml.cs) (SetupTrayIcon), yeni `TrayUsageUpdater` veya mevcut servise entegrasyon.

---

### 5. Minimum Oturum Süresi Filtresi

**Neden:** Kısa süreli uygulama geçişleri (örn. 5 sn) gürültü oluşturuyor.

**Yapılacaklar:**

- Ayarlara "Minimum oturum süresi (saniye)" alanı (varsayılan 0 = kapalı)
- `TrackingEngine` veya `UsageTrackingService`: süre eşiği altındaki oturumları kaydetme
- `app_settings`: `min_session_seconds` (10, 30, 60 vb.)

---

### 6. Uygulama Kategorileri

**Neden:** Uygulamaları gruplamak (Sosyal, Geliştirme, Eğlence vb.) istatistikleri daha anlamlı kılar.

**Yapılacaklar:**

- Veritabanı: `app_categories` (id, name), `apps` tablosuna `category_id`
- Ayarlarda: Her uygulamaya kategori atama (dropdown veya özel editör)
- Dashboard / İstatistikler: kategori bazlı filtre ve toplam süre
- Varsayılan kategori atamaları (ör. Chrome→Tarayıcı, vs.) isteğe bağlı

**Zorluk:** Orta; schema değişikliği ve UI güncellemeleri gerekir.

---

### 7. Takvim / Isı Haritası Görünümü

**Neden:** Günlük kullanım yoğunluğunu tek bakışta görmek (GitHub contributions tarzı).

**Yapılacaklar:**

- Dashboard veya İstatistikler’e yeni bölüm: seçilen ay için grid (7 sütun x 4–5 satır)
- Her hücre = bir gün, renk = o günün toplam süresine göre yoğunluk
- Veri: `GetDailyTotalsInRangeAsync(startOfMonth, endOfMonth)`
- LiveCharts veya basit Rectangle grid ile görselleştirme

---

### 8. Haftalık Karşılaştırma Özeti

**Neden:** "Bu hafta geçen haftaya göre ne kadar farklı?" sorusuna hızlı cevap.

**Yapılacaklar:**

- İstatistikler’e kart veya metin: "Bu hafta: X saat | Geçen hafta: Y saat | Fark: ±Z saat"
- `GetRangeTotalAsync` ile bu hafta ve geçen hafta aralıkları
- Yüzde veya mutlak fark gösterimi

---

### 9. Günlük Limit Aşımında Uygulama Kilitleme (İsteğe Bağlı)

**Neden:** Sadece bildirim yerine limit aşıldığında uygulamayı kapatma veya minimize etme.

**Yapılacaklar:**

- Ayarlarda "Limit aşıldığında uygulamayı kapat" seçeneği
- `DailyLimitCheckService`: limit aşıldığında `Process.Kill(processName)` veya pencere minimize
- Dikkat: Zorla kapatma agresif olabilir; kullanıcıya açık seçenek olarak sunulmalı

---

### 10. Dil Desteği (i18n)

**Neden:** Uygulama şu an Türkçe; İngilizce veya diğer dillerle kullanım artabilir.

**Yapılacaklar:**

- `Resources` klasöründe `Strings.tr.xaml`, `Strings.en.xaml`
- Ayarlarda dil seçimi (Türkçe / English)
- Tüm sabit metinler `{x:Static}` veya binding ile resource’lardan alınır
- `app_settings`: `language` (tr, en)

---

## Öncelik Sıralaması Önerisi


| Öncelik | Özellik                                | Zorluk | Değer  |
| ------- | -------------------------------------- | ------ | ------ |
| 1       | Sistem başlangıcında otomatik başlatma | Düşük  | Yüksek |
| 2       | Tray tooltip’te güncel kullanım        | Düşük  | Orta   |
| 3       | Veri dışa aktarma (CSV)                | Düşük  | Yüksek |
| 4       | Eski veri silme                        | Düşük  | Orta   |
| 5       | Minimum oturum süresi                  | Düşük  | Orta   |
| 6       | Haftalık karşılaştırma özeti           | Düşük  | Orta   |
| 7       | Takvim ısı haritası                    | Orta   | Yüksek |
| 8       | Uygulama kategorileri                  | Orta   | Yüksek |
| 9       | Limit aşımında kilitleme               | Orta   | Orta   |
| 10      | Dil desteği (i18n)                     | Orta   | Orta   |


---

## Uygulama Önerisi

Önce 1–6 numaralı özellikler (düşük zorluk, hızlı kazanım) ile başlanması mantıklı. Uygulama kategorileri ve dil desteği daha büyük refactoring gerektirir; takvim ısı haritası ise görsel olarak en etkili ancak biraz daha iş gerektirir.