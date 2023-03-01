using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VS2022Net4ConsoleApp
{
    internal class Program
    {
        const string VS2019ReleaseChannelUrl = "https://aka.ms/vs/16/release/channel";
        static readonly string[] TargetingPackIds = new[] { "Microsoft.Net.4.TargetingPack", "Microsoft.Net.4.5.TargetingPack", "Microsoft.Net.4.5.1.TargetingPack", "Microsoft.Net.4.5.2.TargetingPack" };

        static HttpClient httpClient = new();

        static async Task Main()
        {
            var baseDir = Guid.NewGuid().ToString("N");

            // 1.下载所需的组件
            var needInstallFilePathList = await DownloadNeedtargetingPackAsync(baseDir);


            // 2.安装所需的组件
            await InstallFilesAsync(baseDir, needInstallFilePathList);


            // 3.清理临时文件
            Directory.Delete(baseDir, true);

            Console.Write("处理完成！按任意键结束！");
            Console.ReadKey();
        }

        private static async Task<IEnumerable<string>> DownloadNeedtargetingPackAsync(string baseDir)
        {
            var vsmanUrl = await GetVSmanUrlAsync(VS2019ReleaseChannelUrl);
            var targetingPacks = await GetNet4TargetingPacks(vsmanUrl);

            Directory.CreateDirectory(baseDir);

            List<string> needInstallFilePathList = new();
            Console.WriteLine("开始下载所需组件！");
            List<Task> taskList = new();
            foreach (var targetingPack in targetingPacks)
            {
                var id = targetingPack.GetProperty("id").GetString()!;
                foreach (var item in targetingPack.GetProperty("payloads").EnumerateArray())
                {
                    var fileName = item.GetProperty("fileName").GetString()!;
                    var url = item.GetProperty("url").GetString()!;

                    // 创建文件夹
                    var dir = Path.Combine(baseDir, id);
                    Directory.CreateDirectory(dir);

                    var filePath = Path.Combine(dir, fileName);

                    taskList.Add(DownloadFileWriteToPathAsync(url, filePath));

                    if (fileName!.EndsWith(".msi")) needInstallFilePathList.Add(filePath);
                }
                Console.WriteLine($"{id}：下载完成！");
            }
            await Task.WhenAll(taskList.ToArray());

            return needInstallFilePathList;
        }

        private static async Task<string> GetVSmanUrlAsync(string url)
        {
            Console.WriteLine($"下载：{url}");
            var json = await httpClient.GetStringAsync(url);
            var jDoc = JsonDocument.Parse(json);
            var channelItemsManifest = jDoc.RootElement.GetProperty("channelItems").EnumerateArray()
                .First(x => x.GetProperty("type").GetString() == "Manifest");

            var vsmanUrl = channelItemsManifest.GetProperty("payloads")[0].GetProperty("url").GetString();

            return vsmanUrl!;
        }

        private static async Task<IEnumerable<JsonElement>> GetNet4TargetingPacks(string vsmanUrl)
        {
            Console.WriteLine($"下载：{vsmanUrl}");
            var vsmanJson = await httpClient.GetStringAsync(vsmanUrl);

            var jDoc = JsonDocument.Parse(vsmanJson);
            var targetingPacks = jDoc.RootElement.GetProperty("packages").EnumerateArray()
                .Where(x => TargetingPackIds.Contains(x.GetProperty("id").GetString()));

            return targetingPacks;
        }

        private static async Task DownloadFileWriteToPathAsync(string url, string filePath)
        {
            var fileContent = await httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, fileContent);
        }


        private static async Task InstallFilesAsync(string baseDir, IEnumerable<string> needFilePathList)
        {
            List<string> cmdList = new();
            foreach (var item in needFilePathList)
            {
                cmdList.Add($"%~dp0{item.Substring(baseDir.Length + 1)} MSIFASTINSTALL=7 EXTUI=1 /qn");
            }

            var cmdFilePath = Path.Combine(baseDir, "InstallAll.cmd");
            await File.WriteAllLinesAsync(cmdFilePath, cmdList);

            Console.WriteLine("开始安装所需组件！");
            var cmdProcess = Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cmdFilePath));
            cmdProcess.WaitForExit();
            Console.WriteLine("组件安装成功！");
        }

    }
}