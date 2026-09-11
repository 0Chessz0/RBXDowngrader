namespace RBXDowngrader.Core;

public static class PackageMap
{
    private static readonly IReadOnlyDictionary<string, string> Directories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RobloxApp.zip"] = "",
            ["Libraries.zip"] = "",
            ["shaders.zip"] = "shaders",
            ["ssl.zip"] = "ssl",
            ["WebView2.zip"] = "",
            ["WebView2RuntimeInstaller.zip"] = "WebView2RuntimeInstaller",
            ["content-avatar.zip"] = Path.Combine("content", "avatar"),
            ["content-configs.zip"] = Path.Combine("content", "configs"),
            ["content-fonts.zip"] = Path.Combine("content", "fonts"),
            ["content-sky.zip"] = Path.Combine("content", "sky"),
            ["content-sounds.zip"] = Path.Combine("content", "sounds"),
            ["content-textures2.zip"] = Path.Combine("content", "textures"),
            ["content-models.zip"] = Path.Combine("content", "models"),
            ["content-textures3.zip"] = Path.Combine("PlatformContent", "pc", "textures"),
            ["content-terrain.zip"] = Path.Combine("PlatformContent", "pc", "terrain"),
            ["content-platform-fonts.zip"] = Path.Combine("PlatformContent", "pc", "fonts"),
            ["content-platform-dictionaries.zip"] = Path.Combine("PlatformContent", "pc", "dictionaries"),
            ["extracontent-luapackages.zip"] = Path.Combine("ExtraContent", "LuaPackages"),
            ["extracontent-translations.zip"] = Path.Combine("ExtraContent", "translations"),
            ["extracontent-models.zip"] = Path.Combine("ExtraContent", "models"),
            ["extracontent-textures.zip"] = Path.Combine("ExtraContent", "textures"),
            ["extracontent-places.zip"] = Path.Combine("ExtraContent", "places")
        };

    public static string GetExtractionDirectory(string packageName)
    {
        if (Directories.TryGetValue(packageName, out var path))
            return path;

        // Roblox occasionally adds a root package before launchers update their maps.
        return string.Empty;
    }
}
