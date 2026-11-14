using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Steamworks;

internal static class Program
{
    private const uint AppId = 4348910;
    private const string ModId = "deep_space_qol";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class State
    {
        public uint AppId { get; set; } = Program.AppId;
        public string ModId { get; set; } = Program.ModId;
        public string ItemId { get; set; } = "";
        public string OwnerSteamId { get; set; } = "";
        public string Status { get; set; } = "prepared";
        public string Version { get; set; } = "";
        public string DllSha256 { get; set; } = "";
        public string Visibility { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record Package(string Folder, string Title, string Version, string DllSha256);

    private static int Main(string[] args)
    {
        bool initialized = false;
        try
        {
            var options = Parse(args);
            string game = Full(Required(options, "game-dir"));
            string native = Path.Combine(game, "Probably Stolen_Data", "Plugins", "x86_64", "steam_api64.dll");
            if (!OperatingSystem.IsWindows() || !File.Exists(native))
                throw new InvalidOperationException("Use this tool on Windows with a legitimate installed game and its Steam client.");

            bool statusOnly = options.ContainsKey("status");
            bool validateOnly = options.ContainsKey("validate");
            bool metadataOnly = options.ContainsKey("metadata-only");
            if (!statusOnly && !validateOnly && !options.ContainsKey("publish"))
                throw new ArgumentException("Specify --status, --validate or --publish.");
            Package? package = null;
            string preview = "", description = "", statePath = "", notes = "", gallery = "";
            string visibility = "public";
            State? state = null;
            if (!statusOnly)
            {
                package = ValidatePackage(Required(options, "content"));
                preview = ValidateImage(Required(options, "preview"));
                description = File.ReadAllText(Full(Required(options, "description")), Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(description) || Encoding.UTF8.GetByteCount(description) >= 8000)
                    throw new InvalidOperationException("Description must be nonempty and less than 8000 UTF-8 bytes.");
                visibility = options.GetValueOrDefault("visibility", "public");
                if (visibility is not ("public" or "private")) throw new ArgumentException("Visibility must be public or private.");
                notes = options.GetValueOrDefault("notes", $"Deep Space QoL {package.Version}");
                statePath = Full(Required(options, "state"));
                if (!statePath.EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Publish state must use an ignored .local.json filename.");
                state = File.Exists(statePath)
                    ? JsonSerializer.Deserialize<State>(File.ReadAllText(statePath)) ?? throw new InvalidOperationException("Invalid publish state.")
                    : new State();
                if (state.AppId != AppId || state.ModId != ModId) throw new InvalidOperationException("Unrelated publish state.");
                if (state.ItemId.Length == 0 && state.Status != "prepared")
                    throw new InvalidOperationException("Previous creation has an unknown outcome. Inspect your Workshop items and restore its ID before retrying; no duplicate will be created.");
                if (state.ItemId.Length > 0 && (!ulong.TryParse(state.ItemId, out ulong id) || id == 0))
                    throw new InvalidOperationException("Invalid persisted Workshop ID.");
                if (metadataOnly && (state.ItemId.Length == 0 || state.Version != package.Version || state.DllSha256 != package.DllSha256))
                    throw new InvalidOperationException("Metadata-only updates require an existing item with the same published version and DLL hash.");
                gallery = options.TryGetValue("gallery-dir", out var folder) ? Full(folder) : "";
                foreach (string path in GalleryImages(gallery)) ValidateImage(path);
                Console.WriteLine($"Validated {ModId} {package.Version}; DLL SHA256={package.DllSha256}; visibility={visibility}.");
                if (validateOnly) return 0;
            }

            // Resolve only the game's legitimate native library. No DLLs are copied into the game or the mod.
            NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (name, _, _) =>
                name is "steam_api64" or "steam_api64.dll" ? NativeLibrary.Load(native) : IntPtr.Zero);
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
            var init = SteamAPI.InitEx(out string error);
            if (init != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                throw new InvalidOperationException($"Steam initialization failed ({init}): {error}. Start Steam and sign in first.");
            initialized = true;
            if (!SteamUser.BLoggedOn()) throw new InvalidOperationException("Steam is not logged on.");
            if (SteamUtils.GetAppID().m_AppId != AppId || !SteamApps.BIsSubscribedApp(new AppId_t(AppId)))
                throw new InvalidOperationException("The active Steam account has no available license for this game.");
            string owner = SteamUser.GetSteamID().m_SteamID.ToString();
            Console.WriteLine($"Steam ready: {SteamFriends.GetPersonaName()}; app={AppId}; game license verified.");
            if (statusOnly) return 0;
            if (state!.OwnerSteamId.Length > 0 && state.OwnerSteamId != owner)
                throw new InvalidOperationException("The persisted Workshop item belongs to a different Steam account.");
            state.OwnerSteamId = owner;
            state.Version = package!.Version;
            state.DllSha256 = package.DllSha256;
            state.Visibility = visibility;

            bool done = false;
            bool failed = false;
            CreateItemResult_t created = default;
            using var createResult = CallResult<CreateItemResult_t>.Create((result, ioFailure) =>
            {
                created = result;
                failed = ioFailure;
                done = true;
            });
            if (state.ItemId.Length == 0)
            {
                state.Status = "creation-pending";
                Save(statePath, state); // A timeout must never cause blind CreateItem retries.
                var call = SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
                if (call == SteamAPICall_t.Invalid) throw new InvalidOperationException("CreateItem did not return a valid call; inspect remote items before retrying.");
                createResult.Set(call);
                Pump(() => done, TimeSpan.FromMinutes(2));
                if (failed || created.m_eResult != EResult.k_EResultOK)
                    throw new InvalidOperationException($"CreateItem failed: {created.m_eResult}, IO={failed}. Publish state retained for reconciliation.");
                state.ItemId = created.m_nPublishedFileId.m_PublishedFileId.ToString();
                state.Status = created.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "legal-agreement-required" : "created";
                Save(statePath, state);
                Console.WriteLine($"Workshop item created: {ItemUrl(state)}");
                if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                {
                    Console.WriteLine("Accept the Steam Workshop legal agreement on the item's official page, then rerun this command. No agreement is accepted by this tool.");
                    return 4;
                }
            }

            var itemId = new PublishedFileId_t(ulong.Parse(state.ItemId));
            var query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { itemId }, 1);
            if (query == UGCQueryHandle_t.Invalid) throw new InvalidOperationException("Cannot query the persisted Workshop item.");
            uint existingPreviews;
            var existingPreviewFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!SteamUGC.SetReturnAdditionalPreviews(query, true)) throw new InvalidOperationException("Cannot request preview metadata.");
                done = false;
                failed = false;
                SteamUGCQueryCompleted_t queried = default;
                using var queryResult = CallResult<SteamUGCQueryCompleted_t>.Create((result, ioFailure) => { queried = result; failed = ioFailure; done = true; });
                var queryCall = SteamUGC.SendQueryUGCRequest(query);
                if (queryCall == SteamAPICall_t.Invalid) throw new InvalidOperationException("Workshop query returned no valid call.");
                queryResult.Set(queryCall);
                Pump(() => done, TimeSpan.FromMinutes(2));
                if (failed || queried.m_eResult != EResult.k_EResultOK || !SteamUGC.GetQueryUGCResult(query, 0, out var details) ||
                    details.m_eResult != EResult.k_EResultOK || details.m_nConsumerAppID.m_AppId != AppId || details.m_ulSteamIDOwner.ToString() != owner)
                    throw new InvalidOperationException("Cannot verify Workshop item ownership and game; no update submitted.");
                existingPreviews = SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0);
                for (uint index = 0; index < existingPreviews; index++)
                {
                    if (!SteamUGC.GetQueryUGCAdditionalPreview(query, 0, index, out _, 2048, out string filename, 2048, out var kind))
                        throw new InvalidOperationException("Cannot read existing gallery filenames; no update submitted.");
                    if (kind != EItemPreviewType.k_EItemPreviewType_Image) continue;
                    if (string.IsNullOrWhiteSpace(filename))
                        throw new InvalidOperationException("Existing gallery image has no filename; cannot safely append without duplicate detection.");
                    existingPreviewFiles.Add(Path.GetFileName(filename));
                }
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(query); }

            // Recheck immutable content directly before submitting, after any authentication wait.
            if (ValidatePackage(package.Folder).DllSha256 != package.DllSha256)
                throw new InvalidOperationException("Package changed while preparing upload.");
            var update = SteamUGC.StartItemUpdate(new AppId_t(AppId), itemId);
            if (update == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Cannot start Workshop update.");
            Require(SteamUGC.SetItemUpdateLanguage(update, "schinese"), "language");
            Require(SteamUGC.SetItemTitle(update, package.Title), "title");
            Require(SteamUGC.SetItemDescription(update, description), "description");
            Require(SteamUGC.SetItemVisibility(update, visibility == "public"
                ? ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic
                : ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate), "visibility");
            Require(SteamUGC.SetItemMetadata(update, JsonSerializer.Serialize(new { modId = ModId, version = package.Version, dllSha256 = package.DllSha256 })), "metadata");
            if (!metadataOnly) Require(SteamUGC.SetItemContent(update, package.Folder), "content");
            Require(SteamUGC.SetItemPreview(update, preview), "preview");
            int addedPreviews = 0;
            foreach (string path in GalleryImages(gallery))
                if (!existingPreviewFiles.Contains(Path.GetFileName(path)))
                {
                    Require(SteamUGC.AddItemPreviewFile(update, path, EItemPreviewType.k_EItemPreviewType_Image), "gallery image");
                    addedPreviews++;
                }
            Console.WriteLine($"Preserving {existingPreviews} gallery previews; appending {addedPreviews} new images by filename; content upload={!metadataOnly}.");
            done = false;
            failed = false;
            SubmitItemUpdateResult_t submitted = default;
            using var submitResult = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) => { submitted = result; failed = ioFailure; done = true; });
            state.Status = "upload-pending";
            Save(statePath, state);
            var submitCall = SteamUGC.SubmitItemUpdate(update, notes);
            if (submitCall == SteamAPICall_t.Invalid) throw new InvalidOperationException("Upload did not return a valid call. Item ID retained for retry.");
            submitResult.Set(submitCall);
            var progressClock = Stopwatch.StartNew();
            Pump(() =>
            {
                if (progressClock.Elapsed.TotalSeconds >= 5)
                {
                    var progress = SteamUGC.GetItemUpdateProgress(update, out ulong current, out ulong total);
                    Console.WriteLine($"Upload: {progress}; {current}/{total} bytes.");
                    progressClock.Restart();
                }
                return done;
            }, TimeSpan.FromMinutes(10));
            if (failed || submitted.m_eResult != EResult.k_EResultOK)
            {
                state.Status = "upload-result-unconfirmed";
                Save(statePath, state);
                throw new InvalidOperationException($"Upload failed or unconfirmed: {submitted.m_eResult}, IO={failed}. Reuse the recorded ID; do not create a replacement item.");
            }
            state.Status = submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "legal-agreement-required" : "uploaded";
            Save(statePath, state);
            if (!submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            {
                // Steam keeps language-specific descriptions. Updating schinese alone leaves
                // anonymous API/English fallback readers on an old version. Sync the same honest
                // Chinese text into the default language in a separate metadata-only update.
                state.Status = "default-description-pending";
                Save(statePath, state);
                var fallbackUpdate = SteamUGC.StartItemUpdate(new AppId_t(AppId), itemId);
                if (fallbackUpdate == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("Cannot synchronize default Workshop description.");
                Require(SteamUGC.SetItemUpdateLanguage(fallbackUpdate, "english"), "default language");
                Require(SteamUGC.SetItemTitle(fallbackUpdate, package.Title), "default title");
                Require(SteamUGC.SetItemDescription(fallbackUpdate, description), "default description");
                done = false;
                failed = false;
                SubmitItemUpdateResult_t fallbackResult = default;
                using var fallbackCallback = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
                { fallbackResult = result; failed = ioFailure; done = true; });
                var fallbackCall = SteamUGC.SubmitItemUpdate(fallbackUpdate, "");
                if (fallbackCall == SteamAPICall_t.Invalid) throw new InvalidOperationException("Default description update did not return a valid call.");
                fallbackCallback.Set(fallbackCall);
                Pump(() => done, TimeSpan.FromMinutes(2));
                if (failed || fallbackResult.m_eResult != EResult.k_EResultOK)
                    throw new InvalidOperationException($"Content uploaded, but default description update was not confirmed: {fallbackResult.m_eResult}, IO={failed}.");
                state.Status = fallbackResult.m_bUserNeedsToAcceptWorkshopLegalAgreement ? "legal-agreement-required" : "uploaded";
                submitted = fallbackResult;
                Save(statePath, state);
            }
            Console.WriteLine($"Steam upload succeeded. Item: {ItemUrl(state)}; visibility={visibility}; status={state.Status}.");
            return submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement ? 4 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { if (initialized) SteamAPI.Shutdown(); }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string> { "status", "validate", "publish", "metadata-only", "game-dir", "content", "preview", "description", "state", "visibility", "notes", "gallery-dir" };
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : throw new ArgumentException("Expected --option.");
            if (!allowed.Contains(key) || result.ContainsKey(key)) throw new ArgumentException($"Unknown or repeated option: {key}");
            result[key] = key is "status" or "validate" or "publish" or "metadata-only" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value: {key}");
        }
        if (new[] { "status", "validate", "publish" }.Count(result.ContainsKey) != 1) throw new ArgumentException("Select exactly one operation.");
        return result;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing --{key}.");
    private static string Full(string path) => Path.GetFullPath(path);
    private static void Require(bool result, string field) { if (!result) throw new InvalidOperationException($"Steam rejected Workshop {field}; nothing submitted."); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static string ItemUrl(State state) => $"https://steamcommunity.com/sharedfiles/filedetails/?id={state.ItemId}";
    private static string[] GalleryImages(string folder)
    {
        if (folder.Length == 0 || !Directory.Exists(folder)) return [];
        var files = Directory.GetFiles(folder)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png").Order(StringComparer.Ordinal).ToArray();
        if (files.Length > 10)
            throw new InvalidOperationException("This publisher accepts at most 10 images per update batch. Pass a version-specific gallery folder; existing remote images are preserved. No images were silently omitted.");
        return files;
    }

    private static string ValidateImage(string path)
    {
        path = Full(path);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 8 || file.Length >= 1024 * 1024) throw new InvalidOperationException($"Image must exist and be smaller than 1 MiB: {path}");
        byte[] bytes = File.ReadAllBytes(path);
        if (!(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
              bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)) throw new InvalidOperationException("Expected a real PNG or JPEG image.");
        return path;
    }

    private static Package ValidatePackage(string folder)
    {
        folder = Full(folder);
        string[] allowed = ["DeepSpaceQoL.dll", "manifest.xml", "README.md", "LICENSE", "BUILD-INFO.json", "SHA256SUMS.txt"];
        if (!Directory.Exists(folder) || Directory.GetDirectories(folder).Length != 0 ||
            !Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(allowed.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("Use the exact six-file validated mod package, with no extra files or directories.");
        var manifest = XDocument.Load(Path.Combine(folder, "manifest.xml")).Root ?? throw new InvalidOperationException("Invalid manifest.");
        if ((string?)manifest.Element("ID") != ModId) throw new InvalidOperationException("Unexpected mod ID.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(folder, "SHA256SUMS.txt")))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split("  ", 2, StringSplitOptions.None);
            if (parts.Length != 2 || parts[0].Length != 64 || parts[1] == "SHA256SUMS.txt" || !allowed.Contains(parts[1], StringComparer.Ordinal) ||
                !seen.Add(parts[1]) || Hash(Path.Combine(folder, parts[1])) != parts[0].ToLowerInvariant())
                throw new InvalidOperationException("Package checksum verification failed.");
        }
        if (seen.Count != 5) throw new InvalidOperationException("Incomplete checksum table.");
        return new Package(folder, (string?)manifest.Element("Name") ?? ModId,
            (string?)manifest.Element("ModVersion") ?? throw new InvalidOperationException("Version missing."), Hash(Path.Combine(folder, "DeepSpaceQoL.dll")));
    }

    private static void Save(string path, State state)
    {
        state.UpdatedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false));
        File.Move(path + ".tmp", path, true);
    }

    private static void Pump(Func<bool> done, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.Elapsed > timeout) throw new TimeoutException("Steam callback timed out. State and item ID were retained; check the remote outcome before retrying.");
            SteamAPI.RunCallbacks();
            Thread.Sleep(50);
        }
    }
}
