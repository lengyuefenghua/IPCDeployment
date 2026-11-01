using System;
using System.IO;

namespace VisionIPC.Deployment.Core
{
    public static class SystemTools
    {
        public static string Get(string fileName)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), fileName);
        }
    }
}
