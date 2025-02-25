using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;

class Program
{

    static async Task Main()
    {
        string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string CurseForgeApiKey = GetAPIKey();
        string htmlFilePath = GetUserFilePath();
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string modsFolder = Path.Combine(desktopPath, "Minecraft Mods");
        string actualModsFolder = Path.Combine(modsFolder, $"Modpack_{sessionId}");

        string manifestFile = Path.Combine(Path.GetDirectoryName(htmlFilePath), "manifest.json");

        if (!File.Exists(manifestFile))
        {
            Console.WriteLine("Error: manifest.json not found in the same directory as modlist.html.");
            return;
        }

        Console.WriteLine("Fetching version from manifest.json...");

        string TargetVersion = GetMinecraftVersionFromManifest(manifestFile);

        if (string.IsNullOrEmpty(TargetVersion))
        {
            Console.WriteLine("Error: Unable to extract Minecraft version from manifest.json.");
            return;
        }

        Console.WriteLine($"Mods version: {TargetVersion}");
        Console.WriteLine($"Mods folder: {actualModsFolder}");

        Process.Start("explorer.exe", actualModsFolder);

        if (!Directory.Exists(actualModsFolder))
            Directory.CreateDirectory(actualModsFolder);

        var modLinks = ExtractModSlugs(htmlFilePath);

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("x-api-key", CurseForgeApiKey);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        foreach (string modSlug in modLinks)
        {
            try
            {
                Console.WriteLine($"Fetching mod: {modSlug}...");
                string fileDownloadLink = await GetModDownloadLink(modSlug, client, TargetVersion);

                if (!string.IsNullOrEmpty(fileDownloadLink))
                {
                    string fileName = Path.Combine(actualModsFolder, GetValidFileName(Path.GetFileName(new Uri(fileDownloadLink).AbsolutePath)));
                    Console.WriteLine($"Downloading {fileDownloadLink}...");
                    byte[] fileBytes = await client.GetByteArrayAsync(fileDownloadLink);
                    await File.WriteAllBytesAsync(fileName, fileBytes);
                    Console.WriteLine($"Saved: {fileName}");
                }
                else
                {
                    Console.WriteLine($"No valid download found for {modSlug} with version {TargetVersion}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to process {modSlug}: {ex.Message}");
            }
        }

        Console.WriteLine("Download process completed.");
    }

    static string GetUserFilePath()
    {
        while (true)
        {
            Console.Write("Enter the full path of the modlist HTML file: ");
            string filePath = Console.ReadLine();
            string newfilePath = filePath.Replace("\"", "");

            if (File.Exists(newfilePath) && Path.GetExtension(newfilePath).Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                return newfilePath;
            }

            Console.WriteLine("Invalid file path. Please enter a valid HTML file.");
        }
    }
    static string GetMinecraftVersionFromManifest(string manifestFilePath)
    {
        try
        {
            string jsonText = File.ReadAllText(manifestFilePath);
            using JsonDocument doc = JsonDocument.Parse(jsonText);

            return doc.RootElement.GetProperty("minecraft").GetProperty("version").GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading manifest.json: {ex.Message}");
            return null;
        }
    }
    static string GetAPIKey()
    {
        while (true)
        {
            string hasEnv = GetAPIKey_env();
            if(hasEnv != "N/A")
            {
                Console.WriteLine("User has API key inside Environment Variables (User).");
                return hasEnv;
            } else
            {
                Console.Write("Enter your console.curseforge.com API key: ");
                string key = Console.ReadLine();
                string pattern = @"^\$2a\$\d{2}\$[A-Za-z0-9./]{53,}$";
                Regex regex = new Regex(pattern);

                if (regex.IsMatch(key))
                {
                    Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", key, EnvironmentVariableTarget.User);
                    return key;
                }

                Console.WriteLine("Invalid API key. Please enter a valid API key.");
            }
        }
    }

    static string GetAPIKey_env()
    {
        string pattern = @"^\$2a\$\d{2}\$[A-Za-z0-9./]{53,}$";
        Regex regex = new Regex(pattern);

        string key = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY", EnvironmentVariableTarget.User);
        if(string.IsNullOrEmpty(key))
        {
            return "N/A";
        }
        if (regex.IsMatch(key))
        {
            return key;
        } else
        {
            return "N/A";
        }
    }

    static List<string> ExtractModSlugs(string htmlFilePath)
    {
        var lines = File.ReadAllLines(htmlFilePath);
        List<string> modSlugs = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("https://www.curseforge.com/minecraft/mc-mods/"))
            {
                string slug = line.Split(new[] { "mc-mods/" }, StringSplitOptions.None)[1].Split('"')[0];
                modSlugs.Add(slug);
            }
        }
        return modSlugs;
    }

    static async Task<string> GetModDownloadLink(string modSlug, HttpClient client, string TargetVersion)
    {
        string modSearchUrl = $"https://api.curseforge.com/v1/mods/search?gameId=432&slug={modSlug}";
        HttpResponseMessage modResponse = await client.GetAsync(modSearchUrl);

        if (!modResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch mod info: {modResponse.StatusCode}");
            return null;
        }

        var modData = JsonDocument.Parse(await modResponse.Content.ReadAsStringAsync());
        int modId = modData.RootElement.GetProperty("data")[0].GetProperty("id").GetInt32();

        string filesUrl = $"https://api.curseforge.com/v1/mods/{modId}/files";
        HttpResponseMessage filesResponse = await client.GetAsync(filesUrl);

        if (!filesResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch files for mod {modSlug}: {filesResponse.StatusCode}");
            return null;
        }

        var filesData = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync());
        foreach (var file in filesData.RootElement.GetProperty("data").EnumerateArray())
        {
            foreach (var gameVersion in file.GetProperty("gameVersions").EnumerateArray())
            {
                if (gameVersion.GetString() == TargetVersion)
                {
                    return file.GetProperty("downloadUrl").GetString();
                }
            }
        }

        return null;
    }

    static string GetValidFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}