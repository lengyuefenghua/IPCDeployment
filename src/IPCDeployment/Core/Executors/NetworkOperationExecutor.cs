using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace IPCDeployment.Core
{
    public sealed class NetworkOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Network"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var network = operation as NetworkOperation;
            if (network == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var adapter = AdapterUtility.Find(network.Adapter);
                if (adapter == null)
                {
                    return OperationResult.Failed(network.Id, "未找到网卡：" + network.Adapter + "。");
                }

                if (network.Kind == NetworkOperationKind.AdvancedProperty)
                {
                    return SetAdvancedProperty(network, adapter, context);
                }
                return await SetIpv4Async(network, adapter, context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Cancelled(network.Id, "网络操作已取消。");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Failed(network.Id, "没有修改网卡设置的权限。");
            }
            catch (System.Security.SecurityException)
            {
                return OperationResult.Failed(network.Id, "网卡设置被安全策略拒绝。");
            }
            catch (Exception exception)
            {
                return OperationResult.Failed(network.Id, "网络操作失败：" + exception.Message);
            }
        }

        private static OperationResult SetAdvancedProperty(NetworkOperation network, NetworkInterface adapter, OperationContext context)
        {
            // 驱动项按网卡 GUID 定位，不能依赖会变化的驱动编号。
            var keyPath = AdapterUtility.FindDriverKey(adapter.Id);
            if (string.IsNullOrEmpty(keyPath))
            {
                return OperationResult.Failed(network.Id, "未找到网卡的驱动设置项。");
            }

            using (var key = Registry.LocalMachine.OpenSubKey(keyPath, true))
            {
                if (key == null)
                {
                    return OperationResult.Failed(network.Id, "无法打开网卡驱动设置项。");
                }

                var valueType = network.ValueType ?? RegistryValueType.String;
                var current = RegistryAccess.ReadValue(key, network.Keyword);
                context.Log("INFO", "网卡写前值 " + network.Keyword + "=" + Format(current));
                if (RegistryAccess.ValuesEqual(current, network.Value, valueType))
                {
                    return OperationResult.AlreadySatisfied(network.Id, "网卡属性已等于目标值，已满足。");
                }

                if (network.ExpectedBefore != null &&
                    !RegistryAccess.ValuesEqual(current, network.ExpectedBefore, valueType))
                {
                    return OperationResult.Failed(network.Id,
                        "网卡写前值不匹配；当前=" + Format(current) + "，期望=" + network.ExpectedBefore + "。");
                }

                RegistryAccess.SetValue(key, network.Keyword, network.Value, valueType);
                var after = RegistryAccess.ReadValue(key, network.Keyword);
                if (!RegistryAccess.ValuesEqual(after, network.Value, valueType))
                {
                    return OperationResult.Failed(network.Id, "网卡属性写入后回读不一致；当前=" + Format(after) + "。");
                }

                context.Log("INFO", "网卡写后回读 " + network.Keyword + "=" + Format(after));
                return OperationResult.Succeeded(network.Id, "网卡属性已写入并回读一致；部分驱动需重启后生效。", true);
            }
        }

        private static async Task<OperationResult> SetIpv4Async(NetworkOperation network, NetworkInterface adapter,
            OperationContext context, CancellationToken cancellationToken)
        {
            if (adapter.Name.IndexOf('"') >= 0)
            {
                return OperationResult.Failed(network.Id, "网卡名称包含不受支持的引号。");
            }

            var before = ReadIpv4State(adapter);
            if (before == null)
            {
                return OperationResult.Failed(network.Id, "无法读取网卡当前 IPv4 配置。");
            }
            context.Log("INFO", "IPv4 写前状态：" + before.ToDisplayString());
            if (Matches(network, before))
            {
                return OperationResult.AlreadySatisfied(network.Id, "IPv4 配置已满足目标。");
            }

            if (network.IpMode == NetworkIpMode.Static)
            {
                var mask = AdapterUtility.PrefixToMask(network.PrefixLength.Value);
                var setAddress = "interface ipv4 set address name=\"" + adapter.Name + "\" static " +
                    network.Address + " " + mask +
                    (string.IsNullOrWhiteSpace(network.Gateway) ? string.Empty : " " + network.Gateway);
                var failure = await RunNetshAsync(network, context, setAddress, cancellationToken);
                if (failure != null)
                {
                    return failure;
                }

                if (!string.IsNullOrWhiteSpace(network.Dns))
                {
                    var dns = network.Dns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (dns.Length > 0)
                    {
                        var first = "interface ipv4 set dnsservers name=\"" + adapter.Name + "\" static " + dns[0] + " primary";
                        failure = await RunNetshAsync(network, context, first, cancellationToken);
                        if (failure != null)
                        {
                            return failure;
                        }
                    }

                    for (var index = 1; index < dns.Length; index++)
                    {
                        var next = "interface ipv4 add dnsservers name=\"" + adapter.Name + "\" " + dns[index] +
                            " index=" + (index + 1);
                        failure = await RunNetshAsync(network, context, next, cancellationToken);
                        if (failure != null)
                        {
                            return failure;
                        }
                    }
                }
            }
            else
            {
                var setDhcp = "interface ipv4 set address name=\"" + adapter.Name + "\" source=dhcp";
                var failure = await RunNetshAsync(network, context, setDhcp, cancellationToken);
                if (failure != null)
                {
                    return failure;
                }

                var setDnsDhcp = "interface ipv4 set dnsservers name=\"" + adapter.Name + "\" source=dhcp";
                failure = await RunNetshAsync(network, context, setDnsDhcp, cancellationToken);
                if (failure != null)
                {
                    return failure;
                }
            }

            await Task.Delay(500, cancellationToken);
            var after = ReadIpv4State(adapter);
            context.Log("INFO", "IPv4 写后状态：" + (after == null ? "无法读取" : after.ToDisplayString()));
            if (after == null || !Matches(network, after))
            {
                return OperationResult.Failed(network.Id, "IPv4 写入后回读不一致。");
            }

            return OperationResult.Succeeded(network.Id, "IPv4 已应用并回读一致。", false);
        }

        private static async Task<OperationResult> RunNetshAsync(NetworkOperation network, OperationContext context,
            string arguments, CancellationToken cancellationToken)
        {
            context.Log("INFO", "netsh " + arguments);
            var result = await context.ProcessRunner.RunAsync(new ProcessRequest
            {
                FileName = SystemTools.Get("netsh.exe"),
                Arguments = arguments,
                TimeoutMinutes = 2
            }, line => context.Log("INFO", line), cancellationToken);

            if (result.Cancelled)
            {
                return OperationResult.Cancelled(network.Id, ExecutorUtility.SummarizeProcess(result));
            }
            if (result.TimedOut || result.FailedToStart || result.ExitCode != 0)
            {
                return OperationResult.Failed(network.Id, "netsh 执行失败：" + ExecutorUtility.SummarizeProcess(result));
            }
            return null;
        }

        private static Ipv4State ReadIpv4State(NetworkInterface adapter)
        {
            var properties = adapter.GetIPProperties().GetIPv4Properties();
            if (properties == null)
            {
                return null;
            }

            var query = "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE InterfaceIndex = " + properties.Index;
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var dhcpValue = item["DHCPEnabled"];
                        var state = new Ipv4State
                        {
                            DhcpEnabled = dhcpValue != null && Convert.ToBoolean(dhcpValue, CultureInfo.InvariantCulture)
                        };
                        state.Addresses = FilterIPv4(item["IPAddress"] as string[]);
                        state.Subnets = FilterIPv4(item["IPSubnet"] as string[]);
                        state.Gateways = FilterIPv4(item["DefaultIPGateway"] as string[]);
                        state.Dns = FilterIPv4(item["DNSServerSearchOrder"] as string[]);
                        return state;
                    }
                }
            }
            return null;
        }

        private static List<string> FilterIPv4(string[] values)
        {
            var result = new List<string>();
            if (values == null)
            {
                return result;
            }
            foreach (var value in values)
            {
                IPAddress address;
                if (IPAddress.TryParse(value, out address) && address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add(value);
                }
            }
            return result;
        }

        private static bool Matches(NetworkOperation network, Ipv4State state)
        {
            if (network.IpMode == NetworkIpMode.Dhcp)
            {
                return state.DhcpEnabled;
            }

            if (state.DhcpEnabled)
            {
                return false;
            }
            if (!state.Addresses.Any(a => string.Equals(a, network.Address, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var mask = AdapterUtility.PrefixToMask(network.PrefixLength.Value);
            if (!state.Subnets.Any(s => string.Equals(s, mask, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(network.Gateway) &&
                !state.Gateways.Any(g => string.Equals(g, network.Gateway, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(network.Dns))
            {
                var expected = network.Dns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (!expected.SequenceEqual(state.Dns, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Format(object value)
        {
            return value == null ? "不存在" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private sealed class Ipv4State
        {
            public bool DhcpEnabled { get; set; }
            public List<string> Addresses { get; set; }
            public List<string> Subnets { get; set; }
            public List<string> Gateways { get; set; }
            public List<string> Dns { get; set; }

            public Ipv4State()
            {
                Addresses = new List<string>();
                Subnets = new List<string>();
                Gateways = new List<string>();
                Dns = new List<string>();
            }

            public string ToDisplayString()
            {
                return "DHCP=" + DhcpEnabled +
                    "；地址=" + string.Join(",", Addresses) +
                    "；掩码=" + string.Join(",", Subnets) +
                    "；网关=" + string.Join(",", Gateways) +
                    "；DNS=" + string.Join(",", Dns);
            }
        }
    }
}
