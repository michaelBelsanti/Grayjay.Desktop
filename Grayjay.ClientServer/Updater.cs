﻿using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grayjay.ClientServer
{
    public static class Updater
    {
        private static string _startupArgs = "";
        public static void SetStartupArguments(string args)
        {
            _startupArgs = args;
        }
        private static string GetStartupArguments()
        {
            return _startupArgs;
        }

        public static string GetSelfExecutablePath()
        {
            if (OperatingSystem.IsWindows())
                return Path.GetFileName("Grayjay.exe");
            else if (OperatingSystem.IsLinux())
                return Path.GetFileName("Grayjay");
            else if (OperatingSystem.IsMacOS())
                return Path.GetFileName("../Grayjay.Desktop.app");
            else throw new NotImplementedException();
        }

        public static string GetUpdaterExecutableName()
        {
            if (OperatingSystem.IsWindows())
                return "FUTO.Updater.Client.exe";
            else if (OperatingSystem.IsLinux())
                return "FUTO.Updater.Client";
            else
                return null;
        }
        public static string GetUpdaterExecutablePath()
        {
            string fileName = null;
            if (OperatingSystem.IsWindows())
                fileName = "FUTO.Updater.Client.exe";
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                fileName = "FUTO.Updater.Client";
            else
                throw new NotImplementedException();

            if (File.Exists(fileName))
            {
                return Path.GetFullPath(fileName);
            }
            else
            {
                return null;
            }
        }

        public static string GetUpdaterConfigPath()
        {
            string fileName = "UpdaterConfig.json";

            if (File.Exists(fileName))
            {
                return Path.GetFullPath(fileName);
            }
            else
            {
                return null;
            }
        }
        public class UpdaterConfig
        {
            public string Server { get; set; }
            public int Version { get; set; }

            public bool HasValidServer => !string.IsNullOrEmpty(Server);
        }
        public static UpdaterConfig GetUpdaterConfig()
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<UpdaterConfig>(File.ReadAllText(GetUpdaterConfigPath()));
            }
            catch(Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get updater config", ex);
                return null;
            }
        }
        public static int GetUpdaterVersion()
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                return 1;
            if (File.Exists(executable))
            {
                try
                {
                    //Old updater doesn't support version yet..
                    if (Constants.App.Version <= 4)
                        return 1;
                    
                    var proc = Process.Start(new ProcessStartInfo()
                    {
                        FileName = executable,
                        Arguments = "version",
                        RedirectStandardOutput = true
                    });
                    while (!proc.StandardOutput.EndOfStream)
                    {
                        var line = proc.StandardOutput.ReadLine();
                        Console.WriteLine(line);
                    }
                    proc.WaitForExit();
                    return Math.Max(1, proc.ExitCode);
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Failed to read updater version, assuming 1");
                    return 1;
                }
            }
            return 1;
        }

        public static void Update(int[] processIds, int version = -1)
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            if (OperatingSystem.IsLinux())
            {
                var toRun = GetLinuxShell($"{executable} update -process_ids {string.Join(",", processIds)} -executable \"{GetSelfExecutablePath()}\"");
                Process.Start(toRun);
            }
            else
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = executable,
                    Arguments = $"update -process_ids {string.Join(",", processIds)} -executable \"{GetSelfExecutablePath()}\"",// + 
                      //    (string.IsNullOrWhiteSpace(_startupArgs) ? "" : " -executable_args " + JsonConvert.SerializeObject(GetStartupArgumentsEscaped())),
                    UseShellExecute = true
                });
            }
        }
        public static void UpdateSelf()
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            var process = Process.Start(new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = $"updateself",
                UseShellExecute = false
            });
            process.WaitForExit();
            Thread.Sleep(5000);

            if (OperatingSystem.IsLinux())
            {
                //Just in case
                try
                {
                    Process chmod = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "chmod",
                            Arguments = "-R u=rwx " + executable,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    chmod.Start();
                    while (!chmod.StandardOutput.EndOfStream)
                    {
                        var line = chmod.StandardOutput.ReadLine();
                        Console.WriteLine(line);
                    }
                    chmod.WaitForExit();
                }
                catch (Exception ex) { }
            }
        }

        private static Regex REGEX_UPDATER_VERSION = new Regex("FUTO Updater v([0-9]+)");
        public static (bool, int) HasUpdate()
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            var proc = Process.Start(new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = "check",
                RedirectStandardOutput = true
            });
            int updaterVersion = -1;
            while (!proc.StandardOutput.EndOfStream)
            {
                var line = proc.StandardOutput.ReadLine();
                Match m = REGEX_UPDATER_VERSION.Match(line);
                if (m.Success && m.Groups.Count > 1)
                    updaterVersion = int.Parse(m.Groups[1].Value);

                Console.WriteLine(line);
            }
            proc.WaitForExit();
            switch (proc.ExitCode)
            {
                case 1:
                    return (true, updaterVersion);
                case 2:
                    return (false, updaterVersion);
                default:
                    return (false, updaterVersion);
            }
        }

        public static int GetTargetVersion()
        {
            try
            {
                var config = GetUpdaterConfig();
                if (config == null || string.IsNullOrEmpty(config.Server))
                    return -1;
                if (config.Version > 0)
                    return config.Version;
                using(WebClient client = new WebClient())
                    return int.Parse(client.DownloadString(config.Server + "/VersionLast.json"));
            }
            catch(Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get last version", ex);
                return -1;
            }
        }
        public class Changelog
        {
            public int Version { get; set; }
            public string Server { get; set; }
            public string Platform { get; set; }
            public string Text { get; set; }

            public Changelog(int version, string text)
            {
                Version = version;
                Text = text;
            }
        }
        public static Changelog GetTargetChangelog()
        {
            try
            {
                var config = GetUpdaterConfig();
                if (config == null || !config.HasValidServer)
                    return null;
                var targetVersion = GetTargetVersion();
                if (targetVersion <= 0)
                    return null;

                string targetPlatform = StateApp.GetPlatformName();

                using(WebClient client = new WebClient())
                {
                    return new Changelog(targetVersion, client.DownloadString(config.Server + $"/{targetVersion}/{targetPlatform}/Changelogs/{targetVersion}.txt"))
                    {
                        Server = config.Server,
                        Platform = targetPlatform
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get changelog", ex);
                return null;
            }
        }
        public static int GetTargetUpdaterVersion(string server, int version, string dist)
        {
            using (WebClient client = new WebClient())
            {
                try
                {
                    return JsonSerializer.Deserialize<int>(client.DownloadString(server + $"/{version}/{dist}/UpdaterVersion.json"));
                }
                catch(Exception ex)
                {
                    return 1;
                }
            }
        }
        public static string GetUpdaterUrl(string server, int version, string dist)
        {
            var updaterName = GetUpdaterExecutableName();
            if (string.IsNullOrEmpty(updaterName))
                return null;
            return server + $"/{version}/{dist}/" + updaterName;
        }


        public static void RebootTest(int[] processIds, int version = -1)
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            Process.Start(new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = $"reboot -process_ids {string.Join(",", processIds)} -executable \"{GetSelfExecutablePath()}\"" +
                  (string.IsNullOrWhiteSpace(_startupArgs) ? "" : " -executable_args " + "BASE64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(GetStartupArguments()))),
                UseShellExecute = true
            });
        }

        public static ProcessStartInfo GetLinuxShell(string cmd)
        {
            string[] eTerminals = new string[]
            {
                "x-terminal-emulator",
                "gnome-terminal",
                "konsole",
                "xfce4-terminal",
                "rxvt",
                "xterm"
            };
            string[] cTerminals = new string[]
            {
                "lxterminal",
            };
            string[] supportedTerminals = new string[]
            {
            };
            string[] allTerminals = eTerminals.Concat(cTerminals).Concat(supportedTerminals).ToArray();
            string selectedTerminal = null;
            foreach (var terminal in allTerminals)
                if (LinuxCommandExists(terminal))
                {
                    selectedTerminal = terminal;
                    break;
                }
            if (selectedTerminal == null)
                return null;

            if (eTerminals.Contains(selectedTerminal))
                return new ProcessStartInfo()
                {
                    FileName = selectedTerminal,
                    Arguments = $"-e \"{cmd.Replace("\"", "\\\"")}\"",
                    UseShellExecute = true,
                };
            else if(cTerminals.Contains(selectedTerminal))
                return new ProcessStartInfo()
                {
                    FileName = selectedTerminal,
                    Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                    UseShellExecute = true,
                };


            return null;
        }
        private static bool LinuxCommandExists(string command)
        {
            var result = Process.Start($"command -v {command}");
            result.WaitForExit();
            return result.ExitCode == 1;
        }
    }
}
