using System.Diagnostics;
using System.Text;

namespace Agentism;

public class PersistentCmd : IAsyncDisposable {
    private readonly Process _process;
    private readonly string _sentinel = $"__CMD_DONE_{Guid.NewGuid():N}__";

    public PersistentCmd() {
        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        _process.Start();
        Task.Run(ReadErrorStreamAsync);

        _process.StandardInput.WriteLine("@echo off");
        _process.StandardInput.WriteLine($"echo {_sentinel}0");

        while (true) {
            var line = _process.StandardOutput.ReadLine();
            if (line != null && line.StartsWith(_sentinel)) break;
        }
    }

    private readonly StringBuilder _errorBuffer = new();

    private async Task ReadErrorStreamAsync() {
        while (!_process.HasExited) {
            var line = await _process.StandardError.ReadLineAsync();
            if (line != null)
                _errorBuffer.AppendLine(line);
        }
    }

    public async Task<CmdResult> RunCommandAsync(
        string command,
        CancellationToken cancellationToken = default) {
        _errorBuffer.Clear();

        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.WriteLineAsync($"echo {_sentinel}%errorlevel%");

        var outputBuilder = new StringBuilder();
        int exitCode = 0;

        while (true) {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith(_sentinel)) {
                exitCode = int.TryParse(line[_sentinel.Length..], out var code) ? code : 0;
                break;
            }
            if (line == command) continue;
            if (line.StartsWith("echo ")) continue;

            outputBuilder.AppendLine(line);
        }

        return new CmdResult(
            Command: command,
            Output: outputBuilder.ToString().Trim(),
            Error: _errorBuffer.ToString().Trim(),
            ExitCode: exitCode
        );
    }

    public async ValueTask DisposeAsync() {
        await _process.StandardInput.WriteLineAsync("exit");
        _process.WaitForExit(3000);
        _process.Dispose();
    }
}

public record CmdResult(string Command, string Output, string Error, int ExitCode) {
    public bool Succeeded => ExitCode == 0;
    public bool Failed => !Succeeded;

    public override string ToString() =>
        $"[{(Succeeded ? "OK" : "FAIL")}]" +
        (string.IsNullOrWhiteSpace(Output) ? "" : $"\n  Output: {Output.Trim()}") +
        (string.IsNullOrWhiteSpace(Error) ? "" : $"\n  Error:  {Error.Trim()}");
}