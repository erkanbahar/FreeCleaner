using System.IO;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class DevAndGamerCleanerService
{
    private static readonly HashSet<string> TargetArtifactDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        "bin",
        "obj",
        "Debug",
        "Release",
        "Retail",
        "x64",
        "x86",
        "ipch",
        ".vs",
        "__pycache__",
        ".venv",
        "target",
        ".gradle",
        ".next",
        "dist"
    };

    private static readonly string[] VsProjectExtensions =
    [
        "*.sln", "*.slnx", "*.csproj", "*.vcxproj", "*.vbproj", "*.fsproj", "*.esproj", "*.wapproj"
    ];

    public static async Task<List<DevArtifactFolderItem>> ScanVisualStudioDebugAndRetailAsync(
        string workspaceRootPath,
        bool includeDebug = true,
        bool includeRetailAndRelease = true,
        bool includeObjAndVs = true,
        IProgress<(string CurrentDir, int FoundCount)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DevArtifactFolderItem>();
            if (string.IsNullOrWhiteSpace(workspaceRootPath) || !Directory.Exists(workspaceRootPath))
                return results;

            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<(string Path, bool InsideVsProject, string NearestProjectName)>();
            pending.Push((workspaceRootPath, false, Path.GetFileName(workspaceRootPath)));
            int scannedDirs = 0;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (currentDir, insideVsContext, nearestProject) = pending.Pop();

                scannedDirs++;
                if (scannedDirs % 20 == 0)
                {
                    progress?.Report((currentDir, results.Count));
                }

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    // Check if this directory contains any Visual Studio Solution or Project file
                    bool hasVsProjectFile = false;
                    string projectName = nearestProject;
                    try
                    {
                        foreach (string pattern in VsProjectExtensions)
                        {
                            var matchingFiles = dirInfo.GetFiles(pattern, SearchOption.TopDirectoryOnly);
                            if (matchingFiles.Length > 0)
                            {
                                hasVsProjectFile = true;
                                projectName = Path.GetFileNameWithoutExtension(matchingFiles[0].Name);
                                break;
                            }
                        }
                    }
                    catch { }

                    bool isVsContext = insideVsContext || hasVsProjectFile;

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (subDir.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                            subDir.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Protect currently running FreeCleaner build output
                        if (subDir.FullName.Contains("FreeCleaner", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string name = subDir.Name;

                        // Check if this folder is Debug / Retail / Release / bin\Debug / obj\Debug etc.
                        bool isDebugFolder = name.Equals("Debug", StringComparison.OrdinalIgnoreCase);
                        bool isRetailOrReleaseFolder = name.Equals("Retail", StringComparison.OrdinalIgnoreCase) ||
                                                       name.Equals("Release", StringComparison.OrdinalIgnoreCase);
                        bool isBinOrObj = name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                          name.Equals("obj", StringComparison.OrdinalIgnoreCase);
                        bool isVsHidden = name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                                          name.Equals("ipch", StringComparison.OrdinalIgnoreCase) ||
                                          name.Equals("TestResults", StringComparison.OrdinalIgnoreCase);

                        if (isVsContext && isBinOrObj)
                        {
                            // Look inside bin/ and obj/ specifically for Debug, Retail, Release subfolders
                            bool addedSubConfigs = false;
                            try
                            {
                                foreach (var cfgDir in subDir.GetDirectories())
                                {
                                    bool cfgIsDebug = cfgDir.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase);
                                    bool cfgIsRetail = cfgDir.Name.Equals("Retail", StringComparison.OrdinalIgnoreCase) ||
                                                       cfgDir.Name.Equals("Release", StringComparison.OrdinalIgnoreCase);

                                    if ((cfgIsDebug && includeDebug) || (cfgIsRetail && includeRetailOrReleaseFolder(cfgDir.Name, includeRetailAndRelease)))
                                    {
                                        long cfgSize = CalculateDirectorySizeFast(cfgDir, cancellationToken);
                                        if (cfgSize > 0 && visitedPaths.Add(cfgDir.FullName))
                                        {
                                            results.Add(new DevArtifactFolderItem
                                            {
                                                ProjectName = projectName,
                                                ArtifactType = $"VS {subDir.Name}\\{cfgDir.Name}",
                                                FullPath = cfgDir.FullName,
                                                SizeBytes = cfgSize,
                                                LastModified = cfgDir.LastWriteTime,
                                                IsSelected = true
                                            });
                                            addedSubConfigs = true;
                                        }
                                    }
                                }
                            }
                            catch { }

                            if (!addedSubConfigs && includeObjAndVs)
                            {
                                long binObjSize = CalculateDirectorySizeFast(subDir, cancellationToken);
                                if (binObjSize > 0 && visitedPaths.Add(subDir.FullName))
                                {
                                    results.Add(new DevArtifactFolderItem
                                    {
                                        ProjectName = projectName,
                                        ArtifactType = $"VS {subDir.Name}",
                                        FullPath = subDir.FullName,
                                        SizeBytes = binObjSize,
                                        LastModified = subDir.LastWriteTime,
                                        IsSelected = true
                                    });
                                }
                            }
                            continue;
                        }

                        if ((isDebugFolder && includeDebug) ||
                            (isRetailOrReleaseFolder && includeRetailAndRelease) ||
                            (isVsHidden && includeObjAndVs))
                        {
                            long size = CalculateDirectorySizeFast(subDir, cancellationToken);
                            if (size > 0 && visitedPaths.Add(subDir.FullName))
                            {
                                results.Add(new DevArtifactFolderItem
                                {
                                    ProjectName = projectName,
                                    ArtifactType = $"VS {subDir.Name}",
                                    FullPath = subDir.FullName,
                                    SizeBytes = size,
                                    LastModified = subDir.LastWriteTime,
                                    IsSelected = true
                                });
                            }
                            continue;
                        }

                        pending.Push((subDir.FullName, isVsContext, projectName));
                    }
                }
                catch
                {
                    // Ignore inaccessible directories
                }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);

        static bool includeRetailOrReleaseFolder(string folderName, bool enabled) => enabled;
    }

    public static async Task<List<DevArtifactFolderItem>> ScanWorkspaceArtifactsAsync(
        string workspaceRootPath,
        IProgress<(string CurrentDir, int FoundCount)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DevArtifactFolderItem>();
            if (string.IsNullOrWhiteSpace(workspaceRootPath) || !Directory.Exists(workspaceRootPath))
                return results;

            var pending = new Stack<string>();
            pending.Push(workspaceRootPath);
            int scannedDirs = 0;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentDir = pending.Pop();

                scannedDirs++;
                if (scannedDirs % 20 == 0)
                {
                    progress?.Report((currentDir, results.Count));
                }

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (subDir.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (TargetArtifactDirNames.Contains(subDir.Name))
                        {
                            // Avoid deleting FreeCleaner's own active bin/obj while running
                            if (subDir.FullName.Contains("FreeCleaner", StringComparison.OrdinalIgnoreCase))
                                continue;

                            long size = CalculateDirectorySizeFast(subDir, cancellationToken);
                            if (size > 0)
                            {
                                results.Add(new DevArtifactFolderItem
                                {
                                    ProjectName = dirInfo.Name,
                                    ArtifactType = subDir.Name,
                                    FullPath = subDir.FullName,
                                    SizeBytes = size,
                                    LastModified = subDir.LastWriteTime,
                                    IsSelected = true
                                });
                            }
                            // Do not recurse inside node_modules or bin/obj
                            continue;
                        }

                        pending.Push(subDir.FullName);
                    }
                }
                catch
                {
                    // Ignore access errors
                }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);
    }

    public static async Task<List<DevArtifactFolderItem>> ScanGlobalDevAndGamerCachesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DevArtifactFolderItem>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var candidates = new (string Category, string Label, string Path)[]
            {
                ("Gamer / GPU", "NVIDIA DXCache (DirectX Shader)", Path.Combine(localAppData, "NVIDIA", "DXCache")),
                ("Gamer / GPU", "NVIDIA GLCache (OpenGL Shader)", Path.Combine(localAppData, "NVIDIA", "GLCache")),
                ("Gamer / GPU", "AMD DxCache", Path.Combine(localAppData, "AMD", "DxCache")),
                ("Gamer / Steam", "Steam HTTP Önbelleği", @"C:\Program Files (x86)\Steam\appcache\httpcache"),
                ("Gamer / Steam", "Steam Depot İndirme Önbelleği", @"C:\Program Files (x86)\Steam\depotcache"),
                ("Gamer / Epic", "Epic Games Launcher WebCache", Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache_4430")),
                ("Python / Pip", "Python Pip İndirme Önbelleği", Path.Combine(localAppData, "pip", "Cache")),
                ("Node / Yarn", "Yarn Global Önbelleği", Path.Combine(localAppData, "Yarn", "Cache")),
                ("Node / pnpm", "pnpm Store Önbelleği", Path.Combine(localAppData, "pnpm-store")),
                ("Java / Gradle", "Gradle Build Caches", Path.Combine(userProfile, ".gradle", "caches")),
                ("Rust / Cargo", "Cargo Registry Cache", Path.Combine(userProfile, ".cargo", "registry", "cache")),
                (".NET / NuGet", "NuGet HTTP v3-cache", Path.Combine(localAppData, "NuGet", "v3-cache"))
            };

            foreach (var (category, label, path) in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(path)) continue;

                try
                {
                    var d = new DirectoryInfo(path);
                    long size = CalculateDirectorySizeFast(d, cancellationToken);
                    if (size > 0)
                    {
                        results.Add(new DevArtifactFolderItem
                        {
                            ProjectName = label,
                            ArtifactType = category,
                            FullPath = path,
                            SizeBytes = size,
                            LastModified = d.LastWriteTime,
                            IsSelected = true
                        });
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        }, cancellationToken);
    }

    private static long CalculateDirectorySizeFast(DirectoryInfo root, CancellationToken ct)
    {
        long total = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var f in current.GetFiles())
                    total += f.Length;

                foreach (var sub in current.GetDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                        stack.Push(sub);
                }
            }
            catch
            {
                // Ignore
            }
        }
        return total;
    }
}
