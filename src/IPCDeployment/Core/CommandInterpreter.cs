using System;
using System.IO;

namespace IPCDeployment.Core
{
    public static class CommandInterpreter
    {
        public static string CmdPath
        {
            get { return SystemTools.Get("cmd.exe"); }
        }

        public static string PowerShellPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
            }
        }
    }
}
