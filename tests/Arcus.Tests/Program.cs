using Arcus;
using System.IO;

var checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
Check(FolderNormalizer.Normalize(@"D:\Avatar\..\Assets\") == @"D:\Assets", "canonicalize parent and trailing separator");
Check(FolderNormalizer.Normalize(@"C:\") == @"C:\", "preserve drive root");
Check(FolderNormalizer.Normalize(@"\\server\share\folder\..") == @"\\server\share", "UNC normalization");
foreach (var path in new[] { "relative", @"C:relative", "shell:Downloads", @"\\server", @"\\?\C:\data", @"\\.\pipe\data", "https://example.com", "" })
    Check(FolderNormalizer.Normalize(path) == null, "reject non-normal path: " + path);
var items = new[] {
    new FolderInfo(@"D:\Assets\", "Explorer", 1, null, null, null, null, ProbeStatus.Partial),
    new FolderInfo(@"d:\assets", "Files", 2, null, null, null, null, ProbeStatus.Partial),
    new FolderInfo(@"C:\Data", "Explorer", 3, null, null, null, null, ProbeStatus.Partial) };
var merged = FolderNormalizer.Merge(items);
Check(merged.Count == 2, "case-insensitive cross-source deduplication");
Check(merged[0].Path == @"C:\Data" && merged.All(x => x.LastUsed == null), "stable fallback order without invented history");
Check(DialogNavigator.Navigate(new("navigate", Path: @"Z:\arcus-does-not-exist-913abd")).Status == ProbeStatus.Failed, "nonexistent destination refused before UI access");
Check(DialogNavigator.Navigate(new("navigate", Window: 0, Path: Environment.GetFolderPath(Environment.SpecialFolder.Windows))).Status == ProbeStatus.Failed, "invalid target refused");
var json = System.Text.Json.JsonSerializer.Serialize(new ProbeResponse(ProbeStatus.Partial, "日本語", [new("Explorer", ProbeStatus.Partial, merged, "test")]), JsonStore.Options);
Check(System.Text.Json.JsonSerializer.Deserialize<ProbeResponse>(json, JsonStore.Options)?.Adapters?[0].Folders.Count == 2, "evidence JSON round-trip");
UiNode Node(string id, string? parent, string type, string automationId, string cls) => new(id, parent, type, "", automationId, cls, 0, false, "", true, false, null);
var host = Node("host", null, "ControlType.ComboBox", "FileNameControlHost", "AppControlHost");
var file = Node("file", "host", "ControlType.Edit", "1001", "Edit");
var address = Node("address", "band", "ControlType.Edit", "41477", "Edit");
var breadcrumb = Node("breadcrumb", "band", "ControlType.ToolBar", "1001", "ToolbarWindow32");
var wrongFile = Node("wrong", "breadcrumb", "ControlType.Edit", "1001", "Edit");
List<UiNode> fixture = [host, file, address, breadcrumb, wrongFile];
Check(AutomationProbe.IsFileName(file, fixture), "filename identified by parent semantics");
Check(!AutomationProbe.IsFileName(address, fixture), "address edit excluded from filename snapshot");
Check(!AutomationProbe.IsFileName(wrongFile, fixture), "numeric ID alone cannot identify filename edit");
var typeField = Node("type", null, "ControlType.ComboBox", "FileTypeControlHost", "AppControlHost");
Check(AutomationProbe.HasUniqueFileFields(new List<UiNode> { host, file, typeField }), "focused discovery requires one validated filename and filetype");
Check(!AutomationProbe.HasUniqueFileFields(new List<UiNode> { host, file }), "focused discovery rejects missing filetype");
Check(!AutomationProbe.HasUniqueFileFields(new List<UiNode> { breadcrumb, wrongFile, typeField }), "focused discovery rejects edit with misleading numeric ID");
Check(!AutomationProbe.HasUniqueFileFields(new List<UiNode> { host, file, file with { RuntimeId = "duplicate" }, typeField }), "focused discovery rejects ambiguous filename fields");
var labels = FolderLabels.Create(new[] { @"D:\Avatar\Textures", @"E:\Assets\Textures", @"C:\Downloads" });
Check(labels.SequenceEqual(new[] { "Avatar / Textures", "Assets / Textures", "Downloads" }), "minimal parent labels for name collisions");
labels = FolderLabels.Create(new[] { @"D:\Assets\Textures", @"E:\Assets\Textures" });
Check(labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "expand to drive when parents also collide");
Check(FolderLabels.Create(new[] { @"C:\" })[0] == @"C:\", "root folder button label");
var work = new PixelRect(0, 0, 1920, 1040);
Check(BarPlacement.Calculate(new(100, 100, 900, 700), work, 180) == new PixelRect(100, 700, 900, 880), "bar below when space exists");
Check(BarPlacement.Calculate(new(100, 400, 900, 1000), work, 180) == new PixelRect(100, 220, 900, 400), "bar above near taskbar");
Check(BarPlacement.Calculate(new(100, 40, 900, 1010), work, 180) == new PixelRect(100, 0, 900, 40), "constrain height when neither side fits");
Check(BarPlacement.Calculate(new(-400, 100, 2200, 700), work, 180) == new PixelRect(0, 700, 1920, 880), "clamp oversized dialog width to work area");
var secondary = new PixelRect(-2560, -500, 0, 940);
var placed = BarPlacement.Calculate(new(-2400, -350, -1100, 700), secondary, 220);
Check(placed == new PixelRect(-2400, 700, -1100, 920), "negative multi-monitor coordinates");
var offscreen = BarPlacement.Calculate(new(100, -900, 900, -100), work, 180);
Check(offscreen.Top >= work.Top && offscreen.Bottom <= work.Bottom, "offscreen dialog cannot place bar outside work area");
var settingsTest = Path.Combine(Path.GetTempPath(), "arcus-settings-test-" + Guid.NewGuid().ToString("N"));
try
{
    var settingsStore = new SettingsManager(settingsTest);
    Check(settingsStore.Load().Settings.Favorites.Count == 0, "first-run settings defaults");
    settingsStore.Save(new ArcusSettings { Favorites = [new(@"D:\Avatar", "Avatar")], MaximumBarHeight = 180 });
    settingsStore.Save(new ArcusSettings { Favorites = [new(@"D:\Assets", "Assets")], MaximumBarHeight = 200 });
    Check(settingsStore.Load().Settings.Favorites[0].DisplayName == "Assets", "settings persisted");
    File.WriteAllText(settingsStore.FilePath, "{invalid json");
    var recovered = settingsStore.Load();
    Check(recovered.Settings.Favorites[0].DisplayName == "Avatar" && recovered.Notice != null, "corrupt primary recovered from backup");
    settingsStore.Save(recovered.Settings);
    Check(JsonStore.Read<ArcusSettings>(settingsStore.FilePath + ".bak").Favorites[0].DisplayName == "Avatar", "save after corruption preserves valid backup");
    File.WriteAllText(settingsStore.FilePath, "null"); File.WriteAllText(settingsStore.FilePath + ".bak", "null");
    Check(settingsStore.Load().Settings.ExplorerEnabled, "two broken settings files fall back without crash");
    var importFile = Path.Combine(settingsTest, "favorites.json");
    FavoritesManager.Export(importFile, [new(@"D:\Assets", "二番"), new(@"D:\Avatar", "一番")]);
    Check(FavoritesManager.Import(importFile).Select(x => x.DisplayName).SequenceEqual(new[] { "二番", "一番" }), "favorite import/export preserves order and names");
    File.WriteAllText(importFile, "[{\"Path\":\"relative\",\"DisplayName\":\"bad\"}]");
    var rejected = false; try { FavoritesManager.Import(importFile); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "favorite import rejects relative path before changing settings");
}
finally
{
    // Only explicitly created test files; no recursive directory deletion.
    if (Directory.Exists(settingsTest)) { foreach (var filePath in Directory.GetFiles(settingsTest)) File.Delete(filePath); Directory.Delete(settingsTest); }
}
var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
foreach (var name in new[] { "open-navigation-profile.json", "save-navigation-profile.json",
    Path.Combine("click-latency", "after", "open-nav-1.json"), Path.Combine("click-latency", "after", "save-nav-1.json") })
{
    var evidencePath = Path.Combine(evidenceDirectory, name);
    if (!File.Exists(evidencePath)) throw new FileNotFoundException("Required public test fixture missing", evidencePath);
    var evidence = JsonStore.Read<ProbeResponse>(evidencePath);
    Check(evidence.Status == ProbeStatus.Success && evidence.Dialogs?.Count == 2, "synthetic navigation preservation profile: " + name);
    var before = evidence.Dialogs![0]; var after = evidence.Dialogs[1];
    Check(before.SupportedStructure && after.SupportedStructure, "shell dialog structure: " + name);
    Check(AutomationProbe.HasUniqueFileFields(before.Nodes), "focused discovery matches recorded standard dialog: " + name);
    Check(before.Nodes.Single(x => AutomationProbe.IsFileName(x, before.Nodes)).Value ==
        after.Nodes.Single(x => AutomationProbe.IsFileName(x, after.Nodes)).Value, "recorded filename preservation: " + name);
    string? TypeSelection(DialogInfo dialog) => dialog.Nodes.Single(x => x.Type == "ControlType.ComboBox" && x.AutomationId is "1136" or "FileTypeControlHost").Selection;
    Check(TypeSelection(before) != null && TypeSelection(before) == TypeSelection(after), "recorded file-type preservation: " + name);
}
FilesWindowState FilesFixture(string name)
{
    var nodes = JsonStore.Read<ProbeResponse>(Path.Combine(evidenceDirectory, name)).Nodes!;
    var tabControl = nodes.Single(x => x.AutomationId == "HorizontalTabView");
    var map = nodes.ToDictionary(x => x.RuntimeId);
    bool UnderTab(UiNode node)
    {
        while (node.ParentId != null && map.TryGetValue(node.ParentId, out var parent))
        { if (parent.RuntimeId == tabControl.RuntimeId) return true; node = parent; }
        return false;
    }
    var tabs = nodes.Where(x => x.Type == "ControlType.TabItem" && UnderTab(x)).ToArray();
    return new(nodes[0].RuntimeId, tabs.Select(x => x.RuntimeId).ToArray(),
        tabs.Where(x => x.Selection == "True").Select(x => x.RuntimeId).ToArray(),
        nodes.Where(x => x.AutomationId == "FileList" && x.Type == "ControlType.List" && !x.Offscreen).Select(x => x.RuntimeId).ToArray(),
        nodes.Where(x => x.AutomationId == "CurrentPathGet" && x.Type == "ControlType.Edit" && x.Class == "TextBox").Select(x => x.Value).ToArray());
}
var filesWindow = new WindowInfo(1446010, 26504, "Files", "WinUIDesktopWin32WindowClass", "unused title");
var filesState = FilesFixture("files-folder-tree-current.json");
var filesResult = FilesPathPolicy.Evaluate(filesWindow, filesState, filesState);
Check(filesResult.Status == ProbeStatus.Partial && filesResult.Folder?.Path == @"E:\" && filesResult.Folder.TabId != null,
    "synthetic Files active tab path without title inference");
var settingsState = FilesFixture("files-settings-tree.json");
Check(settingsState.PathValues.Contains(@"E:\") && FilesPathPolicy.Evaluate(filesWindow, settingsState, settingsState).Folder == null,
    "Files settings tab cached filesystem path is rejected");
foreach (var changed in new[] {
    filesState with { SelectedTabIds = ["other-tab"] }, filesState with { TabIds = [] },
    filesState with { RootId = "other-window" }, filesState with { VisibleFileListIds = ["other-pane"] },
    filesState with { VisibleFileListIds = [] }, filesState with { PathValues = [@"D:\"] },
    filesState with { PathValues = [@"E:\", @"D:\"] }, filesState with { PathValues = ["Home"] },
    filesState with { SelectedTabIds = [] }, filesState with { SelectedTabIds = filesState.TabIds.Take(2).ToArray() } })
    Check(FilesPathPolicy.Evaluate(filesWindow, filesState, changed).Folder == null, "reject changed/ambiguous Files context " + checks);
Check(FilesPathPolicy.Evaluate(filesWindow with { Process = "Other" }, filesState, filesState).Folder == null, "reject wrong source process");
var liveFiles = JsonStore.Read<ProbeResponse>(Path.Combine(evidenceDirectory, "files-collection.json"));
Check(liveFiles.Status == ProbeStatus.Partial && liveFiles.Adapters![0].Folders.Single().Path == @"E:\",
    "synthetic Files adapter result fixture");
var savedFixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(evidenceDirectory, "files-recheck-ready-session.json")));
var savedJson = System.Text.Json.JsonSerializer.Serialize(new { LastSessionTabList = savedFixture.RootElement.GetProperty("SavedTabs") });
var savedResult = FilesSessionReader.Parse(savedJson);
Check(savedResult.Folders.Count == 3 && savedResult.Folders.All(x => x.IsCached && x.Window == 0 && x.IsActive == null && x.LastUsed == null),
    "synthetic Files restore tabs retain uncertain provenance");
Check(savedResult.Folders.Any(x => x.Path == @"D:\Example\Project"), "parse nested Files JSON escaping");
string SavedTab(string left, string? right = null) => System.Text.Json.JsonSerializer.Serialize(new {
    InitialPageType = "Files.App.Views.ShellPanesPage", NavigationParameter = new { LeftPaneNavPathParam = left, RightPaneNavPathParam = right } });
var splitJson = System.Text.Json.JsonSerializer.Serialize(new { LastSessionTabList = new[] {
    SavedTab(@"D:\Left", @"E:\Right"), SavedTab("Settings"), SavedTab("Home"), SavedTab("relative"), "{broken" } });
var splitSaved = FilesSessionReader.Parse(splitJson);
Check(splitSaved.Folders.Count == 2 && splitSaved.Folders.Select(x => x.PaneId).SequenceEqual(new[] { "left", "right" }),
    "restore reader includes both panes and isolates invalid/nonfilesystem entries");
foreach (var invalid in new[] { "null", "{}", "{broken", "{\"LastSessionTabList\":null}", "{\"LastSessionTabList\":[null,{},12]}" })
    Check(FilesSessionReader.Parse(invalid).Folders.Count == 0, "malformed restore data returns no folders: " + invalid);
var cachedFolder = savedResult.Folders[0];
var directFolder = cachedFolder with { IsCached = false, Window = 1, TabId = "live", IsActive = true };
Check(!FolderNormalizer.Merge(new[] { cachedFolder, directFolder }).Single().IsCached, "live data wins even if cache is first");
Check(FolderNormalizer.Merge(new[] { cachedFolder, cachedFolder }).Count == 1, "duplicate restore paths deduplicated");
savedFixture.Dispose();
var legacyMate = System.Text.Json.JsonSerializer.Deserialize<ArcusSettings>("{\"FilesMateEnabled\":true,\"Favorites\":[{\"Path\":\"E:\\\\Data\",\"DisplayName\":\"data\"}]}", JsonStore.Options)!.Validate();
Check(legacyMate.Favorites.Count == 1 && !System.Text.Json.JsonSerializer.Serialize(legacyMate, JsonStore.Options).Contains("FilesMate"), "obsolete FilesMate setting ignored without losing favorites");
var newerSettings = System.Text.Json.JsonSerializer.Deserialize<ArcusSettings>("{\"ExecutableSources\":[{\"ExecutablePath\":\"C:\\\\Apps\\\\Test.exe\"}],\"FilesEnabled\":false}", JsonStore.Options)!.Validate();
Check(!newerSettings.FilesEnabled && !System.Text.Json.JsonSerializer.Serialize(newerSettings, JsonStore.Options).Contains("ExecutableSources"), "newer custom-source setting ignored while retaining existing preferences");
var ocNodes = JsonStore.Read<ProbeResponse>(Path.Combine(evidenceDirectory, "onecommander-tree.json")).Nodes!;
var ocWindow = JsonStore.Read<ProbeResponse>(Path.Combine(evidenceDirectory, "onecommander-windows.json")).Windows!.Single(x => x.Process == "OneCommander");
var ocRoot = ocNodes[0].RuntimeId;
var ocPaths = ocNodes.Where(x => x.ParentId == ocRoot && x.Type == "ControlType.Edit" && x.AutomationId == "CurrentPathGet" && x.Class == "TextBox").ToArray();
var ocState = new OneCommanderWindowState(ocRoot, ocNodes.Where(x => x.ParentId == ocRoot && x.AutomationId == "BrowserUC" && !x.Offscreen).Select(x => x.RuntimeId).ToArray(),
    ocPaths.Select(x => x.RuntimeId).ToArray(), ocPaths.Select(x => x.Value).ToArray());
var ocLive = OneCommanderAdapter.Evaluate(ocWindow, ocState, ocState);
Check(ocLive.Folder is { Path: @"E:\", IsCached: false, IsActive: true, TabId: null, PaneId: null }, "synthetic OneCommander CurrentPathGet yields live path without tab/title inference");
foreach (var changed in new[] { ocState with { RootId = "other" }, ocState with { BrowserIds = [] },
    ocState with { BrowserIds = ["other-browser"] }, ocState with { PathIds = [] }, ocState with { PathIds = ["other-field"] },
    ocState with { PathValues = [@"D:\"] }, ocState with { PathValues = ["relative"] },
    ocState with { PathValues = [@"E:\", @"D:\"] } })
    Check(OneCommanderAdapter.Evaluate(ocWindow, ocState, changed).Folder == null, "OneCommander rejects changed/ambiguous context " + checks);
Check(OneCommanderAdapter.Evaluate(ocWindow with { Process = "Other" }, ocState, ocState).Folder == null, "OneCommander rejects another process with matching UI fields");
var ocSaved = OneCommanderAdapter.ParseSavedTabs(File.ReadAllText(Path.Combine(evidenceDirectory, "onecommander-saved-tabs.json")), "TabsPersonal1.json");
Check(ocSaved.Folders.Count == 2 && ocSaved.Folders.All(x => x.IsCached && x.IsActive == null && x.Window == 0 && x.PaneId == "browser:1" && x.LastUsed == null),
    "synthetic OneCommander tabs retain saved provenance, not AccessedT activity");
foreach (var name in new[] { "TabsPersonal1.123.tmp.json", "TabsPersonal1.json~RF123.TMP", "Destinations.json", "../TabsPersonal1.json", "TabsPersonal1Backup.json" })
    Check(!OneCommanderAdapter.IsSavedTabsFile(name), "OneCommander ignores temporary/backup/unrelated files: " + name);
Check(OneCommanderAdapter.IsSavedTabsFile("TabsCustom.Window2.json"), "OneCommander accepts second browser and named-window tabs");
var ocInvalidItems = OneCommanderAdapter.ParseSavedTabs("[{\"FullPath\":\"C:/file.txt\",\"IsFolder\":false},{\"FullPath\":\"relative\",\"IsFolder\":true},{\"FullPath\":\"C:/unspecified\"},null,{}]", "Tabs2.json");
Check(ocInvalidItems.Folders.Count == 0, "OneCommander ignores files, relative paths and unspecified items");
foreach (var invalid in new[] { "null", "{}", "{broken" })
    Check(OneCommanderAdapter.ParseSavedTabs(invalid, "TabsPersonal1.json").Folders.Count == 0, "OneCommander rejects invalid saved tabs: " + invalid);
var ocMerged = FolderNormalizer.Merge(ocSaved.Folders.Concat(new[] { ocLive.Folder! }));
Check(ocMerged.Count == 2 && ocMerged.Single(x => x.Path == @"E:\").IsCached == false, "OneCommander live path overrides matching saved tab");
var oldSigmaSettings = System.Text.Json.JsonSerializer.Deserialize<ArcusSettings>("{\"SigmaEnabled\":true,\"Favorites\":[]}", JsonStore.Options)!;
Check(oldSigmaSettings.OneCommanderEnabled && !System.Text.Json.JsonSerializer.Serialize(oldSigmaSettings).Contains("Sigma"), "old Sigma setting ignored; Sigma capability removed");
Check(UiText.Resolve("auto", "ja-JP") == "ja" && UiText.Resolve("auto", "en-US") == "en" && UiText.Resolve("auto", "fr-FR") == "en", "automatic UI language follows Windows with English fallback");
Check(UiText.Resolve("en", "ja-JP") == "en" && UiText.Resolve("ja", "en-US") == "ja", "explicit UI language overrides Windows");
var priorLanguage = UiText.Language;
try
{
    UiText.Language = "en";
    Check(UiText.T("保存") == "Save" && UiText.T("{0}で開いているフォルダ", "Files") == "Folders open in Files", "English UI text and placeholders");
    Check(UiText.English.Values.All(x => !x.Contains("{1}") || x.Contains("{0}")), "translation format placeholders retain their arguments");
    var englishSettings = System.Text.Json.JsonSerializer.Deserialize<ArcusSettings>("{\"Language\":\"en\",\"Favorites\":[]}", JsonStore.Options)!.Validate();
    Check(englishSettings.Language == "en" && System.Text.Json.JsonSerializer.Serialize(englishSettings, JsonStore.Options).Contains("en"), "language preference round trip");
    var englishValidation = false;
    try { new ArcusSettings { MaximumBarHeight = 0 }.Validate(); } catch (InvalidDataException e) { englishValidation = e.Message.StartsWith("Maximum bar height"); }
    Check(englishValidation, "settings validation error uses selected UI language");
    UiText.Language = "ja";
    Check(UiText.T("保存") == "保存", "Japanese UI text preserved");
    Check(new ArcusSettings().Validate().Language == "auto" && new ArcusSettings { Language = "unknown" }.Validate().Language == "auto", "legacy or unknown language uses automatic selection");
}
finally { UiText.Language = priorLanguage; }
Console.WriteLine($"{checks} checks passed.");
