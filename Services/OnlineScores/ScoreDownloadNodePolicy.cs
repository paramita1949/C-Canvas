using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ImageColorChanger.Services.OnlineScores
{
    // 只保存官方节点地址、耗时和健康状态；绝不保存票据、请求头或账户资料。
    public sealed class ScoreDownloadNodePolicy
    {
        public sealed class Health
        {
            public string Host { get; set; }
            public DateTimeOffset VerifiedAt { get; set; }
            public DateTimeOffset RetryAfter { get; set; }
            public int Failures { get; set; }
            public double Milliseconds { get; set; }
            public string FailureKind { get; set; }
        }
        private readonly object _sync = new();
        private readonly string _path;
        private readonly Func<DateTimeOffset> _now;
        private readonly Dictionary<string, Health> _health = new(StringComparer.OrdinalIgnoreCase);
        public ScoreDownloadNodePolicy(string path, Func<DateTimeOffset> now = null)
        {
            _path = path; _now = now ?? (() => DateTimeOffset.UtcNow);
            if (path == null || !File.Exists(path)) return;
            try
            {
                if (new FileInfo(path).Length > 64 * 1024) return;
                foreach (var row in JsonSerializer.Deserialize<List<Health>>(File.ReadAllText(path)) ?? new())
                {
                    if (row == null || !Uri.TryCreate(row.Host, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                        uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || row.Failures < 0 ||
                        row.Failures > 20 || !double.IsFinite(row.Milliseconds) || row.Milliseconds < 0) continue;
                    // 损坏/时钟变化不能导致永久禁用。旧成功也不能被当成永久可用。
                    if (row.RetryAfter > _now().AddMinutes(5)) row.RetryAfter = _now().AddMinutes(5);
                    _health[uri.AbsoluteUri] = row;
                    if (_health.Count >= 64) break;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { }
        }
        private Health Get(Uri host)
        {
            if (!_health.TryGetValue(host.AbsoluteUri, out var value))
                _health[host.AbsoluteUri] = value = new Health { Host = host.AbsoluteUri };
            return value;
        }
        public IReadOnlyList<Uri> Rank(IReadOnlyList<Uri> advertised)
        {
            lock (_sync)
                return advertised.Where(h => Get(h).RetryAfter <= _now())
                    .OrderByDescending(h => Verified(Get(h)))
                    .ThenBy(h => Get(h).Milliseconds > 0 ? Get(h).Milliseconds : double.MaxValue)
                    .ThenBy(h => Get(h).Failures).ToArray();
        }
        private bool Verified(Health h) => h.Failures == 0 && h.VerifiedAt > _now().AddDays(-7);
        public bool HasVerifiedNode(IReadOnlyList<Uri> hosts)
        { lock (_sync) return hosts.Any(h => Get(h).RetryAfter <= _now() && Verified(Get(h))); }
        public void Reachable(Uri host, double milliseconds)
        {
            lock (_sync) { var h = Get(host); if (!Verified(h)) h.Milliseconds = milliseconds; }
        }
        public void Success(Uri host, double milliseconds)
        {
            lock (_sync)
            {
                var h = Get(host); h.Milliseconds = Verified(h) && h.Milliseconds > 0 ? h.Milliseconds * .7 + milliseconds * .3 : milliseconds;
                h.VerifiedAt = _now(); h.RetryAfter = default; h.Failures = 0; h.FailureKind = null; Save();
            }
        }
        public void Failure(Uri host, string kind)
        {
            lock (_sync)
            {
                var h = Get(host); h.Failures = Math.Min(20, h.Failures + 1); h.FailureKind = kind;
                h.RetryAfter = _now().AddSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Min(4, h.Failures - 1)))); Save();
            }
        }
        public string Unavailable(IReadOnlyList<Uri> hosts)
        {
            lock (_sync)
            {
                int seconds = hosts.Count == 0 ? 30 : Math.Max(1, (int)Math.Ceiling(hosts.Min(h => Get(h).RetryAfter).Subtract(_now()).TotalSeconds));
                string reasons = string.Join("、", hosts.GroupBy(h => Get(h).FailureKind ?? "连接失败").Select(g => g.Key + " " + g.Count() + "个"));
                return $"官方原图线路暂不可用（已检测 {hosts.Count} 个节点：{reasons}）；失败线路冷却中，约 {seconds} 秒后再次点击将自动重测。搜索结果和原画面已保留。";
            }
        }
        private void Save()
        {
            if (_path == null) return;
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(temp, JsonSerializer.Serialize(_health.Values.OrderByDescending(h => h.VerifiedAt).Take(64)));
                File.Move(temp, _path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
