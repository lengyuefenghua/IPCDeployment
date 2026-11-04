using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace IPCDeployment
{
    /// <summary>
    /// 从 exe 内嵌资源加载托管依赖。CLR 在磁盘上找不到程序集时会触发 <see cref="AppDomain.AssemblyResolve"/>，
    /// 由这里按简单名从清单资源取回，避免同一程序集出现多份副本。
    /// </summary>
    public static class EmbeddedAssemblyLoader
    {
        private const string ResourcePrefix = "IPCDeployment.Resources.";

        private static readonly ConcurrentDictionary<string, Assembly> Cache =
            new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册解析回调；应在任何内嵌依赖的类型被使用之前调用。</summary>
        public static void Attach()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        /// <summary>优先返回已加载的同名程序集，其次从内嵌资源加载；找不到返回 null。</summary>
        public static Assembly Load(string simpleName)
        {
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            Assembly cached;
            if (Cache.TryGetValue(simpleName, out cached))
            {
                return cached;
            }

            Assembly assembly = LoadFromResource(simpleName);
            if (assembly != null)
            {
                Cache.TryAdd(simpleName, assembly);
            }

            return assembly;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                if (string.IsNullOrEmpty(args.Name))
                {
                    return null;
                }

                return Load(new AssemblyName(args.Name).Name);
            }
            catch (Exception)
            {
                // 交给 CLR 抛原始加载异常，保留诊断信息。
                return null;
            }
        }

        private static Assembly LoadFromResource(string simpleName)
        {
            string resource = ResourcePrefix + simpleName + ".dll";
            using (Stream stream = typeof(EmbeddedAssemblyLoader).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return Assembly.Load(buffer);
            }
        }
    }
}
