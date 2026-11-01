using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VisionIPC.Deployment.Core
{
    public static class LogRedaction
    {
        private static readonly Regex SecretPattern = new Regex(
            @"(?i)(password|pwd|token|secret)\s*=\s*[^;\s]+",
            RegexOptions.Compiled);

        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }
            return SecretPattern.Replace(value, "$1=***");
        }
    }

    public sealed class DeploymentLog
    {
        private readonly object sync = new object();
        private readonly List<string> lines = new List<string>();
        private string logPath;

        public bool HasWriteFailure { get; private set; }
        public bool UsedFallback { get; private set; }

        public DeploymentLog(string packageRoot, string logDirectory)
        {
            var paths = new PathPolicy();
            var primary = paths.ResolveLogDirectory(packageRoot, logDirectory);
            var primaryError = string.Empty;

            try
            {
                EnsureWritableDirectory(primary);
                logPath = Path.Combine(primary, "deploy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
            }
            catch (Exception exception)
            {
                primaryError = exception.Message;
                UsedFallback = true;
            }

            if (logPath == null)
            {
                try
                {
                    var fallback = Path.Combine(Path.GetTempPath(), "VisionIPC-Deployment", "logs");
                    EnsureWritableDirectory(fallback);
                    logPath = Path.Combine(fallback, "deploy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                }
                catch (Exception)
                {
                    HasWriteFailure = true;
                }
            }

            if (UsedFallback && !HasWriteFailure)
            {
                Write("WARN", string.Empty, "主日志目录不可写，已回退到临时目录：" + primaryError);
            }
        }

        public string LogPath
        {
            get { return logPath; }
        }

        public IList<string> Lines
        {
            get
            {
                lock (sync)
                {
                    return new List<string>(lines);
                }
            }
        }

        public string Write(string level, string operationId, string message)
        {
            var safeMessage = LogRedaction.Redact(message ?? string.Empty);
            if (safeMessage.Length > 4000)
            {
                safeMessage = safeMessage.Substring(0, 4000) + "...";
            }

            var prefix = DateTime.Now.ToString("HH:mm:ss") + " [" + (string.IsNullOrWhiteSpace(level) ? "INFO" : level) + "]";
            if (!string.IsNullOrWhiteSpace(operationId))
            {
                prefix += " " + operationId;
            }
            var line = prefix + " " + safeMessage;

            lock (sync)
            {
                lines.Add(line);
                if (lines.Count > 20000)
                {
                    lines.RemoveAt(0);
                }

                if (!string.IsNullOrEmpty(logPath))
                {
                    try
                    {
                        File.AppendAllText(logPath, line + Environment.NewLine);
                    }
                    catch (Exception)
                    {
                        HasWriteFailure = true;
                    }
                }
                else
                {
                    HasWriteFailure = true;
                }
            }
            return line;
        }

        private static void EnsureWritableDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
    }
}
