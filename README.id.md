# AppReset

Kembalikan aplikasi Windows seperti saat pertama kali dibuka, tanpa perlu uninstall.

AppReset mencari pengaturan dan data yang disimpan program terpasang di folder pengguna dan registry Windows, menunjukkan apa saja yang ditemukan, menyimpan cadangan, lalu hanya menghapus yang kamu centang. Programnya tetap terpasang.

[Read in English](README.md)

> Status: versi awal (0.1.0). Baca bagian keamanan sebelum memakainya di PC yang penting.

## Kenapa dibuat

Windows bisa mereset aplikasi dari Microsoft Store (Settings, Apps, Installed apps, Advanced options, Reset), tapi tidak untuk program biasa yang dipasang dengan installer `.exe` atau `.msi`. Uninstaller menghapus programnya, padahal yang biasanya dibutuhkan setelah update bermasalah atau pengaturan berantakan adalah program tetap ada dan pengaturannya bersih. Biasanya itu berarti mencari manual di `AppData` dan registry. AppReset melakukan pencarian itu dan kamu yang memutuskan.

## Yang dilakukan

1. Menampilkan daftar program yang terpasang menurut Windows.
2. Untuk program yang dipilih, mencari di:
   - `%APPDATA%` (AppData\Roaming)
   - `%LOCALAPPDATA%` (AppData\Local)
   - `AppData\LocalLow`
   - `%PROGRAMDATA%`
   - `HKEY_CURRENT_USER\Software`
   - `HKEY_LOCAL_MACHINE\SOFTWARE` dan tampilan 32-bit `WOW6432Node`
3. Menampilkan setiap temuan beserta alasan dan tingkat kecocokannya:

   | Kecocokan | Contoh | Dicentang otomatis? |
   |---|---|---|
   | Cocok persis | `AppData\Roaming\7-Zip` untuk 7-Zip | Ya, untuk data milikmu sendiri |
   | Kemungkinan besar | folder bernama sama dengan file program, atau seluruh folder penerbit kalau tidak ada aplikasi lain dari penerbit itu | Tidak |
   | Mungkin | folder yang namanya mengandung nama aplikasi | Tidak |

   Data yang dipakai bersama semua pengguna PC (`ProgramData`, `HKEY_LOCAL_MACHINE`) tidak pernah dicentang otomatis, karena installer sering menyimpan file yang dibutuhkan program di sana.
4. Menyimpan cadangan semua yang dicentang ke `%LOCALAPPDATA%\AppReset\Backups`, lalu menghapusnya.
5. Cadangan bisa dipulihkan kapan saja dari tab Cadangan.

## Yang tidak dilakukan

- Tidak meng-uninstall program dan tidak menyentuh folder programnya.
- Tidak mencari kunci lisensi, data aktivasi, atau penanda trial, dan bukan alat untuk mengakali lisensi software. Pull request yang menambahkan fitur itu akan ditutup.
- Tidak menyentuh akun atau data yang tersimpan online. Kalau aplikasi menyinkronkan pengaturan dari server, pengaturan itu bisa kembali setelah login.
- Hanya memeriksa lokasi standar di atas. Aplikasi yang menyimpan data di tempat lain (misalnya `Documents`) tidak tereset sepenuhnya.

## Keamanan

AppReset menghapus data, jadi aturan tentang apa yang boleh disentuh dibuat ketat dan ada di satu tempat: [`SafetyRules.cs`](src/AppReset.Core/SafetyRules.cs). Aturan ini diperiksa saat memindai dan diperiksa lagi tepat sebelum ada yang dihapus atau dipulihkan.

- Hanya folder satu atau dua tingkat di dalam folder data di atas, dan hanya kunci registry satu atau dua tingkat di dalam `Software`.
- Tidak pernah menyentuh folder dan kunci milik Windows atau yang dipakai bersama: `Microsoft`, `Windows`, `Temp`, `Packages`, `Programs`, `Classes`, `Policies`, dan lainnya.
- Tidak pernah menyentuh folder program, walaupun letaknya di dalam AppData (instalasi per pengguna seperti Discord dan VS Code).
- Tidak pernah mengikuti junction atau symbolic link.
- Folder penerbit yang dipakai beberapa aplikasi terpasang tidak pernah ditawarkan secara utuh.
- Item yang gagal dicadangkan sepenuhnya (misalnya karena ada file yang sedang dibuka) dibiarkan di tempatnya.
- Item untuk seluruh PC butuh AppReset dibuka ulang sebagai administrator. Selain itu AppReset berjalan sebagai pengguna biasa.
- Kalau Windows meminta akun administrator lain saat dibuka ulang sebagai administrator, AppReset akan bekerja pada data pengguna akun itu, bukan akunmu. Path yang ditampilkan memuat nama pengguna, jadi periksa dulu.
- Reset diblokir dan ada peringatan selama aplikasinya masih berjalan.

Aturan pencocokan nama dan keamanan diuji dengan unit test di [`tests/`](tests/AppReset.Tests).

## Unduh

Ambil `AppReset.exe` dari halaman [Releases](../../releases). Hanya satu file, jalan di Windows 10 dan 11 (64-bit) tanpa perlu memasang apa pun.

Windows SmartScreen mungkin memberi peringatan karena file belum ditandatangani (code-signed). Kamu bisa memeriksa source code-nya dan membangunnya sendiri (lihat di bawah).

## Membangun dari source

Butuh [.NET 10 SDK](https://dotnet.microsoft.com/download) di Windows.

```powershell
git clone <repositori ini>
cd AppReset
dotnet test tests/AppReset.Tests
dotnet run --project src/AppReset.App
```

Untuk membuat satu file exe seperti di halaman rilis:

```powershell
dotnet publish src/AppReset.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
```

Setiap push ke `main` membangun exe lewat GitHub Actions. Push tag seperti `v0.1.0` sekaligus membuat rilis dengan exe terlampir.

## Kontribusi

Laporan bug berisi nama aplikasi, penerbitnya, dan apa yang ditemukan (atau terlewat) oleh AppReset adalah bantuan paling berguna.

Kalau mengubah apa yang boleh disentuh AppReset, tambahkan test di `SafetyRulesTests.cs` atau `DataScannerTests.cs` yang menunjukkan kasus barunya. Untuk menambah bahasa, salin `Strings/Strings.en.xaml`, terjemahkan isinya (key-nya jangan diubah), lalu tambahkan ke daftar bahasa di `MainViewModel.cs`.

## Lisensi

MIT. Lihat [LICENSE](LICENSE).
