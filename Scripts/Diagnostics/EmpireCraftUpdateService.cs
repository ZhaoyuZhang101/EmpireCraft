using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.HelperFunc;
using Newtonsoft.Json.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Diagnostics;

public enum EmpireCraftUpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    InstallScheduled,
    InstallPending,
    Installing,
    InstallSucceeded,
    InstallFailed,
    ManualPackageReady,
    ManualInstallRequired,
    Failed
}

public sealed class EmpireCraftUpdateSnapshot
{
    public long Revision;
    public EmpireCraftUpdateStatus Status;
    public string CurrentVersion;
    public string AvailableVersion;
    public string ReleaseNotes;
    public string LocalPackagePath;
    public int ProgressPercent;
    public string Error;
}

public static class EmpireCraftUpdateService
{
    private static readonly string[] ManifestEndpoints =
    {
        "https://update.cmhgamedev.dpdns.org/releases/latest.json",
        "https://empirecraft-update.zhangzhaoyu101.workers.dev/releases/latest.json"
    };

    private static readonly HashSet<string> TrustedDownloadHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "update.cmhgamedev.dpdns.org",
            "empirecraft-update.zhangzhaoyu101.workers.dev"
        };

    private const int NetworkTimeoutMilliseconds = 20000;
    private const long MaximumPackageBytes = 128L * 1024L * 1024L;
    private const long MaximumExtractedBytes = 512L * 1024L * 1024L;
    private const int MaximumArchiveEntries = 20000;
    private const string PendingInstallFileName = "pending-install.json";
    private const string InstallResultFileName = "install-result.json";
    private const string UpdateSettingsFileName = "update-settings.json";
    private static readonly object Sync = new();
    private static bool _initialized;
    private static bool _busy;
    private static bool _manualPackageMode;
    private static bool _archiveCurrentVersion;
    private static UpdateManifest _manifest;
    private static EmpireCraftUpdateSnapshot _snapshot = new()
    {
        Status = EmpireCraftUpdateStatus.Idle,
        CurrentVersion = "unknown"
    };

    private sealed class UpdateManifest
    {
        public string Version;
        public string DownloadUrl;
        public string Sha256;
        public long Size;
        public string ReleaseNotes;
    }

    public static void Initialize()
    {
        // 安卓等平台不自动换文件(没验证过)，转手动安装；在主线程取平台
        UnityEngine.RuntimePlatform platform = UnityEngine.Application.platform;
        _autoInstallSupported = platform is UnityEngine.RuntimePlatform.WindowsPlayer or UnityEngine.RuntimePlatform.WindowsEditor or
            UnityEngine.RuntimePlatform.OSXPlayer or UnityEngine.RuntimePlatform.OSXEditor or UnityEngine.RuntimePlatform.LinuxPlayer or
            UnityEngine.RuntimePlatform.LinuxEditor;
        EnableTls12();
        (bool manualPackageMode, bool archiveCurrentVersion) = ReadUpdateSettings();
        lock (Sync)
        {
            if (_initialized) return;
            _initialized = true;
            _manualPackageMode = manualPackageMode;
            _archiveCurrentVersion = archiveCurrentVersion;
            _snapshot.CurrentVersion = ReadInstalledVersion();
        }

        if (!RestoreInstallState()) CheckForUpdates();
    }

    // 有新版本可更新(用于更新按钮上的小红点)
    public static bool HasUpdateAvailable
    {
        get
        {
            lock (Sync) return _snapshot.Status == EmpireCraftUpdateStatus.UpdateAvailable;
        }
    }

    public static EmpireCraftUpdateSnapshot GetSnapshot()
    {
        lock (Sync)
        {
            return new EmpireCraftUpdateSnapshot
            {
                Revision = _snapshot.Revision,
                Status = _snapshot.Status,
                CurrentVersion = _snapshot.CurrentVersion,
                AvailableVersion = _snapshot.AvailableVersion,
                ReleaseNotes = _snapshot.ReleaseNotes,
                LocalPackagePath = _snapshot.LocalPackagePath,
                ProgressPercent = _snapshot.ProgressPercent,
                Error = _snapshot.Error
            };
        }
    }

    public static bool ManualPackageMode
    {
        get
        {
            lock (Sync) return _manualPackageMode;
        }
    }

    public static void SetManualPackageMode(bool enabled)
    {
        lock (Sync)
        {
            if (_manualPackageMode == enabled) return;
            _manualPackageMode = enabled;
            _snapshot.Revision++;
        }

        SaveUpdateSettings();
    }

    public static bool ArchiveCurrentVersion
    {
        get
        {
            lock (Sync) return _archiveCurrentVersion;
        }
    }

    public static void SetArchiveCurrentVersion(bool enabled)
    {
        lock (Sync)
        {
            if (_archiveCurrentVersion == enabled) return;
            _archiveCurrentVersion = enabled;
            _snapshot.Revision++;
        }

        SaveUpdateSettings();
    }

    public static void CheckForUpdates()
    {
        if (!TryBegin(EmpireCraftUpdateStatus.Checking)) return;
        ThreadPool.QueueUserWorkItem(_ => CheckWorker());
    }

    public static void DownloadAndInstall()
    {
        UpdateManifest manifest;
        bool manualPackageMode;
        bool archiveCurrentVersion;
        lock (Sync)
        {
            if (_busy || _manifest == null || _snapshot.Status != EmpireCraftUpdateStatus.UpdateAvailable)
                return;

            _busy = true;
            manifest = _manifest;
            manualPackageMode = _manualPackageMode;
            archiveCurrentVersion = _archiveCurrentVersion;
            PublishLocked(EmpireCraftUpdateStatus.Downloading, progress: 0);
        }

        ThreadPool.QueueUserWorkItem(_ => DownloadWorker(manifest, manualPackageMode, archiveCurrentVersion));
    }

    public static bool OpenModsFolder()
    {
        try
        {
            DirectoryInfo modsDirectory = GetModsDirectory();
            if (modsDirectory == null || !modsDirectory.Exists)
                return false;

            Process.Start(new ProcessStartInfo(modsDirectory.FullName)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not open the Mods directory: " + error.Message);
            return false;
        }
    }

    public static bool OpenArchivedVersionsFolder()
    {
        try
        {
            string path = GetArchivedVersionsRoot();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not open the archive directory: " + error.Message);
            return false;
        }
    }

    // 打开 TLS 1.2。手机版(安卓)上 ServicePointManager 的静态构造会抛异常(2026-10-22 手机版读模组时报
    // "The type initializer for 'System.Net.ServicePointManager' threw an exception"，整个模组加载失败)：设不上就用默认协议
    internal static void EnableTls12()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch (Exception)
        {
            // 设不上照常走默认协议；真连不上由检查更新、上传报错各自报失败
        }
    }

    internal static int CompareVersions(string left, string right)
    {
        return EmpireCraftVersionRules.Compare(left, right);
    }

    private static void CheckWorker()
    {
        var failures = new List<string>();
        UpdateManifest newestManifest = null;

        foreach (string endpoint in ManifestEndpoints)
        {
            try
            {
                UpdateManifest manifest = ParseManifest(DownloadText(endpoint));
                if (newestManifest == null || CompareVersions(manifest.Version, newestManifest.Version) > 0)
                    newestManifest = manifest;
            }
            catch (Exception error)
            {
                failures.Add(endpoint + " -> " + error.Message);
            }
        }

        if (newestManifest == null)
        {
            Fail(string.Join("\n", failures));
            return;
        }

        string currentVersion = ReadInstalledVersion();
        bool available = CompareVersions(newestManifest.Version, currentVersion) > 0;
        lock (Sync)
        {
            _manifest = available ? newestManifest : null;
            _busy = false;
            _snapshot.CurrentVersion = currentVersion;
            _snapshot.AvailableVersion = newestManifest.Version;
            _snapshot.ReleaseNotes = newestManifest.ReleaseNotes;
            _snapshot.Error = null;
            PublishLocked(available
                ? EmpireCraftUpdateStatus.UpdateAvailable
                : EmpireCraftUpdateStatus.UpToDate);
        }

        if (available)
            LogService.LogInfo("EmpireCraft update available: " + newestManifest.Version);
    }

    private static void DownloadWorker(UpdateManifest manifest, bool manualPackageMode,
        bool archiveCurrentVersion)
    {
        try
        {
            Uri downloadUri = ValidateDownloadUri(manifest.DownloadUrl);
            string updateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EmpireCraft",
                "Updates",
                SanitizeFileName(manifest.Version));
            Directory.CreateDirectory(updateRoot);

            string packagePath = Path.Combine(updateRoot, "EmpireCraft.zip");
            DownloadPackage(downloadUri, packagePath, manifest.Size);
            VerifySha256(packagePath, manifest.Sha256);

            string stagePath = Path.Combine(updateRoot, "staged");
            RecreateDirectory(stagePath);
            ExtractArchiveSafely(packagePath, stagePath);

            string packageRoot = ResolvePackageRoot(stagePath);
            ValidateStagedPackage(packageRoot, manifest.Version);

            string modFolder = Path.GetFullPath(ModClass._declare.FolderPath);
            if (manualPackageMode)
            {
                string manualPackagePath = CopyPackageToMods(packagePath, modFolder, manifest);
                Complete(EmpireCraftUpdateStatus.ManualPackageReady, manualPackagePath, null);
                return;
            }

            string gitMarker = Path.Combine(modFolder, ".git");
            bool developerCheckout = Directory.Exists(gitMarker) || File.Exists(gitMarker);

            if (developerCheckout)
            {
                Complete(EmpireCraftUpdateStatus.ManualInstallRequired, packagePath,
                    "Developer checkout detected; automatic replacement is disabled.");
                return;
            }

            // 拼好候选，退出游戏时对调(见 OnQuitting)
            if (!PrepareInstall(packageRoot, modFolder, updateRoot, manifest.Version, packagePath, archiveCurrentVersion,
                    out string reason))
                Complete(EmpireCraftUpdateStatus.ManualInstallRequired, packagePath, reason);
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    private static bool TryBegin(EmpireCraftUpdateStatus status)
    {
        lock (Sync)
        {
            if (_busy || _snapshot.Status is EmpireCraftUpdateStatus.InstallScheduled or
                EmpireCraftUpdateStatus.InstallPending) return false;
            _busy = true;
            _snapshot.Error = null;
            _snapshot.ProgressPercent = 0;
            PublishLocked(status);
            return true;
        }
    }

    private static void Complete(EmpireCraftUpdateStatus status, string packagePath, string detail)
    {
        lock (Sync)
        {
            _busy = false;
            _snapshot.LocalPackagePath = packagePath;
            _snapshot.Error = detail;
            _snapshot.ProgressPercent = 100;
            PublishLocked(status);
        }
    }

    private static void Fail(string error)
    {
        lock (Sync)
        {
            _busy = false;
            _snapshot.Error = error;
            PublishLocked(EmpireCraftUpdateStatus.Failed);
        }
        LogService.LogWarning("EmpireCraft update failed: " + error);
    }

    private static void PublishLocked(EmpireCraftUpdateStatus status, int? progress = null)
    {
        _snapshot.Status = status;
        if (progress.HasValue) _snapshot.ProgressPercent = progress.Value;
        _snapshot.Revision++;
    }

    private static string DownloadText(string url)
    {
        var builder = new UriBuilder(url);
        string cacheBust = "ec_check=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? cacheBust
            : builder.Query.TrimStart('?') + "&" + cacheBust;
        Uri uri = builder.Uri;
        if (uri.Scheme != Uri.UriSchemeHttps) throw new Exception("Update manifest must use HTTPS.");

        HttpWebRequest request = CreateRequest(uri);
        using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception("HTTP " + (int)response.StatusCode);

        using Stream stream = response.GetResponseStream();
        using var memory = new MemoryStream();
        CopyLimited(stream, memory, 1024L * 1024L);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static UpdateManifest ParseManifest(string json)
    {
        JObject root = JObject.Parse(json);
        var manifest = new UpdateManifest
        {
            Version = root.Value<string>("version")?.Trim(),
            DownloadUrl = root.Value<string>("downloadUrl")?.Trim(),
            Sha256 = root.Value<string>("sha256")?.Trim(),
            Size = root.Value<long?>("size") ?? 0L,
            ReleaseNotes = root.Value<string>("releaseNotes")?.Trim()
        };

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.DownloadUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            manifest.Sha256.Length != 64 ||
            manifest.Size <= 0 || manifest.Size > MaximumPackageBytes)
            throw new Exception("Update manifest is incomplete or invalid.");

        ValidateDownloadUri(manifest.DownloadUrl);
        return manifest;
    }

    private static Uri ValidateDownloadUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !TrustedDownloadHosts.Contains(uri.Host))
            throw new Exception("Update download host is not trusted.");
        return uri;
    }

    private static HttpWebRequest CreateRequest(Uri uri)
    {
#pragma warning disable SYSLIB0014
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Method = "GET";
        request.UserAgent = "EmpireCraft-Updater/1.0";
        request.Timeout = NetworkTimeoutMilliseconds;
        request.ReadWriteTimeout = NetworkTimeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.KeepAlive = false;
        request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);
        return request;
    }

    private static void DownloadPackage(Uri uri, string path, long expectedSize)
    {
        HttpWebRequest request = CreateRequest(uri);
        using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception("Package download returned HTTP " + (int)response.StatusCode + ".");
        if (response.ContentLength > MaximumPackageBytes)
            throw new Exception("Update package exceeds the safety size limit.");

        using Stream input = response.GetResponseStream();
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes)
                throw new Exception("Update package exceeds the safety size limit.");
            output.Write(buffer, 0, read);

            long denominator = expectedSize > 0 ? expectedSize : response.ContentLength;
            int progress = denominator > 0
                ? Math.Min(99, (int)(total * 100L / denominator))
                : 0;
            lock (Sync) PublishLocked(EmpireCraftUpdateStatus.Downloading, progress);
        }

        if (expectedSize > 0 && total != expectedSize)
            throw new Exception("Downloaded package size does not match the manifest.");
    }

    private static void VerifySha256(string path, string expectedHash)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Update package SHA-256 verification failed.");
    }

    private static string CopyPackageToMods(string packagePath, string modFolder, UpdateManifest manifest)
    {
        DirectoryInfo modsDirectory = GetModsDirectory(modFolder);
        if (modsDirectory == null || !modsDirectory.Exists)
            throw new DirectoryNotFoundException("The WorldBox Mods directory could not be located.");

        string fileName = "EmpireCraft_" + SanitizeFileName(manifest.Version) + ".zip";
        string destination = Path.Combine(modsDirectory.FullName, fileName);
        string temporaryDestination = destination + ".download";
        TryDeleteFile(temporaryDestination);
        try
        {
            File.Copy(packagePath, temporaryDestination, true);
            if (manifest.Size > 0 && new FileInfo(temporaryDestination).Length != manifest.Size)
                throw new Exception("The copied update package size does not match the manifest.");
            VerifySha256(temporaryDestination, manifest.Sha256);
            TryDeleteFile(destination);
            File.Move(temporaryDestination, destination);
            return destination;
        }
        catch
        {
            TryDeleteFile(temporaryDestination);
            throw;
        }
    }

    private static DirectoryInfo GetModsDirectory(string modFolder = null)
    {
        string path = modFolder ?? ModClass._declare?.FolderPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return new DirectoryInfo(Path.GetFullPath(path)).Parent;
    }

    private static void ExtractArchiveSafely(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new Exception("Update package contains too many entries.");
        long extracted = 0;
        var buffer = new byte[64 * 1024];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Update package contains an unsafe path.");
            if (entry.Length < 0 || entry.Length > MaximumExtractedBytes - extracted)
                throw new Exception("Update package exceeds the extracted size limit.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            using Stream input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (read > MaximumExtractedBytes - extracted)
                    throw new Exception("Update package exceeds the extracted size limit.");
                output.Write(buffer, 0, read);
                extracted += read;
            }
        }
    }

    private static string ResolvePackageRoot(string stagePath)
    {
        string nested = Path.Combine(stagePath, "EmpireCraft");
        if (File.Exists(Path.Combine(nested, "mod.json"))) return nested;
        if (File.Exists(Path.Combine(stagePath, "mod.json"))) return stagePath;
        throw new Exception("Update package does not contain EmpireCraft/mod.json.");
    }

    private static void ValidateStagedPackage(string packageRoot, string expectedVersion)
    {
        JObject mod = JObject.Parse(File.ReadAllText(Path.Combine(packageRoot, "mod.json")));
        string stagedVersion = mod.Value<string>("version")?.Trim();
        if (!string.Equals(stagedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Update package version does not match its manifest.");
        if (!Directory.Exists(Path.Combine(packageRoot, "Scripts")))
            throw new Exception("Update package is missing the Scripts directory.");
        if (File.Exists(Path.Combine(packageRoot, ".git")) ||
            Directory.Exists(Path.Combine(packageRoot, ".git")))
            throw new Exception("Update package contains a Git checkout marker.");
    }

    // 游戏里换上新版(用户 2026-10-22 转"自动更新程序有问题，更新完重启没生效"，选"游戏里当场替换")：以前等游戏退出后由
    // 隐藏窗口、绕过执行策略的 PowerShell 脚本替换，常被杀毒软件拦下、或随游戏进程一起被结束，文件根本没换上。
    //   · 后台线程拼候选目录：旧目录整个拷一份，新版的各目录整个换掉、新版的顶层文件覆盖。玩家的设置不在模组目录里
    //     (NML 存在 mods_config/<模组>.config，文化物种配对、派系、官职等存在 Mods 目录下)，一概不动；模组自带的
    //     default_config.json 只是设置项与默认值，换成新版的，新加的设置项才出得来(以前留旧的，新开关在玩家那里缺项)；
    //   · 退出游戏那一刻在游戏进程里对调(只是改名，一瞬间；Application.quitting)：先关掉文明乐的曲子流(FMOD 开着曲子文件，
    //     目录改不了名)，整个模组目录改名成备份、候选改名成模组目录；整夹改不了名就按顶层一个个对调，哪一个换不了就把换过的
    //     全部换回去，模组目录始终是整套旧版或整套新版。挪到退出时换(用户 2026-10-22 "小问题都要修")：这一局从头到尾只读旧文件
    //     (以前装完到重启前，用到时才读的科技树、诉求规则、宪法模板等会读到新版的)，下次启动直接是新版；
    //   · 没赶上退出时对调(强行结束、崩溃)：下次启动一开始补上(这时编译好的还是旧版，提示再重启一次)；
    //     以前对调不了就逐个文件同步、备份拷到一半出错时"恢复"会把模组目录删空(审查发现)，现在不再逐个文件改；
    //   · 动手前记下进度(install-state.json)：对调中途被关的，下次启动按记录换回旧版或确认装好；
    //   · 换完写安装结果，下次启动核对加载的版本、按设置存档旧版并清掉备份(见 RestoreInstallResult)；
    //   · 候选、备份放在 Mods 外面的同一层(以点开头，不会被当成模组)；模组目录是链接(junction)的、在安卓上的，转手动安装。
    private const string InstallStateFileName = "install-state.json";
    private const string PendingSwapFileName = "pending-swap.json";
    // 退出时对调：被占用(日志正在写之类)就稍等再试，至多这么多次、每次隔 50 毫秒
    private const int SwapAttempts = 20;

    private sealed class PendingSwap
    {
        public string Target, Candidate, Backup, Version, PackagePath, UpdateRoot, ModsPath, InstallId;
        public bool Archive;
        public int Attempts;
    }

    private static PendingSwap _swap;
    private static bool _autoInstallSupported = true, _quitHooked;

    // 后台线程：拼好候选目录，交给主线程对调；不能自动安装的返回 false
    private static bool PrepareInstall(string source, string target, string updateRoot, string version, string packagePath,
        bool archiveCurrentVersion, out string reason)
    {
        reason = null;
        string targetPath = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!_autoInstallSupported)
        {
            reason = "Automatic installation is not available on this platform.";
            return false;
        }
        if ((File.GetAttributes(targetPath) & FileAttributes.ReparsePoint) != 0)
        {
            reason = "The mod folder is a link; automatic replacement is disabled.";
            return false;
        }
        string modsPath = Path.GetDirectoryName(targetPath) ??
                          throw new InvalidOperationException("The mod folder has no parent directory.");
        string workRoot = Path.GetDirectoryName(modsPath) ??
                          throw new InvalidOperationException("The Mods folder has no parent directory.");
        string installId = Guid.NewGuid().ToString("N");
        string name = Path.GetFileName(targetPath);
        string stateRoot = GetUpdatesRoot();
        Directory.CreateDirectory(stateRoot);
        TryDeleteFile(Path.Combine(stateRoot, InstallResultFileName));
        CleanLeftovers(workRoot, name);
        lock (Sync) PublishLocked(EmpireCraftUpdateStatus.Installing);
        string candidate = Path.Combine(workRoot, "." + name + ".update-" + installId);
        try
        {
            BuildCandidate(source, targetPath, candidate, version);
        }
        catch
        {
            TryDeleteDirectory(candidate);
            throw;
        }
        var swap = new PendingSwap
        {
            Target = targetPath, Candidate = candidate, Backup = Path.Combine(workRoot, "." + name + ".backup-" + installId),
            Version = version, PackagePath = packagePath, UpdateRoot = updateRoot, ModsPath = modsPath, InstallId = installId,
            Archive = archiveCurrentVersion
        };
        // 记在盘上：没赶上退出时对调的，下次启动补上
        WriteJson(Path.Combine(stateRoot, PendingSwapFileName), new JObject
        {
            ["target"] = swap.Target, ["candidate"] = swap.Candidate, ["backup"] = swap.Backup, ["version"] = swap.Version,
            ["packagePath"] = swap.PackagePath, ["updateRoot"] = swap.UpdateRoot, ["modsPath"] = swap.ModsPath,
            ["installId"] = swap.InstallId, ["archive"] = swap.Archive
        });
        lock (Sync) _swap = swap;
        Complete(EmpireCraftUpdateStatus.InstallPending, packagePath, null);
        return true;
    }

    // 退出游戏那一刻(Application.quitting，主线程)：有拼好的候选就对调；没换成的留着，下次启动补上
    private static void OnQuitting()
    {
        PendingSwap swap;
        lock (Sync) swap = _swap;
        if (swap == null) return;
        try
        {
            EmpireCraft.Scripts.GeneralSystems.CivMusic.ReleaseFiles();
        }
        catch
        {
            // FMOD 已在关：曲子流随之关掉
        }
        try
        {
            for (int attempt = 0; attempt < SwapAttempts; attempt++)
            {
                if (SwapIn(swap) == null)
                {
                    WriteSuccess(swap, restartNeeded: false);
                    lock (Sync) _swap = null;
                    return;
                }
                Thread.Sleep(50);
            }
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not install the update on exit: " + error.Message);
        }
    }

    private static void InstallFailedWith(string packagePath, string detail)
    {
        Complete(EmpireCraftUpdateStatus.InstallFailed, packagePath, detail);
        LogService.LogWarning("EmpireCraft update installation failed: " + detail);
    }

    // 对调：整夹改名，不行就按顶层一个个对调；被占用(什么也没改)返回占用的名字，换好了返回 null
    private static string SwapIn(PendingSwap swap)
    {
        string statePath = Path.Combine(GetUpdatesRoot(), InstallStateFileName);
        var state = new JObject
        {
            ["target"] = swap.Target, ["backup"] = swap.Backup, ["candidate"] = swap.Candidate, ["version"] = swap.Version,
            ["packagePath"] = swap.PackagePath, ["phase"] = "whole"
        };
        WriteJson(statePath, state);
        if (TryMoveDirectory(swap.Target, swap.Backup))
        {
            try
            {
                Directory.Move(swap.Candidate, swap.Target);
            }
            catch
            {
                Directory.Move(swap.Backup, swap.Target);
                TryDeleteFile(statePath);
                throw;
            }
            state["phase"] = "swapped";
            WriteJson(statePath, state);
            return null;
        }
        // 按顶层一个个对调：旧的挪进备份、新的挪进模组目录；记下换过的，哪一个换不了就全部换回去
        Directory.CreateDirectory(swap.Backup);
        var moved = new JArray();
        state["phase"] = "parts";
        state["moved"] = moved;
        WriteJson(statePath, state);
        string busy = null;
        try
        {
            foreach (string entry in Directory.GetFileSystemEntries(swap.Candidate))
            {
                string name = Path.GetFileName(entry), live = Path.Combine(swap.Target, name), old = Path.Combine(swap.Backup, name);
                bool hadOld = Exists(live);
                try
                {
                    if (hadOld) MoveEntry(live, old);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    busy = name;
                    break;
                }
                try
                {
                    MoveEntry(entry, live);
                }
                catch
                {
                    if (hadOld) MoveEntry(old, live);
                    throw;
                }
                moved.Add(name);
                WriteJson(statePath, state);
            }
        }
        catch
        {
            // 换到一半出了别的错：换过的全部换回去，模组目录仍是整套旧版
            UndoParts(swap.Target, swap.Backup, swap.Candidate, moved.Values<string>());
            TryDeleteDirectory(swap.Backup);
            TryDeleteFile(statePath);
            throw;
        }
        if (busy != null)
        {
            UndoParts(swap.Target, swap.Backup, swap.Candidate, moved.Values<string>());
            TryDeleteDirectory(swap.Backup);
            TryDeleteFile(statePath);
            return busy;
        }
        state["phase"] = "swapped";
        WriteJson(statePath, state);
        return null;
    }

    // 按顶层对调到一半：换过的倒着换回去(新的挪回候选，旧的从备份挪回模组目录)
    private static void UndoParts(string target, string backup, string candidate, IEnumerable<string> moved)
    {
        foreach (string name in moved.Reverse())
        {
            string live = Path.Combine(target, name), old = Path.Combine(backup, name);
            if (Exists(live))
            {
                if (candidate != null && Directory.Exists(candidate)) MoveEntry(live, Path.Combine(candidate, name));
                else DeleteEntry(live);
            }
            if (Exists(old)) MoveEntry(old, live);
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }

    // 对调好了：写安装结果(下次启动核对版本、按设置存档旧版、清掉备份，见 RestoreInstallResult)，清掉待办记录
    private static void WriteSuccess(PendingSwap swap, bool restartNeeded)
    {
        string stateRoot = GetUpdatesRoot();
        WriteJson(Path.Combine(stateRoot, InstallResultFileName), new JObject
        {
            ["status"] = "success", ["version"] = swap.Version, ["packagePath"] = swap.PackagePath, ["targetPath"] = swap.Target,
            ["backup"] = swap.Backup, ["modsPath"] = swap.ModsPath, ["installId"] = swap.InstallId, ["archive"] = swap.Archive,
            ["restartNeeded"] = restartNeeded, ["detail"] = "",
            ["completedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
        TryDeleteFile(Path.Combine(stateRoot, InstallStateFileName));
        TryDeleteFile(Path.Combine(stateRoot, PendingSwapFileName));
        try
        {
            File.WriteAllText(Path.Combine(swap.UpdateRoot, "install.log"),
                "Installed " + swap.Version + " at " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " into " +
                swap.Target, new UTF8Encoding(false));
        }
        catch
        {
            // 只是记录
        }
    }

    // 换下来的旧版(后台线程)：按设置存成 .ecbackup，再删掉
    private static void DisposeBackup(string backup, string modsPath, string installId, bool archive)
    {
        if (string.IsNullOrEmpty(backup) || !Directory.Exists(backup)) return;
        if (archive && !string.IsNullOrEmpty(modsPath))
        {
            try
            {
                ArchiveBackup(backup, modsPath, installId ?? Guid.NewGuid().ToString("N"));
            }
            catch (Exception error)
            {
                LogService.LogWarning("EmpireCraft could not archive the replaced version: " + error.Message);
            }
        }
        TryDeleteDirectory(backup);
    }

    // 启动时：上次没赶上退出时对调的，现在补上(编译好的还是旧版，提示再重启一次)
    private static bool SwapPendingAtStartup()
    {
        string path = Path.Combine(GetUpdatesRoot(), PendingSwapFileName);
        if (!File.Exists(path)) return false;
        PendingSwap swap = null;
        try
        {
            JObject json = JObject.Parse(File.ReadAllText(path));
            swap = new PendingSwap
            {
                Target = json.Value<string>("target"), Candidate = json.Value<string>("candidate"), Backup = json.Value<string>("backup"),
                Version = json.Value<string>("version"), PackagePath = json.Value<string>("packagePath"),
                UpdateRoot = json.Value<string>("updateRoot"), ModsPath = json.Value<string>("modsPath"),
                InstallId = json.Value<string>("installId"), Archive = json.Value<bool?>("archive") ?? false
            };
            if (!PathsEqual(swap.Target, ModClass._declare?.FolderPath ?? "") || !Directory.Exists(swap.Candidate) ||
                !string.Equals(ReadModVersion(swap.Candidate), swap.Version, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(path);
                TryDeleteDirectory(swap.Candidate);
                return false;
            }
            string busy = SwapIn(swap);
            if (busy != null)
            {
                // 刚启动还被占着：留着，退出时再换
                lock (Sync) _swap = swap;
                lock (Sync)
                {
                    _snapshot.AvailableVersion = swap.Version;
                    _snapshot.LocalPackagePath = swap.PackagePath;
                    PublishLocked(EmpireCraftUpdateStatus.InstallPending);
                }
                return true;
            }
            WriteSuccess(swap, restartNeeded: true);
            ThreadPool.QueueUserWorkItem(_ => DisposeBackup(swap.Backup, swap.ModsPath, swap.InstallId, swap.Archive));
            lock (Sync)
            {
                _snapshot.AvailableVersion = swap.Version;
                _snapshot.LocalPackagePath = swap.PackagePath;
                _snapshot.Error = null;
                PublishLocked(EmpireCraftUpdateStatus.InstallScheduled);
            }
            // 安装结果留着：下次启动(加载的是新版了)再核对
            LogService.LogInfo("EmpireCraft update " + swap.Version + " installed at startup; restart the game once more to load it");
            return true;
        }
        catch (Exception error)
        {
            TryDeleteFile(path);
            TryDeleteDirectory(swap?.Candidate);
            InstallFailedWith(swap?.PackagePath, error.Message);
            return true;
        }
    }

    // 候选目录：旧目录整拷一份，新版的各目录整个换掉，新版的顶层文件覆盖(旧版独有的顶层文件留着)
    private static void BuildCandidate(string source, string target, string candidate, string version)
    {
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Installed mod folder is missing.");
        if (Directory.Exists(Path.Combine(source, ".git")) || File.Exists(Path.Combine(source, ".git")))
            throw new Exception("Update package contains a Git checkout marker.");
        TryDeleteDirectory(candidate);
        CopyDirectory(target, candidate);
        foreach (string directory in Directory.GetDirectories(source))
        {
            string destination = Path.Combine(candidate, Path.GetFileName(directory));
            TryDeleteDirectory(destination);
            if (Directory.Exists(destination)) throw new IOException("Could not clear " + destination);
            CopyDirectory(directory, destination);
        }
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(candidate, Path.GetFileName(file)), true);
        if (!Directory.Exists(Path.Combine(candidate, "Scripts")))
            throw new Exception("Candidate update is missing Scripts.");
        if (!string.Equals(ReadModVersion(candidate), version, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Candidate update version mismatch.");
    }

    private static bool TryMoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    // 以前装完没删掉的候选、备份(没有进行中的安装时)
    private static void CleanLeftovers(string workRoot, string name)
    {
        if (File.Exists(Path.Combine(GetUpdatesRoot(), InstallStateFileName))) return;
        try
        {
            foreach (string directory in Directory.GetDirectories(workRoot, "." + name + ".update-*"))
                TryDeleteDirectory(directory);
            foreach (string directory in Directory.GetDirectories(workRoot, "." + name + ".backup-*"))
                TryDeleteDirectory(directory);
        }
        catch
        {
            // 只是清理
        }
    }

    private static string ArchiveBackup(string backup, string modsPath, string installId)
    {
        string archiveRoot = Path.GetFullPath(GetArchivedVersionsRoot());
        string modsRoot = modsPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (PathsEqual(archiveRoot, modsPath) || archiveRoot.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update archive must be outside the Mods directory.");
        Directory.CreateDirectory(archiveRoot);
        string oldVersion = SanitizeFileName(ReadModVersion(backup) ?? "unknown");
        string archivePath = Path.Combine(archiveRoot, "EmpireCraft_" + oldVersion + "_" +
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "_" + installId + ".ecbackup");
        string temporary = archivePath + ".tmp";
        TryDeleteFile(temporary);
        ZipFile.CreateFromDirectory(backup, temporary, CompressionLevel.Optimal, false);
        File.Move(temporary, archivePath);
        return archivePath;
    }

    private static string ReadModVersion(string folder)
    {
        string path = Path.Combine(folder, "mod.json");
        if (!File.Exists(path)) return null;
        return JObject.Parse(File.ReadAllText(path)).Value<string>("version")?.Trim();
    }

    private static void WriteJson(string path, JObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json.ToString(), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // 清理失败无妨(在 Mods 外面、以点开头，不会被当成模组)
        }
    }

    // 启动时：上次对调中途被关的，按记录换回旧版或确认装好；旧版本(脚本安装、逐个文件补)留下的记录清掉
    private static void FinishInterruptedInstall()
    {
        string stateRoot = GetUpdatesRoot();
        TryDeleteFile(Path.Combine(stateRoot, PendingInstallFileName));
        TryDeleteFile(Path.Combine(stateRoot, "deferred-install.json"));
        string statePath = Path.Combine(stateRoot, InstallStateFileName);
        if (!File.Exists(statePath)) return;
        try
        {
            JObject state = JObject.Parse(File.ReadAllText(statePath));
            string target = state.Value<string>("target"), backup = state.Value<string>("backup");
            string candidate = state.Value<string>("candidate"), version = state.Value<string>("version");
            string phase = state.Value<string>("phase");
            bool installed = Directory.Exists(target) &&
                             string.Equals(ReadModVersion(target), version, StringComparison.OrdinalIgnoreCase);
            string detail;
            if (phase == "swapped" && installed)
            {
                // 对调已经完成、只差收尾：算装好了
                TryDeleteDirectory(backup);
                detail = null;
            }
            else
            {
                if (phase == "parts")
                    UndoParts(target, backup, candidate, state.Value<JArray>("moved")?.Values<string>() ?? Array.Empty<string>());
                else if (!Directory.Exists(target) && Directory.Exists(backup))
                    Directory.Move(backup, target);
                detail = "The game closed while the update was being installed; the previous installation was kept.";
            }
            TryDeleteDirectory(candidate);
            if (detail != null) TryDeleteDirectory(backup);
            WriteJson(Path.Combine(stateRoot, InstallResultFileName), new JObject
            {
                ["status"] = detail == null ? "success" : "failure", ["version"] = version,
                ["packagePath"] = state.Value<string>("packagePath"), ["targetPath"] = target, ["detail"] = detail ?? ""
            });
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not recover an interrupted update: " + error.Message);
        }
        TryDeleteFile(statePath);
    }

    private static bool RestoreInstallState()
    {
        if (!_quitHooked)
        {
            _quitHooked = true;
            UnityEngine.Application.quitting += OnQuitting;
        }
        FinishInterruptedInstall();
        if (SwapPendingAtStartup()) return true;
        string stateRoot = GetUpdatesRoot();
        string resultPath = Path.Combine(stateRoot, InstallResultFileName);
        string pendingPath = Path.Combine(stateRoot, PendingInstallFileName);
        return File.Exists(resultPath) && RestoreInstallResult(resultPath, pendingPath);
    }

    private static bool RestoreInstallResult(string resultPath, string pendingPath)
    {
        try
        {
            JObject result = JObject.Parse(File.ReadAllText(resultPath));
            string status = result.Value<string>("status")?.Trim();
            string version = result.Value<string>("version")?.Trim();
            string packagePath = result.Value<string>("packagePath")?.Trim();
            string targetPath = result.Value<string>("targetPath")?.Trim();
            string detail = result.Value<string>("detail")?.Trim();
            // 换下来的旧版：按设置存档后删掉(退出时来不及做)
            string backup = result.Value<string>("backup"), modsPath = result.Value<string>("modsPath");
            string installId = result.Value<string>("installId");
            bool archive = result.Value<bool?>("archive") ?? false;
            if (!string.IsNullOrEmpty(backup))
                ThreadPool.QueueUserWorkItem(_ => DisposeBackup(backup, modsPath, installId, archive));
            string currentVersion = ReadInstalledVersion();
            string currentTarget = Path.GetFullPath(ModClass._declare?.FolderPath ?? "");
            bool reportedSuccess = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);
            bool versionMatches = !string.IsNullOrWhiteSpace(version) &&
                                  CompareVersions(currentVersion, version) >= 0;
            bool targetMatches = !string.IsNullOrWhiteSpace(targetPath) && PathsEqual(currentTarget, targetPath);

            EmpireCraftUpdateStatus restoredStatus;
            if (reportedSuccess && versionMatches && targetMatches)
            {
                restoredStatus = EmpireCraftUpdateStatus.InstallSucceeded;
                detail = null;
            }
            else
            {
                restoredStatus = EmpireCraftUpdateStatus.InstallFailed;
                if (reportedSuccess)
                {
                    detail = "The updater replaced " + targetPath + " with " + version +
                             ", but the game loaded " + currentVersion + " from " + currentTarget +
                             ". Remove duplicate EmpireCraft installations or restart once more.";
                }
                else if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = "The installer reported a failure without an error message.";
                }
            }

            lock (Sync)
            {
                _snapshot.CurrentVersion = currentVersion;
                _snapshot.AvailableVersion = version;
                _snapshot.LocalPackagePath = packagePath;
                _snapshot.Error = detail;
                _snapshot.ProgressPercent = reportedSuccess ? 100 : 0;
                PublishLocked(restoredStatus);
            }

            if (restoredStatus == EmpireCraftUpdateStatus.InstallSucceeded)
                LogService.LogInfo("EmpireCraft update installation verified: " + version);
            else
                LogService.LogWarning("EmpireCraft update installation failed: " + detail);
        }
        catch (Exception error)
        {
            lock (Sync)
            {
                _snapshot.Error = "Could not read the update installation result: " + error.Message;
                PublishLocked(EmpireCraftUpdateStatus.InstallFailed);
            }
            LogService.LogWarning("EmpireCraft update result is invalid: " + error);
        }
        finally
        {
            TryDeleteFile(resultPath);
            TryDeleteFile(pendingPath);
        }
        return true;
    }

    private static string GetUpdatesRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmpireCraft",
            "Updates");
    }

    private static string GetArchivedVersionsRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmpireCraft",
            "ArchivedVersions");
    }

    private static (bool manualPackageMode, bool archiveCurrentVersion) ReadUpdateSettings()
    {
        try
        {
            string path = Path.Combine(GetUpdatesRoot(), UpdateSettingsFileName);
            if (!File.Exists(path)) return (false, false);
            JObject settings = JObject.Parse(File.ReadAllText(path));
            return (settings.Value<bool?>("manualPackageMode") ?? false,
                    settings.Value<bool?>("archiveCurrentVersion") ?? false);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be read: " + error.Message);
            return (false, false);
        }
    }

    private static void SaveUpdateSettings()
    {
        try
        {
            lock (Sync) WriteUpdateSettings(_manualPackageMode, _archiveCurrentVersion);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be saved: " + error.Message);
        }
    }

    private static void WriteUpdateSettings(bool manualPackageMode, bool archiveCurrentVersion)
    {
        string root = GetUpdatesRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, UpdateSettingsFileName);
        string temporaryPath = path + ".tmp";
        var settings = new JObject
        {
            ["manualPackageMode"] = manualPackageMode,
            ["archiveCurrentVersion"] = archiveCurrentVersion
        };
        File.WriteAllText(temporaryPath, settings.ToString(), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporaryPath, path, null);
        else File.Move(temporaryPath, path);
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup failures must not hide the actual update result.
        }
    }

    private static string ReadInstalledVersion()
    {
        try
        {
            string path = Path.Combine(ModClass._declare?.FolderPath ?? "", "mod.json");
            if (File.Exists(path))
                return JObject.Parse(File.ReadAllText(path)).Value<string>("version")?.Trim() ?? "unknown";
        }
        catch
        {
            // A malformed local manifest is surfaced as an unknown version.
        }
        return "unknown";
    }

    private static void CopyLimited(Stream input, Stream output, long maximumBytes)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new Exception("Response exceeds the safety size limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }
}
