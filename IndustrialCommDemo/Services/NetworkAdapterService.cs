using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;

namespace IndustrialCommDemo.Services
{
    /// <summary>供网卡设置页面显示和修改的网络适配器快照。</summary>
    internal sealed class NetworkAdapterInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IpAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }
        public string DnsServers { get; set; }
        public string MacAddress { get; set; }
        public bool IsDhcpEnabled { get; set; }
        public bool CanConfigure { get; set; }

        public string DisplayName
        {
            get
            {
                return string.Format("{0}  ({1}){2}",
                    Name,
                    string.IsNullOrWhiteSpace(IpAddress) ? "无 IPv4" : IpAddress,
                    CanConfigure ? string.Empty : "  [只读]");
            }
        }
    }

    /// <summary>封装 Windows 网卡枚举、静态地址和 DHCP 配置操作。</summary>
    internal static class NetworkAdapterService
    {
        public static IReadOnlyList<NetworkAdapterInfo> GetAdapters()
        {
            var result = new List<NetworkAdapterInfo>();
            var configurableIds = GetConfigurableAdapterIds();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                               item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var properties = adapter.GetIPProperties();
                    var ipv4 = properties.UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork);
                    var gateway = properties.GatewayAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork);
                    result.Add(new NetworkAdapterInfo
                    {
                        Id = adapter.Id,
                        Name = adapter.Name,
                        Description = adapter.Description,
                        IpAddress = ipv4 == null ? string.Empty : ipv4.Address.ToString(),
                        SubnetMask = ipv4 == null || ipv4.IPv4Mask == null ? string.Empty : ipv4.IPv4Mask.ToString(),
                        Gateway = gateway == null ? string.Empty : gateway.Address.ToString(),
                        DnsServers = string.Join(", ", properties.DnsAddresses
                            .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                            .Select(item => item.ToString())),
                        MacAddress = FormatMacAddress(adapter.GetPhysicalAddress()),
                        IsDhcpEnabled = properties.GetIPv4Properties() != null && properties.GetIPv4Properties().IsDhcpEnabled,
                        CanConfigure = configurableIds.Contains(NormalizeAdapterId(adapter.Id)),
                    });
                }
                catch
                {
                    // 某些虚拟或正在卸载的网卡可能无法读取属性，不影响其他网卡显示。
                }
            }
            return result
                .OrderByDescending(item => item.CanConfigure)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static void ApplyStatic(string adapterName, string ipAddress, string subnetMask, string gateway, string dnsText)
        {
            EnsureAdministrator();
            ValidateAdapterName(adapterName);
            var ip = ParseIpv4(ipAddress, "IP 地址", false);
            var mask = ParseIpv4(subnetMask, "子网掩码", false);
            var gatewayValue = ParseIpv4(gateway, "默认网关", true);
            var dnsServers = SplitAddresses(dnsText, "DNS");

            RunNetsh("interface", "ipv4", "set", "address",
                "name=" + adapterName,
                "source=static",
                "address=" + ip,
                "mask=" + mask,
                "gateway=" + (string.IsNullOrEmpty(gatewayValue) ? "none" : gatewayValue),
                "store=persistent");

            if (dnsServers.Length > 0)
            {
                RunNetsh("interface", "ipv4", "set", "dnsservers",
                    "name=" + adapterName,
                    "source=static",
                    "address=" + dnsServers[0],
                    "register=primary",
                    "validate=no");
                for (var index = 1; index < dnsServers.Length; index++)
                {
                    RunNetsh("interface", "ipv4", "add", "dnsservers",
                        "name=" + adapterName,
                        "address=" + dnsServers[index],
                        "index=" + (index + 1),
                        "validate=no");
                }
            }
        }

        public static void EnableDhcp(string adapterName)
        {
            EnsureAdministrator();
            ValidateAdapterName(adapterName);
            RunNetsh("interface", "ipv4", "set", "address", "name=" + adapterName, "source=dhcp");
            RunNetsh("interface", "ipv4", "set", "dnsservers", "name=" + adapterName, "source=dhcp");
        }

        private static void RunNetsh(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("无法启动 Windows 网络配置工具。 ");
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("修改网卡失败：" +
                        (string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim()));
            }
        }

        private static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t' }) < 0) return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void ValidateAdapterName(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) throw new ArgumentException("请选择网卡。", nameof(adapterName));
            if (adapterName.IndexOfAny(new[] { '\"', '\r', '\n' }) >= 0)
                throw new ArgumentException("网卡名称包含不支持的字符。", nameof(adapterName));
        }

        private static HashSet<string> GetConfigurableAdapterIds()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT GUID, PhysicalAdapter FROM Win32_NetworkAdapter WHERE GUID IS NOT NULL"))
                using (var adapters = searcher.Get())
                {
                    foreach (ManagementObject adapter in adapters)
                    {
                        using (adapter)
                        {
                            if (Convert.ToBoolean(adapter["PhysicalAdapter"]))
                                result.Add(NormalizeAdapterId(Convert.ToString(adapter["GUID"])));
                        }
                    }
                }
            }
            catch
            {
                // 无法读取 WMI 能力时保持只读，避免对未知接口执行系统配置操作。
            }
            return result;
        }

        private static string NormalizeAdapterId(string value)
        {
            return (value ?? string.Empty).Trim().Trim('{', '}');
        }

        private static string[] SplitAddresses(string text, string fieldName)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => ParseIpv4(item, fieldName, false))
                .ToArray();
        }

        private static string ParseIpv4(string text, string fieldName, bool allowEmpty)
        {
            var value = (text ?? string.Empty).Trim();
            if (allowEmpty && value.Length == 0) return string.Empty;
            IPAddress address;
            if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException(fieldName + "格式无效。");
            return address.ToString();
        }

        private static string FormatMacAddress(PhysicalAddress address)
        {
            var bytes = address == null ? new byte[0] : address.GetAddressBytes();
            return string.Join("-", bytes.Select(item => item.ToString("X2")));
        }

        private static void EnsureAdministrator()
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("修改网卡需要管理员权限，请右键以管理员身份运行程序。");
        }
    }
}
