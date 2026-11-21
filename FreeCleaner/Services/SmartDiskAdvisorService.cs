using System.IO;

namespace FreeCleaner.Services;

public sealed record SmartFileAdvice(
    string RiskCategory,
    string BadgeText,
    string Explanation,
    bool CanSafeDeleteToRecycleBin);

/// <summary>
/// Recognizes well-known system, browser, AI, developer, and VM files/folders and provides actionable safety advice.
/// </summary>
public static class SmartDiskAdvisorService
{
    public static bool IsProtectedSystemPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return true;

        string normalized = Path.GetFullPath(fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Drive roots (C:\, D:\)
        if (normalized.Length <= 3 && normalized.Contains(':'))
            return true;

        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appDataRoot = Path.GetDirectoryName(localAppData) ?? string.Empty;

        string[] exactProtectedRoots =
        [
            windowsDir,
            programFiles,
            programFilesX86,
            programData,
            Path.GetDirectoryName(userProfile) ?? @"C:\Users",
            userProfile,
            appDataRoot,
            localAppData,
            roamingAppData,
            Path.Combine(windowsDir, "System32"),
            Path.Combine(windowsDir, "SysWOW64"),
            Path.Combine(windowsDir, "WinSxS"),
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\hiberfil.sys",
            @"C:\pagefile.sys",
            @"C:\swapfile.sys"
        ];

        foreach (string protectedPath in exactProtectedRoots)
        {
            if (string.IsNullOrWhiteSpace(protectedPath))
                continue;

            string p = protectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Equals(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        string fileName = Path.GetFileName(normalized);
        if (fileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("ntuser.dat", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("bootmgr", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Everything directly under Windows or System32 (except safe temp/logs handled explicitly)
        if (normalized.StartsWith(Path.Combine(windowsDir, "System32"), StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(Path.Combine(windowsDir, "SysWOW64"), StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(Path.Combine(windowsDir, "WinSxS"), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static SmartFileAdvice AnalyzePath(string fullPath, string? name = null, bool isDirectory = true)
    {
        string displayName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(fullPath) : name;
        string p = fullPath ?? string.Empty;

        if (displayName.StartsWith("Diğer Küçük Öğeler", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartFileAdvice(
                "Özet",
                "📊 Gruplanmış Küçük Öğeler",
                "Görünümü sade tutmak için birleştirilmiş küçük dosya ve klasörlerin toplamıdır.",
                CanSafeDeleteToRecycleBin: false);
        }

        // 1. Critical Windows System Files (Do NOT delete directly!)
        if (displayName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartFileAdvice(
                "Sistem",
                "⛔ Sistem — powercfg ile Yönet",
                "Hazırda Bekletme (Hibernate) sistem dosyasıdır. Doğrudan silinemez; 'Dev Sistem Alanı Kurtarıcısı' ile güvenle küçültülebilir.",
                CanSafeDeleteToRecycleBin: false);
        }

        if (displayName.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
            displayName.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartFileAdvice(
                "Sistem",
                "⛔ Windows Sanal Bellek",
                "Windows bellek yönetim dosyasıdır; doğrudan silinmesi sistem kararsızlığına yol açar.",
                CanSafeDeleteToRecycleBin: false);
        }

        if (IsProtectedSystemPath(p))
        {
            return new SmartFileAdvice(
                "Korunan",
                "⛔ Windows Sistem — Silme!",
                "Temel işletim sistemi veya kritik kullanıcı kök klasörüdür; doğrudan silmeye karşı kilitlenmiştir.",
                CanSafeDeleteToRecycleBin: false);
        }

        // 2. Chrome / Edge / Brave On-Device AI Models (OptGuideOnDeviceModel / weights.bin)
        if (ContainsAny(p, "OptGuideOnDeviceModel", "EdgeOnDeviceModel") ||
            displayName.Equals("weights.bin", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartFileAdvice(
                "Güvenli",
                "✅ Tarayıcı AI Modeli (Silinebilir)",
                "Chrome/Edge'in arka planda indirdiği cihaz içi Gemini/Phi modelidir. Silmek şifre/yer imlerini etkilemez; yeniden indirmeyi Tweaker'dan engelleyebilirsiniz.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 3. Claude Desktop VM / Docker / WSL2 VHDX
        if (ContainsAny(p, "Claude-Data", "AnthropicClaude", "vm_bundles", "claudevm.bundle") ||
            displayName.Equals("rootfs.vhdx", StringComparison.OrdinalIgnoreCase) ||
            displayName.Equals("ext4.vhdx", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
        {
            return new SmartFileAdvice(
                "Sanal Disk",
                "🗜️ VHDX Sıkıştır / VM Kapatıp Sil",
                "Sanal makine disk imajıdır (.vhdx). İçindeki veriyi kaybetmeden küçültmek için VHDX Sıkıştırma modülünü kullanabilir veya VM kapalıyken silebilirsiniz.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 4. Local LLM & AI Caches
        if (ContainsAny(p, @"\.ollama\", @"\huggingface\", @"\.lmstudio\", @"\.cache\torch", @"\.cache\whisper"))
        {
            return new SmartFileAdvice(
                "AI",
                "🤖 Yerel AI Model / Önbellek",
                "Ollama, HuggingFace, LM Studio veya PyTorch yerel model dosyalarıdır. Kullanmadığınız modelleri güvenle silebilirsiniz.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 5. GPU Driver Extracted Installers & Windows.old
        if (ContainsAny(p, @"C:\NVIDIA", @"C:\AMD", @"C:\Intel", @"NVIDIA Corporation\Installer2", @"\Windows.old"))
        {
            return new SmartFileAdvice(
                "Güvenli",
                "✅ Eski Kurulum / Driver (Silinebilir)",
                "Sürücü kurulumu veya Windows yükseltmesinden kalan çıkarılmış kurulum dosyalarıdır; güvenle silinebilir.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 6. Developer Dependency & Build Artifact Caches
        if (ContainsAny(displayName, "node_modules", ".nuget", ".gradle", "npm-cache", "pnpm-store", "CrashDumps", "ShaderCache", "DXCache", "GPUCache") ||
            ContainsAny(p, @"\.m2\repository", @"\.cargo\registry", @"\pip\Cache", @"\AppData\Local\Temp", @"\Windows\Temp", @"\SoftwareDistribution\Download", @"\Windows\WER"))
        {
            return new SmartFileAdvice(
                "Güvenli",
                "✅ Önbellek / Paket (Güvenle Sil)",
                "Gerektiğinde geliştirici aracı veya Windows tarafından otomatik yeniden oluşturulan geçici önbellek/paket deposudur.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 7. Android AVD / SDK / Emulators
        if (ContainsAny(p, @"\.android\avd", @"\Android\Sdk"))
        {
            return new SmartFileAdvice(
                "Geliştirici",
                "⚠️ Android Emülatör / SDK",
                "Android Studio sanal cihaz imajı (.img) veya SDK dosyasıdır. Sadece artık kullanmadığınız cihazları kaldırın.",
                CanSafeDeleteToRecycleBin: true);
        }

        // 8. Chat / Media / Game Caches
        if (ContainsAny(p, @"\discord\", @"\Spotify\", @"\Slack\", @"\MSTeams") && ContainsAny(p, "Cache", "Code Cache", "GPUCache"))
        {
            return new SmartFileAdvice(
                "Güvenli",
                "✅ Uygulama Önbelleği",
                "Sohbet/müzik uygulamasının geçici medya önbelleğidir; hesap verilerinize dokunmadan temizlenebilir.",
                CanSafeDeleteToRecycleBin: true);
        }

        if (ContainsAny(p, @"\Steam\steamapps", @"\Epic Games\"))
        {
            return new SmartFileAdvice(
                "Oyun",
                "🎮 Oyun Klasörü",
                "Yüklü PC oyun dosyalarıdır; silmek yerine Steam/Epic istemcisinden kaldırmanız önerilir.",
                CanSafeDeleteToRecycleBin: true);
        }

        if (ContainsAny(p, @"\Downloads", @"\İndirilenler"))
        {
            return new SmartFileAdvice(
                "Kullanıcı",
                "📥 İndirilen Öğe",
                "İndirilenler klasöründeki dosya/kurulum paketidir; ihtiyacınız yoksa Geri Dönüşüm Kutusuna taşıyabilirsiniz.",
                CanSafeDeleteToRecycleBin: true);
        }

        return isDirectory
            ? new SmartFileAdvice("İncele", "📁 Klasör (İncelenebilir)", "Kullanıcı veya uygulama klasörüdür; içine girip büyük dosyaları inceleyebilirsiniz.", CanSafeDeleteToRecycleBin: true)
            : new SmartFileAdvice("İncele", "📄 Büyük Dosya", "Doğrudan silmeden önce dosya uzantısını ve konumunu kontrol edin.", CanSafeDeleteToRecycleBin: true);
    }

    private static bool ContainsAny(string source, params string[] needles)
    {
        foreach (string n in needles)
        {
            if (source.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
