using System.Diagnostics;

namespace Ahsoka.CS.StreamDeck;

public sealed class DeckActionRunner
{
    private readonly PcCompanionClient? pcCompanionClient;
    private readonly WiFiConnectionManager? wifiConnectionManager;
    private readonly BluetoothConnectionManager? bluetoothConnectionManager;

    public DeckActionRunner(
        PcCompanionClient? pcCompanionClient = null,
        WiFiConnectionManager? wifiConnectionManager = null,
        BluetoothConnectionManager? bluetoothConnectionManager = null)
    {
        this.pcCompanionClient = pcCompanionClient;
        this.wifiConnectionManager = wifiConnectionManager;
        this.bluetoothConnectionManager = bluetoothConnectionManager;
    }

    public async Task<DeckActionResult> ExecuteAsync(DeckButton button)
    {
        string action = (button.Action ?? "").Trim().ToLowerInvariant();

        return action switch
        {
            "launch" => await LaunchAsync(button),
            "pc-launch" => pcCompanionClient == null
                ? new DeckActionResult(false, "Companion PC no configurado.")
                : await pcCompanionClient.LaunchAsync(button.Command),
            "pc-stream" => pcCompanionClient == null
                ? new DeckActionResult(false, "Companion PC no configurado.")
                : await pcCompanionClient.RunStreamActionAsync(button.Command),
            "wifi-connect" => wifiConnectionManager == null
                ? new DeckActionResult(false, "WiFi no configurado.")
                : await wifiConnectionManager.ConnectAsync(button.Command, button.Arguments),
            "wifi-disconnect" => wifiConnectionManager == null
                ? new DeckActionResult(false, "WiFi no configurado.")
                : await wifiConnectionManager.DisconnectAsync(),
            "wifi-scan" => wifiConnectionManager == null
                ? new DeckActionResult(false, "WiFi no configurado.")
                : wifiConnectionManager.Discover(),
            "bt-connect" => bluetoothConnectionManager == null
                ? new DeckActionResult(false, "Bluetooth no configurado.")
                : await bluetoothConnectionManager.ConnectAsync(button.Command),
            "bt-scan" => bluetoothConnectionManager == null
                ? new DeckActionResult(false, "Bluetooth no configurado.")
                : bluetoothConnectionManager.StartDiscovery(),
            "shell" => await ShellAsync(button),
            "noop" or "" => new DeckActionResult(true, "Sin accion configurada."),
            _ => new DeckActionResult(false, $"Accion no soportada: {button.Action}")
        };
    }

    private static async Task<DeckActionResult> LaunchAsync(DeckButton button)
    {
        if (string.IsNullOrWhiteSpace(button.Command))
            return new DeckActionResult(false, "El boton no tiene programa configurado.");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = button.Command,
                Arguments = button.Arguments ?? "",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(button.WorkingDirectory))
                startInfo.WorkingDirectory = button.WorkingDirectory;

            if (button.WaitForExit)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }

            using var process = Process.Start(startInfo);
            if (process == null)
                return new DeckActionResult(false, "No se pudo iniciar el proceso.");

            if (!button.WaitForExit)
                return new DeckActionResult(true, $"Programa iniciado. PID={process.Id}");

            using var timeout = new CancellationTokenSource(Math.Max(1000, button.TimeoutMs));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                string combinedOutput = string.Concat(output, error).Trim();
                string message = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(combinedOutput)
                    ? FirstLine(combinedOutput)
                    : process.ExitCode == 0
                    ? $"Comando completado. ExitCode={process.ExitCode}"
                    : $"Comando fallo. ExitCode={process.ExitCode}";

                return new DeckActionResult(process.ExitCode == 0, message, combinedOutput);
            }
            catch (OperationCanceledException)
            {
                return new DeckActionResult(true, $"Programa iniciado y sigue corriendo. PID={process.Id}");
            }
        }
        catch (Exception ex)
        {
            return new DeckActionResult(false, ex.Message);
        }
    }

    private static string FirstLine(string value)
    {
        string line = value.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        line = line.Trim();
        return line.Length <= 100 ? line : line[..99] + ".";
    }

    private static Task<DeckActionResult> ShellAsync(DeckButton button)
    {
        if (string.IsNullOrWhiteSpace(button.Command))
            return Task.FromResult(new DeckActionResult(false, "El boton no tiene comando configurado."));

        string shell;
        string arguments;

        if (OperatingSystem.IsWindows())
        {
            shell = "cmd.exe";
            arguments = $"/c {button.Command}";
        }
        else
        {
            shell = "/bin/sh";
            arguments = $"-c \"{button.Command.Replace("\"", "\\\"")}\"";
        }

        var shellButton = new DeckButton
        {
            Command = shell,
            Arguments = arguments,
            WorkingDirectory = button.WorkingDirectory,
            WaitForExit = true,
            TimeoutMs = button.TimeoutMs <= 0 ? 10000 : button.TimeoutMs
        };

        return LaunchAsync(shellButton);
    }
}
