using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class SeventhGenSecurityAndPowerSuiteService
{
    // ==================== 1. PROCESS SECURITY & UNSIGNED BINARY HUNTER ====================

    public static async Task<List<ProcessSecurityAuditItem>> ScanRunningProcessSecurityAsync(bool onlyUnsignedOrSuspicious = false)
    {
        return await Task.Run(() =>
        {
            var results = new List<ProcessSecurityAuditItem>();
            var exeCache = new Dictionary<string, (string SigStatus, string Publisher, string HashShort, bool IsUnsignedOrSuspicious)>(StringComparer.OrdinalIgnoreCase);

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id <= 4) continue;

                    string? exePath = null;
                    try
                    {
                        exePath = proc.MainModule?.FileName;
                    }
                    catch
                    {
                        // Protected system process (e.g. csrss, svchost without elevation)
                    }

                    if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                        continue;

                    if (!exeCache.TryGetValue(exePath, out var cached))
                    {
                        cached = InspectExecutableSecurity(exePath);
                        exeCache[exePath] = cached;
                    }

                    if (onlyUnsignedOrSuspicious && !cached.IsUnsignedOrSuspicious)
                        continue;

                    long ws = 0;
                    try { ws = proc.WorkingSet64; } catch { }

                    results.Add(new ProcessSecurityAuditItem
                    {
                        ProcessId = proc.Id,
                        ProcessName = $"{proc.ProcessName}.exe",
                        ExecutablePath = exePath,
                        SignatureStatus = cached.SigStatus,
                        PublisherName = cached.Publisher,
                        MemoryFormatted = CleanerRule.FormatBytes(ws),
                        Sha256Short = cached.HashShort,
                        IsUnsignedOrSuspicious = cached.IsUnsignedOrSuspicious
                    });
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            return results
                .OrderByDescending(x => x.IsUnsignedOrSuspicious)
                .ThenBy(x => x.ProcessName)
                .ToList();
        });
    }

    private static (string SigStatus, string Publisher, string HashShort, bool IsUnsignedOrSuspicious) InspectExecutableSecurity(string exePath)
    {
        bool isSuspiciousDir =
            exePath.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) ||
            exePath.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase) ||
            exePath.Contains(@"\Users\Public\", StringComparison.OrdinalIgnoreCase);

        string sigStatus = "⚠️ İmzasız (Unsigned)";
        string publisher = "Bilinmeyen / İmzasız Üretici";
        bool isUnsigned = true;

        try
        {
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(exePath);
            using var cert2 = new X509Certificate2(cert);
#pragma warning restore SYSLIB0057
            string cn = ExtractCommonName(cert2.Subject);
            publisher = string.IsNullOrWhiteSpace(cn) ? cert2.Subject : cn;
            sigStatus = isSuspiciousDir
                ? "⚠️ İmzalı (Geçici Klasörde!)"
                : "✅ Dijital İmzalı";
            isUnsigned = false;
        }
        catch
        {
            if (isSuspiciousDir)
                sigStatus = "🚨 İMZASIZ & ŞÜPHELİ DİZİN (Temp/Downloads)";
        }

        string shaShort = "-";
        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] hash = SHA256.HashData(fs);
            shaShort = Convert.ToHexString(hash)[..16] + "...";
        }
        catch { }

        return (sigStatus, publisher, shaShort, isUnsigned || isSuspiciousDir);
    }

    private static string ExtractCommonName(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return string.Empty;
        var match = Regex.Match(subject, @"CN=([^,]+)");
        return match.Success ? match.Groups[1].Value.Trim('"') : subject;
    }

    // ==================== 2. BATCH FILE RENAMER (POWERRENAME) ====================

    private static readonly List<(string NewPath, string OldPath)> LastRenameUndoLog = [];

    public static List<BatchRenamePreviewItem> BuildBatchRenamePreview(
        string folderPath,
        string findText,
        string replaceText,
        string prefixText,
        string suffixText,
        bool addSequenceNumber,
        string caseMode)
    {
        var list = new List<BatchRenamePreviewItem>();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return list;

        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .Take(500)
                .ToList();

            int seq = 1;
            foreach (var file in files)
            {
                string dir = Path.GetDirectoryName(file) ?? folderPath;
                string origFileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file);
                string baseName = Path.GetFileNameWithoutExtension(file);

                string transformed = baseName;

                if (!string.IsNullOrEmpty(findText))
                {
                    transformed = transformed.Replace(findText, replaceText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }

                transformed = caseMode switch
                {
                    "küçük harf (lowercase)" => transformed.ToLower(new CultureInfo("tr-TR")),
                    "BÜYÜK HARF (UPPERCASE)" => transformed.ToUpper(new CultureInfo("tr-TR")),
                    "Baş Harfler Büyük (Title Case)" => new CultureInfo("tr-TR").TextInfo.ToTitleCase(transformed.ToLower(new CultureInfo("tr-TR"))),
                    _ => transformed
                };

                if (!string.IsNullOrEmpty(prefixText))
                    transformed = prefixText + transformed;

                if (!string.IsNullOrEmpty(suffixText))
                    transformed = transformed + suffixText;

                if (addSequenceNumber)
                    transformed = $"{transformed}_{seq:D3}";

                string newFileName = transformed + ext;
                string newFullPath = Path.Combine(dir, newFileName);
                bool changed = !string.Equals(origFileName, newFileName, StringComparison.Ordinal);

                list.Add(new BatchRenamePreviewItem
                {
                    OriginalName = origFileName,
                    NewName = newFileName,
                    Extension = string.IsNullOrEmpty(ext) ? "-" : ext,
                    OriginalFullPath = file,
                    NewFullPath = newFullPath,
                    ChangeStatus = changed ? "✨ Yeniden Adlandırılacak" : "Değişiklik Yok",
                    IsSelected = changed
                });

                seq++;
            }
        }
        catch { }

        return list;
    }

    public static (int RenamedCount, string Message) ApplyBatchRename(IEnumerable<BatchRenamePreviewItem> items)
    {
        int count = 0;
        LastRenameUndoLog.Clear();

        foreach (var item in items.Where(x => x.IsSelected))
        {
            try
            {
                if (string.Equals(item.OriginalFullPath, item.NewFullPath, StringComparison.Ordinal))
                    continue;

                if (!File.Exists(item.OriginalFullPath) || File.Exists(item.NewFullPath))
                    continue;

                File.Move(item.OriginalFullPath, item.NewFullPath);
                LastRenameUndoLog.Add((item.NewFullPath, item.OriginalFullPath));
                count++;
            }
            catch { }
        }

        return (count, count > 0
            ? $"{count} dosya başarıyla yeniden adlandırıldı. ('Geri Al' ile eski adlarına döndürebilirsiniz)."
            : "Yeniden adlandırılacak uygun dosya bulunamadı.");
    }

    public static (int RestoredCount, string Message) UndoLastBatchRename()
    {
        if (LastRenameUndoLog.Count == 0)
            return (0, "Geri alınacak son adlandırma işlemi bulunamadı.");

        int restored = 0;
        for (int i = LastRenameUndoLog.Count - 1; i >= 0; i--)
        {
            var (newPath, oldPath) = LastRenameUndoLog[i];
            try
            {
                if (File.Exists(newPath) && !File.Exists(oldPath))
                {
                    File.Move(newPath, oldPath);
                    restored++;
                }
            }
            catch { }
        }

        LastRenameUndoLog.Clear();
        return (restored, $"{restored} dosya orijinal adına geri döndürüldü.");
    }

    // ==================== 3. NETWORK ADAPTER, IP/MAC & TCP/IP DOCTOR ====================

    public static async Task<List<NetworkAdapterDetailItem>> GetNetworkAdaptersAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<NetworkAdapterDetailItem>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = nic.GetIPProperties();
                    string ipv4 = ipProps.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString())
                        .FirstOrDefault() ?? "-";

                    string gw = ipProps.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.ToString())
                        .FirstOrDefault() ?? "-";

                    var dnsList = ipProps.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();

                    string dnsStr = dnsList.Count > 0 ? string.Join(", ", dnsList) : "Otomatik (DHCP)";

                    byte[] macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                    string mac = macBytes.Length > 0
                        ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                        : "-";

                    long mbps = nic.Speed > 0 ? nic.Speed / 1_000_000 : 0;
                    string speedStr = mbps > 0 && mbps < 100_000 ? $"{mbps} Mbps" : "Sanal / Dinamik";

                    string statusBadge = nic.OperationalStatus == OperationalStatus.Up
                        ? "🟢 Bağlı (Aktif)"
                        : "⚪ Bağlı Değil";

                    list.Add(new NetworkAdapterDetailItem
                    {
                        AdapterName = nic.Name,
                        Description = nic.Description,
                        StatusBadge = statusBadge,
                        MacAddress = mac,
                        IpAndGateway = $"IP: {ipv4} | Ağ Geçidi: {gw}",
                        DnsAndSpeed = $"Hız: {speedStr} | DNS: {dnsStr}"
                    });
                }
            }
            catch { }

            return list
                .OrderByDescending(x => x.StatusBadge.StartsWith("🟢"))
                .ThenBy(x => x.AdapterName)
                .ToList();
        });
    }

    public static async Task<(bool Success, string Message)> RunNetworkRepairActionAsync(string actionKey)
    {
        return await Task.Run(() =>
        {
            try
            {
                string cmd = actionKey switch
                {
                    "ARP_FLUSH" => "ipconfig /flushdns & arp -d *",
                    "TCP_WINSOCK_RESET" => "netsh winsock reset & netsh int ip reset",
                    "DHCP_RENEW" => "ipconfig /release & ipconfig /renew",
                    _ => "ipconfig /flushdns"
                };

                bool needsAdmin = actionKey is "TCP_WINSOCK_RESET" or "ARP_FLUSH";
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{cmd}\"",
                    UseShellExecute = needsAdmin,
                    Verb = needsAdmin ? "runas" : "",
                    CreateNoWindow = !needsAdmin,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);

                return actionKey switch
                {
                    "ARP_FLUSH" => (true, "DNS ve ARP Yönlendirme Önbelleği (arp -d * & flushdns) başarıyla sıfırlandı!"),
                    "TCP_WINSOCK_RESET" => (true, "Winsock Kataloğu ve TCP/IP Yığını sıfırlandı! (Tam etki için yeniden başlatma önerilir)."),
                    "DHCP_RENEW" => (true, "DHCP IP Adresi serbest bırakıldı ve yeniden alındı (ipconfig /release & /renew)."),
                    _ => (true, "Ağ komutu uygulandı.")
                };
            }
            catch (Exception ex)
            {
                return (false, $"Ağ onarım komutu tamamlanamadı: {ex.Message}");
            }
        });
    }

    // ==================== 4. SYSTEM & USER FONT MANAGER ====================

    public static async Task<List<SystemFontItem>> ScanInstalledFontsAsync(string? searchFilter = null)
    {
        return await Task.Run(() =>
        {
            var results = new List<SystemFontItem>();
            var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };

            string sysFontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string userFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");

            void ScanDir(string dir, bool isUserFont, string categoryLabel)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    return;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        string ext = Path.GetExtension(file);
                        if (!allowedExts.Contains(ext)) continue;

                        var fi = new FileInfo(file);
                        string baseName = Path.GetFileNameWithoutExtension(fi.Name);
                        string familyGuess = Regex.Replace(baseName, @"(Regular|Bold|Italic|Light|Medium|SemiBold|Black|Thin|-|_)+$", "", RegexOptions.IgnoreCase).Trim();
                        if (string.IsNullOrWhiteSpace(familyGuess))
                            familyGuess = baseName;

                        if (!string.IsNullOrWhiteSpace(searchFilter))
                        {
                            string f = searchFilter.Trim();
                            if (!fi.Name.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                                !familyGuess.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                                !categoryLabel.Contains(f, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        results.Add(new SystemFontItem
                        {
                            FontFileName = fi.Name,
                            FontFamilyGuess = familyGuess,
                            SourceCategory = categoryLabel,
                            FullFilePath = fi.FullName,
                            SizeBytes = fi.Length,
                            IsUserFont = isUserFont,
                            IsSelected = false
                        });
                    }
                }
                catch { }
            }

            ScanDir(userFontsDir, isUserFont: true, "👤 Kullanıcı Fontu (Silinebilir)");
            ScanDir(sysFontsDir, isUserFont: false, "🖥️ Windows Sistem Fontu");

            return results
                .OrderByDescending(x => x.IsUserFont)
                .ThenByDescending(x => x.SizeBytes)
                .Take(450)
                .ToList();
        });
    }

    public static (int DeletedCount, long FreedBytes, string Message) RemoveSelectedUserFonts(IEnumerable<SystemFontItem> items)
    {
        int deleted = 0;
        long freed = 0;

        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "FreeCleaner_FontBackup");

        foreach (var font in items.Where(x => x.IsSelected))
        {
            if (!font.IsUserFont)
                continue; // Protect core C:\Windows\Fonts system fonts from accidental deletion

            try
            {
                if (File.Exists(font.FullFilePath))
                {
                    Directory.CreateDirectory(backupDir);
                    string backupFile = Path.Combine(backupDir, font.FontFileName);
                    File.Copy(font.FullFilePath, backupFile, overwrite: true);

                    long size = font.SizeBytes;
                    File.Delete(font.FullFilePath);
                    deleted++;
                    freed += size;
                }
            }
            catch { }
        }

        string msg = deleted > 0
            ? $"{deleted} kullanıcı fontu Masaüstü/FreeCleaner_FontBackup klasörüne yedeklendi ve kaldırıldı ({CleanerRule.FormatBytes(freed)} açıldı)."
            : "Yalnızca 'Kullanıcı Fontu' kategorisindeki seçili fontlar güvenli şekilde silinebilir.";

        return (deleted, freed, msg);
    }

    // ==================== 5. SMART CLIPBOARD SHIELD & URL TRACKING STRIPPER ====================

    private static readonly string[] TrackingQueryParams =
    [
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "fbclid", "gclid", "msclkid", "mc_cid", "mc_eid", "igshid", "si", "_hsenc", "_hsmi"
    ];

    public static ClipboardAuditEntryItem InspectAndSanitizeText(string? rawClipboardText)
    {
        string now = DateTime.Now.ToString("HH:mm:ss");
        if (string.IsNullOrWhiteSpace(rawClipboardText))
        {
            return new ClipboardAuditEntryItem
            {
                CapturedAt = now,
                CategoryBadge = "⚪ Pano Boş",
                OriginalPreview = "(Panoda metin verisi bulunmuyor)",
                SanitizedContent = string.Empty,
                SecurityAdvice = "Panoda herhangi bir hassas veri veya izleme bağlantısı tespit edilmedi."
            };
        }

        string trimmed = rawClipboardText.Trim();
        string preview = trimmed.Replace("\r\n", " ").Replace("\n", " ");
        if (preview.Length > 110)
            preview = preview[..110] + "...";

        // 1. Check for URL with tracking parameters
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            string cleanedUrl = StripUrlTrackingParameters(uri, out int strippedCount);
            if (strippedCount > 0)
            {
                return new ClipboardAuditEntryItem
                {
                    CapturedAt = now,
                    CategoryBadge = $"🔗 İzleme URL'si ({strippedCount} Takip Parametresi)",
                    OriginalPreview = preview,
                    SanitizedContent = cleanedUrl,
                    SecurityAdvice = "Re-marketing ve reklam takip parametreleri (utm_, fbclid, gclid, si) temizlendi."
                };
            }

            return new ClipboardAuditEntryItem
            {
                CapturedAt = now,
                CategoryBadge = "✅ Temiz Web Bağlantısı (URL)",
                OriginalPreview = preview,
                SanitizedContent = trimmed,
                SecurityAdvice = "Bağlantıda bilinen reklam takip parametresi bulunmadı."
            };
        }

        // 2. Check for API Keys / Secrets / Tokens
        if (Regex.IsMatch(trimmed, @"(sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{20,}|AIza[0-9A-Za-z\-_]{30,}|eyJ[A-Za-z0-9_\-]{20,})"))
        {
            string masked = trimmed.Length > 10
                ? $"{trimmed[..6]}••••••••••••{trimmed[^4..]}"
                : "••••••••";
            return new ClipboardAuditEntryItem
            {
                CapturedAt = now,
                CategoryBadge = "🚨 Hassas API Anahtarı / Token!",
                OriginalPreview = masked,
                SanitizedContent = string.Empty,
                SecurityAdvice = "Panoda açık API Anahtarı / JWT Token tespit edildi! Kullandıktan hemen sonra panoyu temizleyin."
            };
        }

        // 3. Check for Turkish IBAN or Credit Card pattern
        if (Regex.IsMatch(trimmed.Replace(" ", ""), @"TR\d{24}", RegexOptions.IgnoreCase))
        {
            return new ClipboardAuditEntryItem
            {
                CapturedAt = now,
                CategoryBadge = "💳 Finansal Veri (IBAN)",
                OriginalPreview = preview,
                SanitizedContent = trimmed.Replace(" ", "").ToUpperInvariant(),
                SecurityAdvice = "IBAN boşlukları temizlendi; işlem sonrası panoyu sıfırlamanız önerilir."
            };
        }

        return new ClipboardAuditEntryItem
        {
            CapturedAt = now,
            CategoryBadge = "📝 Standart Metin",
            OriginalPreview = preview,
            SanitizedContent = trimmed,
            SecurityAdvice = $"Uzunluk: {trimmed.Length} karakter ({trimmed.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length} kelime)."
        };
    }

    private static string StripUrlTrackingParameters(Uri uri, out int removedCount)
    {
        removedCount = 0;
        if (string.IsNullOrWhiteSpace(uri.Query))
            return uri.ToString();

        string rawQuery = uri.Query.TrimStart('?');
        var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();

        foreach (var part in parts)
        {
            string key = part.Split('=')[0];
            if (TrackingQueryParams.Any(t => key.Equals(t, StringComparison.OrdinalIgnoreCase)) ||
                key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            {
                removedCount++;
            }
            else
            {
                kept.Add(part);
            }
        }

        var builder = new UriBuilder(uri)
        {
            Query = kept.Count > 0 ? string.Join("&", kept) : string.Empty
        };
        return builder.Uri.ToString();
    }
}
