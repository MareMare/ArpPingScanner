// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="MareMare">
// Copyright © 2024 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

// コマンドライン引数を定義します。
var cidrArgument = new Argument<string>(
    name: "cidr",
    description: "スキャン対象のCIDR (例: 192.168.10.0/24)",
    getDefaultValue: () => "192.168.10.0/24");
var outputOption = new Option<string?>(
    name: "--output",
    description: "出力先CSVファイルパス (省略可能)");
outputOption.AddAlias("-o");

// ルートコマンドを作成します。
var rootCommand = new RootCommand(description: "指定したCIDR範囲のネットワークスキャンを実行し、結果を表示またはCSVに出力します。")
{
    cidrArgument,
    outputOption,
};

// コマンドのハンドラーを設定します。
rootCommand.SetHandler(
    async (cidr, output) =>
    {
        var ipList = GetIpRange(cidr).ToList();

        var startTime = DateTimeOffset.Now;
        Console.WriteLine($"[{startTime:HH:mm:ss}] Start Scanning network [{cidr}]...");

        // 並列実行数を制限して結果を収集します。
        var maxDegreeOfParallelism = 100; // 同時実行タスク数の上限
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        var results = await ProcessScanningAsync(semaphore, ipList).ConfigureAwait(false);

        var finishTime = DateTimeOffset.Now;
        Console.WriteLine(
            $"[{finishTime:HH:mm:ss}] Completed Scanning network [{cidr}]... {(finishTime - startTime).TotalMilliseconds:n0}[ms]");
        Console.WriteLine();

        // 到達可能なホストのみを抽出します。
        var filteredResults = results.Where(result => result.IsReachable).ToArray();

        // 結果をコンソールへ出力します。
        foreach (var result in filteredResults)
        {
            Console.WriteLine(
                $"{result.Ip,-15}\t{result.HostName,-30}\t{result.MacAddress,-20}\t{(result.IsReachable ? "Reachable" : "Unreachable")}");
        }

        // 結果をCSVに出力する場合
        if (!string.IsNullOrEmpty(output))
        {
            var csvLines = filteredResults
                .Select(
                    result =>
                        $"{result.Ip},{result.HostName},{result.MacAddress},{result.IsReachable}");
            await File.WriteAllLinesAsync(output, csvLines).ConfigureAwait(false);

            Console.WriteLine($"結果をCSVファイルに出力しました: {output}");
        }
    },
    cidrArgument,
    outputOption);

// コマンドを実行
return await rootCommand.InvokeAsync(args).ConfigureAwait(false);

async Task<List<HostInfo>> ProcessScanningAsync(
    SemaphoreSlim parallelismLimiter,
    IEnumerable<string> ipStringList)
{
    var tasks = ipStringList.Select(
        async ip =>
        {
            await parallelismLimiter.WaitAsync().ConfigureAwait(false);
            try
            {
                return await ScanHostAsync(ip).ConfigureAwait(false);
            }
            finally
            {
                parallelismLimiter.Release();
            }
        });
    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
    return results.ToList();
}

async Task<HostInfo> ScanHostAsync(string ip)
{
    var isReachable = await PingHostAsync(ip).ConfigureAwait(false);
    var hostName = isReachable ? await GetHostNameAsync(ip).ConfigureAwait(false) : "N/A";
    var macAddress = isReachable ? GetMacAddress(ip) : "N/A";

    return new HostInfo(ip, hostName, macAddress, isReachable);
}

IEnumerable<string> GetIpRange(string ipRangeString)
{
    var cidrRegex = new Regex(
        @"^(?<adr>([\d.]+)|([\da-f:]+(:[\d.]+)?(%\w+)?))[ \t]*/[ \t]*(?<maskLen>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // CIDR 形式の検証
    var match = cidrRegex.Match(ipRangeString);
    if (!match.Success)
    {
        throw new ArgumentException("Invalid CIDR format.", nameof(ipRangeString));
    }

    // アドレスとプレフィックス長を取得
    var baseAddress = IPAddress.Parse(match.Groups["adr"].Value);
    var prefixLength = int.Parse(match.Groups["maskLen"].Value, CultureInfo.InvariantCulture);
    if (baseAddress.AddressFamily != AddressFamily.InterNetwork)
    {
        throw new NotSupportedException("Only IPv4 is supported in this implementation.");
    }

    // IPv4 アドレスの範囲を計算
    var ipv4 = BitConverter.ToUInt32(baseAddress.GetAddressBytes().Reverse().ToArray(), 0);
    var mask = 0xFFFFFFFF << (32 - prefixLength);
    var startIp = ipv4 & mask; // ネットワークアドレス
    var endIp = startIp | ~mask; // ブロードキャストアドレス
    var ips = new List<string>();

    // ネットワークアドレスからブロードキャストアドレスまで含む
    for (var currentIp = startIp; currentIp <= endIp; currentIp++)
    {
        var ip = new IPAddress(BitConverter.GetBytes(currentIp).Reverse().ToArray());
        ips.Add($"{ip}");
    }

    return ips;
}

// Ping を使って到達可否を確認
async Task<bool> PingHostAsync(string ipv4)
{
    using var ping = new Ping();
    try
    {
        var reply = await ping.SendPingAsync(ipv4, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        return reply.Status == IPStatus.Success;
    }
#pragma warning disable CA1031
    catch
#pragma warning restore CA1031
    {
        // 例外を握りつぶします。
        return false;
    }
}

// 非同期でホスト名を取得
async Task<string> GetHostNameAsync(string ipv4)
{
    try
    {
        var hostEntry = await Dns.GetHostEntryAsync(ipv4).ConfigureAwait(false);
        return hostEntry.HostName;
    }
#pragma warning disable CA1031
    catch
#pragma warning restore CA1031
    {
        // 例外を握りつぶします。
        return "Unknown";
    }
}

// `arp -a` を使って MAC アドレスを取得
string GetMacAddress(string ipv4)
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c arp -a {ipv4}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var match = Regex.Match(output, $@"\b{Regex.Escape(ipv4)}\s+([0-9A-Fa-f-]+)\b");
        return match.Success ? match.Groups[1].Value : "Not Found";
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
        // 例外を握りつぶします。
        return $"Error: {ex.Message}";
    }
}

/// <summary>
/// ネットワークホストに関する情報を表します。IPアドレス、ホスト名、MACアドレス、および到達可能性の状態を含みます。
/// </summary>
/// <param name="Ip">IPアドレス。</param>
/// <param name="HostName">ホスト名。</param>
/// <param name="MacAddress">MACアドレス。</param>
/// <param name="IsReachable">到達可能かどうか。到達可能な場合 <c>true</c>。それ以外は <c>false</c>。</param>
#pragma warning disable SA1649
internal sealed record HostInfo(string Ip, string HostName, string MacAddress, bool IsReachable);
#pragma warning restore SA1649
