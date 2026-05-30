using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Agentism;

internal static class CommandRunner {
    public static readonly HashSet<string> ConsentCommands = [
        "RECYCLE_FILE", "MOVE_FILE", "MOVE_FILES", "DOWNLOAD_FILE"
    ];
    public static readonly HashSet<string> UnsafeCommands = ["del", "rmdir", "rd", "erase", "format", "type"];
    public static async Task<CmdResult> RunAgentIssuedCommand(string command, PersistentCmd console) {
        command = command.Trim();

        if (command.Contains("&&"))
            return new CmdResult(command, "", "ERROR: One command at a time! No chaining with &&", 1);

        bool isCustomTool = command.StartsWith("WRITE_FILE")
                 || command.StartsWith("READ_FILE")
                 || command.StartsWith("APPEND_FILE")
                 || command.StartsWith("COPY_FILE")
                 || command.StartsWith("COPY_FILES")
                 || command.StartsWith("MOVE_FILE")
                 || command.StartsWith("MOVE_FILES")
                 || command.StartsWith("RECYCLE_FILE")
                 || command.StartsWith("DOWNLOAD_FILE")
                 || command.StartsWith("UNZIP_FILE")
                 || command.StartsWith("ZIP_FILES")
                 || command.StartsWith("NOTIFY")
                 || command.StartsWith("READ_DIR")
                 || command.StartsWith("CHECK_EXISTS")
                 || command.StartsWith("GET_FILE_INFO")
                 || command.StartsWith("SEARCH_FILES");

        if (!isCustomTool && UnsafeCommands.Any(uc => Regex.IsMatch(command, $@"\b{uc}\b", RegexOptions.IgnoreCase)))
            return new CmdResult(command, "", "UNSAFE COMMAND DETECTED!", 1);

        if (ConsentCommands.Any(c => command.StartsWith(c))) {
            // Notification to grab your attention
            var ps = "Add-Type -AssemblyName System.Windows.Forms; " +
                     "$n = New-Object System.Windows.Forms.NotifyIcon; " +
                     "$n.Icon = [System.Drawing.SystemIcons]::Warning; " +
                     "$n.Visible = $true; " +
                     "$n.ShowBalloonTip(5000, 'Consent Required', 'Agent is waiting for your approval!', 'Warning'); " +
                     "Start-Sleep -Milliseconds 5500; " +
                     "$n.Dispose()";

            Process.Start(new ProcessStartInfo("powershell", $"-WindowStyle Hidden -Command \"{ps}\"") {
                CreateNoWindow = true
            });

            // Then just ask in the console as normal
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  Agent wants to run: {command}");
            Console.Write("  Allow? [y/n]: ");
            Console.ResetColor();

            if (Console.ReadLine()?.Trim().ToLower() != "y")
                return new CmdResult(command, "", "User denied this command.", 1);
        }

        if (command.StartsWith("WRITE_FILE")) {
            try {
                var json = command["WRITE_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var content = doc.RootElement.GetProperty("content").GetString()!;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
                return new CmdResult(command, $"Written {content.Length} chars to {path}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }
        if (command.StartsWith("READ_FILE")) {
            try {
                var json = command["READ_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var content = await File.ReadAllTextAsync(path);
                return new CmdResult(command, $"<file_content>\n{content}\n</file_content>", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("APPEND_FILE")) {
            try {
                var json = command["APPEND_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var content = doc.RootElement.GetProperty("content").GetString()!;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.AppendAllTextAsync(path, content);
                return new CmdResult(command, $"Appended {content.Length} chars to {path}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("READ_DIR")) {
            try {
                var json = command["READ_DIR".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;

                var dirs = Directory.GetDirectories(path)
                                     .Select(d => $"[DIR]  {Path.GetFileName(d)}");
                var files = Directory.GetFiles(path)
                                     .Select(f => { var i = new FileInfo(f); return $"[FILE] {i.Name} ({i.Length} bytes)"; });

                var listing = string.Join("\n", dirs.Concat(files));
                return new CmdResult(command, listing, "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("CHECK_EXISTS")) {
            try {
                var json = command["CHECK_EXISTS".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;

                if (File.Exists(path)) return new CmdResult(command, "EXISTS: file", "", 0);
                if (Directory.Exists(path)) return new CmdResult(command, "EXISTS: directory", "", 0);

                return new CmdResult(command, "NOT_EXISTS", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("RECYCLE_FILE")) {
            try {
                var json = command["RECYCLE_FILE".Length..].Trim();
                var path = JsonDocument.Parse(json).RootElement.GetProperty("path").GetString()!;

                if (!File.Exists(path) && !Directory.Exists(path))
                    return new CmdResult(command, "", $"Path not found: {path}", 1);

                bool isDir = Directory.Exists(path);
                var escapedPath = path.Replace("'", "''");
                var method = isDir ? "DeleteDirectory" : "DeleteFile";

                var ps = $"Add-Type -AssemblyName Microsoft.VisualBasic; " +
                         $"[Microsoft.VisualBasic.FileIO.FileSystem]::{method}" +
                         $"('{escapedPath}', 'OnlyErrorDialogs', 'SendToRecycleBin')";

                var result = await console.RunCommandAsync($"powershell -Command \"{ps}\"");

                return result.Succeeded
                    ? new CmdResult(command, $"Sent to Recycle Bin: {path}", "", 0)
                    : result;

            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("SEARCH_FILES")) {
            try {
                var json = command["SEARCH_FILES".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var pattern = doc.RootElement.GetProperty("pattern").GetString()!;

                var results = new List<string>();
                SearchRecursive(path, pattern, results);

                var listing = string.Join("\n", results);
                return new CmdResult(command, string.IsNullOrEmpty(listing) ? "No files found" : listing, "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("GET_FILE_INFO")) {
            try {
                var json = command["GET_FILE_INFO".Length..].Trim();
                var path = JsonDocument.Parse(json).RootElement.GetProperty("path").GetString()!;

                if (File.Exists(path)) {
                    var i = new FileInfo(path);
                    return new CmdResult(command,
                        $"Name:      {i.Name}\n" +
                        $"Size:      {i.Length} bytes\n" +
                        $"Created:   {i.CreationTime}\n" +
                        $"Modified:  {i.LastWriteTime}\n" +
                        $"Extension: {i.Extension}", "", 0);
                }

                if (Directory.Exists(path)) {
                    var i = new DirectoryInfo(path);
                    var count = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
                    return new CmdResult(command,
                        $"Name:     {i.Name}\n" +
                        $"Type:     Directory\n" +
                        $"Created:  {i.CreationTime}\n" +
                        $"Modified: {i.LastWriteTime}\n" +
                        $"Files:    {count}", "", 0);
                }

                return new CmdResult(command, "", $"Path not found: {path}", 1);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("DOWNLOAD_FILE")) {
            try {
                var json = command["DOWNLOAD_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var url = doc.RootElement.GetProperty("url").GetString()!;
                var dest = doc.RootElement.GetProperty("destination").GetString()!;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                using var client = new HttpClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(dest, FileMode.Create, FileAccess.Write);

                var buffer = new byte[8192];
                long downloadedBytes = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer)) > 0) {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    if (totalBytes > 0) {
                        var percent = (int)(downloadedBytes * 100 / totalBytes);
                        var filled = percent / 5;
                        var bar = $"[{new string('█', filled)}{new string('░', 20 - filled)}]";
                        Console.Write($"\r  {bar} {percent}% — {downloadedBytes:N0} / {totalBytes:N0} bytes");
                    }
                    else {
                        Console.Write($"\r  Downloading... {downloadedBytes:N0} bytes");
                    }
                    Console.ResetColor();
                }

                Console.WriteLine();
                return new CmdResult(command, $"Downloaded {downloadedBytes:N0} bytes to {dest}", "", 0);
            } catch (Exception ex) {
                Console.WriteLine();
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("COPY_FILES")) {
            try {
                var json = command["COPY_FILES".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var pattern = doc.RootElement.GetProperty("pattern").GetString()!;
                var dest = doc.RootElement.GetProperty("destination").GetString()!;

                Directory.CreateDirectory(dest);
                var files = Directory.GetFiles(path, pattern);
                foreach (var f in files)
                    File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);

                return new CmdResult(command, $"Copied {files.Length} file(s) to {dest}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("MOVE_FILES")) {
            try {
                var json = command["MOVE_FILES".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var pattern = doc.RootElement.GetProperty("pattern").GetString()!;
                var dest = doc.RootElement.GetProperty("destination").GetString()!;

                Directory.CreateDirectory(dest);
                var files = Directory.GetFiles(path, pattern);
                foreach (var f in files)
                    File.Move(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);

                return new CmdResult(command, $"Moved {files.Length} file(s) to {dest}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("COPY_FILE")) {
            try {
                var json = command["COPY_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var from = doc.RootElement.GetProperty("from").GetString()!;
                var to = doc.RootElement.GetProperty("to").GetString()!;

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
                return new CmdResult(command, $"Copied {from} -> {to}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("MOVE_FILE")) {
            try {
                var json = command["MOVE_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var from = doc.RootElement.GetProperty("from").GetString()!;
                var to = doc.RootElement.GetProperty("to").GetString()!;

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to, overwrite: true);
                return new CmdResult(command, $"Moved {from} -> {to}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("UNZIP_FILE")) {
            try {
                var json = command["UNZIP_FILE".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var path = doc.RootElement.GetProperty("path").GetString()!;
                var dest = doc.RootElement.GetProperty("destination").GetString()!;

                Directory.CreateDirectory(dest);
                ZipFile.ExtractToDirectory(path, dest, overwriteFiles: true);
                return new CmdResult(command, $"Extracted to {dest}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("ZIP_FILES")) {
            try {
                var json = command["ZIP_FILES".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var paths = doc.RootElement.GetProperty("paths").EnumerateArray()
                                           .Select(e => e.GetString()!).ToList();
                var dest = doc.RootElement.GetProperty("destination").GetString()!;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                using var zip = ZipFile.Open(dest, ZipArchiveMode.Create);
                foreach (var p in paths.Where(File.Exists))
                    zip.CreateEntryFromFile(p, Path.GetFileName(p));

                return new CmdResult(command, $"Zipped {paths.Count} file(s) to {dest}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }

        if (command.StartsWith("NOTIFY")) {
            try {
                var json = command["NOTIFY".Length..].Trim();
                var doc = JsonDocument.Parse(json);
                var title = doc.RootElement.GetProperty("title").GetString()!.Replace("'", "''");
                var message = doc.RootElement.GetProperty("message").GetString()!.Replace("'", "''");

                var ps = "Add-Type -AssemblyName System.Windows.Forms; " +
                         "$n = New-Object System.Windows.Forms.NotifyIcon; " +
                         "$n.Icon = [System.Drawing.SystemIcons]::Information; " +
                         "$n.Visible = $true; " +
                        $"$n.ShowBalloonTip(5000, '{title}', '{message}', 'Info'); " +
                         "Start-Sleep -Milliseconds 5500; " +
                         "$n.Dispose()";

                Process.Start(new ProcessStartInfo("powershell", $"-WindowStyle Hidden -Command \"{ps}\"") {
                    CreateNoWindow = true
                });

                return new CmdResult(command, $"Notification sent: {title}", "", 0);
            } catch (Exception ex) {
                return new CmdResult(command, "", ex.Message, 1);
            }
        }


        return await console.RunCommandAsync(command);
    }
    public static void SearchRecursive(string path, string pattern, List<string> results) {
        try {
            foreach (var f in Directory.GetFiles(path, pattern))
                results.Add($"[FILE] {f} ({new FileInfo(f).Length} bytes)");

            foreach (var dir in Directory.GetDirectories(path))
                SearchRecursive(dir, pattern, results);
        } catch (UnauthorizedAccessException) { }
    }
}
