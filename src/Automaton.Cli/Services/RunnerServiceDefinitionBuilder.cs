using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

internal static class RunnerServiceDefinitionBuilder
{    internal static string BuildWindowsWrapperScript(RunnerInstallRecord install)
    {
        return $@"$envFile = ""{install.EnvFilePath}""
Get-Content -LiteralPath $envFile | ForEach-Object {{
  if ($_ -match '^(.*?)=(.*)$') {{
    [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
  }}
}}
& ""{install.BinaryPath}"" service *>> ""{install.LogsPath}""";
    }

    internal static string BuildSystemdService(RunnerInstallRecord install)
    {
        var user = Environment.UserName;
        return $"""
[Unit]
Description=Automaton Runner ({install.RunnerName})
After=network.target

[Service]
Type=simple
User={user}
WorkingDirectory={install.InstallRoot}
EnvironmentFile={install.EnvFilePath}
ExecStart={install.BinaryPath} service
Restart=always
RestartSec=10
StandardOutput=append:{install.LogsPath}
StandardError=append:{install.LogsPath}

[Install]
WantedBy=multi-user.target
""";
    }

    internal static string BuildLaunchdPlist(RunnerInstallRecord install)
    {
        var envVars = ParseEnvFile(install.EnvFilePath!);
        var envXml = string.Join(Environment.NewLine, envVars.Select(kvp => $"    <key>{System.Security.SecurityElement.Escape(kvp.Key)}</key>{Environment.NewLine}    <string>{System.Security.SecurityElement.Escape(kvp.Value)}</string>"));
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>{install.ServiceName}</string>
  <key>ProgramArguments</key>
  <array>
    <string>{install.BinaryPath}</string>
    <string>service</string>
  </array>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <{(install.AutoStart ? "true" : "false")}/>
  <key>WorkingDirectory</key>
  <string>{install.InstallRoot}</string>
  <key>StandardOutPath</key>
  <string>{install.LogsPath}</string>
  <key>StandardErrorPath</key>
  <string>{install.LogsPath}</string>
  <key>EnvironmentVariables</key>
  <dict>
{envXml}
  </dict>
</dict>
</plist>
""";
    }

    private static Dictionary<string, string> ParseEnvFile(string path)
    {
        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }


}
