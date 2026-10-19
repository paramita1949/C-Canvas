#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 升级地址健康度选择器：记住最近一次探测成功的地址，并让刚失败的地址进入冷却期。
    /// 目的是避免每次升级检查都从同一个不可用地址开始、反复卡在超时上。
    /// </summary>
    internal sealed class UpdateEndpointSelector
    {
        internal static readonly TimeSpan DefaultFailureCooldown = TimeSpan.FromMinutes(10);

        private readonly object _gate = new();
        private readonly Dictionary<string, DateTimeOffset> _failedAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<DateTimeOffset> _clock;
        private readonly TimeSpan _failureCooldown;
        private string _preferred = string.Empty;

        public UpdateEndpointSelector(Func<DateTimeOffset>? clock = null, TimeSpan? failureCooldown = null)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _failureCooldown = failureCooldown ?? DefaultFailureCooldown;
        }

        /// <summary>最近一次探测成功的地址（没有则为空字符串）。</summary>
        public string Preferred
        {
            get
            {
                lock (_gate)
                {
                    return _preferred;
                }
            }
        }

        /// <summary>该地址是否仍处于失败冷却期。</summary>
        public bool IsCoolingDown(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return false;
            }

            lock (_gate)
            {
                return _failedAt.TryGetValue(baseUrl, out var failedAt)
                    && _clock() - failedAt < _failureCooldown;
            }
        }

        /// <summary>
        /// 按“健康优先”重排候选地址：最近可用地址最前，冷却中的地址最后，其余保持配置顺序。
        /// 不会丢弃任何地址，全部冷却时仍然返回完整列表。
        /// </summary>
        public IReadOnlyList<string> Order(IEnumerable<string>? candidates)
        {
            var list = (candidates ?? Enumerable.Empty<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToList();

            if (list.Count <= 1)
            {
                return list;
            }

            lock (_gate)
            {
                var now = _clock();
                var healthy = new List<string>();
                var coolingDown = new List<string>();
                foreach (var url in list)
                {
                    var cooling = _failedAt.TryGetValue(url, out var failedAt) && now - failedAt < _failureCooldown;
                    (cooling ? coolingDown : healthy).Add(url);
                }

                // 首选地址只有在不处于冷却期时才提前，避免把用户重新钉死在刚失败的地址上。
                if (!string.IsNullOrEmpty(_preferred) && !IsCoolingDownLocked(_preferred, now))
                {
                    var index = healthy.FindIndex(url => string.Equals(url, _preferred, StringComparison.OrdinalIgnoreCase));
                    if (index > 0)
                    {
                        var preferred = healthy[index];
                        healthy.RemoveAt(index);
                        healthy.Insert(0, preferred);
                    }
                }

                healthy.AddRange(coolingDown);
                return healthy;
            }
        }

        /// <summary>记录一次成功探测：该地址成为首选。</summary>
        public void MarkSuccess(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            lock (_gate)
            {
                _preferred = baseUrl;
                _failedAt.Remove(baseUrl);
            }
        }

        /// <summary>记录一次失败探测：该地址进入冷却期。</summary>
        public void MarkFailure(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            lock (_gate)
            {
                _failedAt[baseUrl] = _clock();
                if (string.Equals(_preferred, baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    _preferred = string.Empty;
                }
            }
        }

        /// <summary>升级地址列表更新后清空健康记录。</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _preferred = string.Empty;
                _failedAt.Clear();
            }
        }

        private bool IsCoolingDownLocked(string baseUrl, DateTimeOffset now)
        {
            return _failedAt.TryGetValue(baseUrl, out var failedAt) && now - failedAt < _failureCooldown;
        }
    }
}
