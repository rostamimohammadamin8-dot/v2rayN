using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ServiceLib.Handler;
using ServiceLib.Helper;
using ServiceLib.Models.Entities;
using ServiceLib.Models;

namespace ServiceLib.Extensions
{
    /// <summary>
    /// مدیریت هوشمند پیکربندی‌ها (SmartConfigManager)
    /// این کلاس به صورت ماژولار و بدون تغییر در کدهای اصلی v2rayN عمل می‌کند.
    /// وظیفه: دریافت اشتراک‌ها، پارس گره‌ها، تست تأخیر و فیلتر کردن گره‌های مرده.
    /// </summary>
    public class SmartConfigManager
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly List<string> _subscriptionUrls;

        public SmartConfigManager(List<string> subscriptionUrls)
        {
            _subscriptionUrls = subscriptionUrls ?? new List<string>();
            // افزودن هدرهای معمول برای جلوگیری از مسدود شدن توسط CDNها
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "v2rayN/SmartConfigManager");
        }

        /// <summary>
        /// اجرای چرخه کامل: دریافت -> پارس -> تست -> ذخیره‌سازی
        /// </summary>
        public async Task ProcessAndSyncAsync()
        {
            Logging.SaveLog("شروع عملیات خودکار بروزرسانی و فیلتر گره‌ها...");

            var allRawConfigs = new List<string>();

            // 1. دریافت محتوای خام از تمامی لینک‌های اشتراک
            foreach (var url in _subscriptionUrls)
            {
                try
                {
                    var content = await FetchRawSubscriptionAsync(url);
                    if (!string.IsNullOrEmpty(content))
                    {
                        allRawConfigs.Add(content);
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"خطا در دریافت اشتراک از {url}: {ex.Message}");
                }
            }

            // 2. پارس کردن گره‌ها
            var nodes = ParseNodes(allRawConfigs);
            Logging.SaveLog($"تعداد {nodes.Count} گره یافت شد. شروع تست تأخیر...");

            // 3. تست تأخیر به صورت موازی
            var healthyNodes = await FilterHealthyNodesAsync(nodes, 1000); // حد آستانه 1000 میلی‌ثانیه

            // 4. ذخیره در دیتابیس v2rayN (guiNDB.db)
            if (healthyNodes.Any())
            {
                await SaveToDatabaseAsync(healthyNodes);
                Logging.SaveLog($"عملیات با موفقیت پایان یافت. {healthyNodes.Count} گره سالم ذخیره شد.");
            }
            else
            {
                Logging.SaveLog("هیچ گره سالمی یافت نشد.");
            }
        }

        private async Task<string> FetchRawSubscriptionAsync(string url)
        {
            var response = await _httpClient.GetStringAsync(url);
            if (string.IsNullOrEmpty(response)) return string.Empty;

            // بررسی اگر محتوا Base64 است، آن را دیکود می‌کنیم
            try
            {
                if (!response.Contains("://"))
                {
                    var data = Convert.FromBase64String(response);
                    return Encoding.UTF8.GetString(data);
                }
            }
            catch
            {
                // اگر دیکود نشد، فرض می‌کنیم متن خام است
            }
            return response;
        }

        private List<ProfileItem> ParseNodes(List<string> rawContents)
        {
            var profileItems = new List<ProfileItem>();
            foreach (var content in rawContents)
            {
                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        // استفاده از FmtHandler داخلی v2rayN برای پارس استاندارد
                        var item = FmtHandler.ResolveConfig(line);
                        if (item != null && item.IsValid())
                        {
                            item.IndexId = Guid.NewGuid().ToString(); // شناسه یکتا برای دیتابیس
                            profileItems.Add(item);
                        }
                    }
                    catch
                    {
                        // خطاهای پارس گره‌های نامعتبر نادیده گرفته می‌شوند
                    }
                }
            }
            return profileItems;
        }

        private async Task<List<ProfileItem>> FilterHealthyNodesAsync(List<ProfileItem> nodes, int timeoutMs)
        {
            var tasks = nodes.Select(async node =>
            {
                long latency = await GetTcpLatencyAsync(node.Address, node.Port, timeoutMs);
                return new { Node = node, Latency = latency };
            });

            var results = await Task.WhenAll(tasks);
            
            // فیلتر کردن گره‌هایی که تأخیر معتبر دارند
            return results
                .Where(r => r.Latency > 0 && r.Latency <= timeoutMs)
                .Select(r => {
                    r.Node.Remarks = $"[Smart-{r.Latency}ms] {r.Node.Remarks}";
                    return r.Node;
                })
                .ToList();
        }

        private async Task<long> GetTcpLatencyAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                var connectTask = client.ConnectAsync(host, port);
                var delayTask = Task.Delay(timeoutMs);

                if (await Task.WhenAny(connectTask, delayTask) == connectTask)
                {
                    stopwatch.Stop();
                    return stopwatch.ElapsedMilliseconds;
                }
            }
            catch
            {
                // خطا در اتصال
            }
            return -1; // گره مرده یا تایم‌اوت
        }

        private async Task SaveToDatabaseAsync(List<ProfileItem> nodes)
        {
            // استفاده از SQLiteHelper برای تعامل با دیتابیس v2rayN
            // این عملیات بدون دستکاری کدهای هسته انجام می‌شود
            await SQLiteHelper.Instance.InsertAllAsync(nodes);
        }
    }
}
