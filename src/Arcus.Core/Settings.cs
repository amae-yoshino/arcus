using static Arcus.UiText;
using System.IO;
using System.Text.Json;

namespace Arcus;

public sealed record Favorite(string Path, string DisplayName);
public sealed record ArcusSettings
{
    public bool Enabled { get; set; } = true;
    public string Language { get; set; } = "auto";
    public bool ExplorerEnabled { get; set; } = true;
    public bool FilesEnabled { get; set; } = true;
    public bool OneCommanderEnabled { get; set; } = true;
    public bool FilediniEnabled { get; set; }
    public int MaximumBarHeight { get; set; } = 220;
    public List<string> ExcludedApplications { get; set; } = [];
    public List<Favorite> Favorites { get; set; } = [];
    public ArcusSettings Validate()
    {
        if (Language is not ("auto" or "ja" or "en")) Language = "auto";
        if (MaximumBarHeight is < 64 or > 600) throw new InvalidDataException(T("バーの最大高さは64〜600 DIPにしてください。"));
        if (Favorites == null || ExcludedApplications == null) throw new InvalidDataException(T("設定の一覧がnullです。"));
        Favorites = Favorites.Select(f => f == null ? throw new InvalidDataException(T("お気に入りがnullです。")) :
            new Favorite(FolderNormalizer.Normalize(f.Path) ?? throw new InvalidDataException(T("絶対フォルダパスではありません: {0}", f.Path)), f.DisplayName?.Trim() ?? "")).ToList();
        ExcludedApplications = ExcludedApplications.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => System.IO.Path.GetFileNameWithoutExtension(x.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return this;
    }
}

public sealed record SettingsLoad(ArcusSettings Settings, string? Notice);
public sealed class SettingsManager
{
    public string DirectoryPath { get; }
    public string FilePath => System.IO.Path.Combine(DirectoryPath, "settings.json");
    public SettingsManager(string? directory = null) => DirectoryPath = directory ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "arcus");
    public SettingsLoad Load()
    {
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new(new(), null);
        try { return new(JsonStore.Read<ArcusSettings>(FilePath).Validate(), null); }
        catch (Exception e) when (Recoverable(e))
        {
            try { return new(JsonStore.Read<ArcusSettings>(FilePath + ".bak").Validate(), T("設定をバックアップから復旧しました。")); }
            catch (Exception backupError) when (Recoverable(backupError))
            { return new(new(), T("設定を読み込めないため初期状態で起動しました。元ファイルは保持されています。")); }
        }
    }
    public void Save(ArcusSettings settings)
    {
        settings.Validate(); Directory.CreateDirectory(DirectoryPath);
        // Never replace a valid backup with an unreadable primary file.
        if (File.Exists(FilePath))
        {
            try { JsonStore.Read<ArcusSettings>(FilePath).Validate(); File.Copy(FilePath, FilePath + ".bak", true); }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            { File.Copy(FilePath, FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}", false); }
        }
        JsonStore.Write(FilePath, settings);
    }
    private static bool Recoverable(Exception e) => e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException;
}

public static class FavoritesManager
{
    public static List<Favorite> Import(string path) => new ArcusSettings { Favorites = JsonStore.Read<List<Favorite>>(path) }.Validate().Favorites;
    public static void Export(string path, List<Favorite> favorites) => JsonStore.Write(path,
        new ArcusSettings { Favorites = favorites }.Validate().Favorites);
}

public static class FolderLabels
{
    public static List<string> Create(IReadOnlyList<string> paths)
    {
        var parts = paths.Select(p => p.Replace('/', '\\').TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        var depth = Enumerable.Repeat(1, paths.Count).ToArray();
        string Label(int i) => string.Join(" / ", parts[i].TakeLast(depth[i]));
        while (true)
        {
            var duplicates = Enumerable.Range(0, paths.Count).GroupBy(Label, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).SelectMany(g => g).ToArray();
            var changed = false;
            foreach (var i in duplicates) if (depth[i] < parts[i].Length) { depth[i]++; changed = true; }
            if (!changed) break;
        }
        return Enumerable.Range(0, paths.Count).Select(i => parts[i].Length == 1 ? paths[i] : Label(i)).ToList();
    }
}

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public static class BarPlacement
{
    public static PixelRect Calculate(PixelRect dialog, PixelRect work, int wantedHeight)
    {
        var belowStart = Math.Clamp(dialog.Bottom, work.Top, work.Bottom);
        var aboveEnd = Math.Clamp(dialog.Top, work.Top, work.Bottom);
        var below = work.Bottom - belowStart;
        var above = aboveEnd - work.Top;
        var useBelow = below >= wantedHeight || (above < wantedHeight && below >= above);
        var height = Math.Min(wantedHeight, useBelow ? below : above);
        var width = Math.Min(Math.Max(1, dialog.Width), Math.Max(1, work.Width));
        var left = Math.Clamp(dialog.Left, work.Left, work.Right - width);
        return new(left, useBelow ? belowStart : aboveEnd - height, left + width,
            useBelow ? belowStart + height : aboveEnd);
    }
}
