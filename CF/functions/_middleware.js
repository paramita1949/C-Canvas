// 全局中间件 - 处理 CORS、Rate Limiting 和安全防护
export async function onRequest(context) {
  // 处理 OPTIONS 预检请求
  if (context.request.method === 'OPTIONS') {
    return new Response(null, {
      headers: {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, Authorization',
        'Access-Control-Max-Age': '86400',
      }
    });
  }

  try {
    const { request, env } = context;
    const clientIp = request.headers.get('CF-Connecting-IP') || 'unknown';
    const url = new URL(request.url);
    const pathname = url.pathname;

    // 只对 API 请求进行防护检查
    if (pathname.startsWith('/api/')) {
      // 检查 IP 黑名单 (使用 KV)
      if (env.SECURITY_KV) {
        const blocked = await checkBlacklist(env.SECURITY_KV, clientIp);
        if (blocked.isBlocked) {
          return jsonResponse({
            success: false,
            message: blocked.message,
            blocked_until: blocked.blocked_until
          }, 403);
        }

        // Rate Limiting 检查 (使用 KV)
        const rateLimitCheck = await checkRateLimit(env.SECURITY_KV, clientIp, pathname);
        if (!rateLimitCheck.allowed) {
          // 记录失败次数并检查是否需要自动封禁
          const shouldBlock = await recordFailedAttempt(env.SECURITY_KV, env.DB, clientIp, pathname);
          if (shouldBlock) {
            await autoBlockIp(env.SECURITY_KV, env.DB, clientIp, pathname);
          }
          
          return jsonResponse({
            success: false,
            message: `请求过于频繁，请在 ${rateLimitCheck.retryAfter} 秒后重试`,
            retry_after: rateLimitCheck.retryAfter
          }, 429);
        }
      }
    }

    // 执行下一个处理器
    const response = await context.next();
    
    // 添加 CORS 头
    response.headers.set('Access-Control-Allow-Origin', '*');
    response.headers.set('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    response.headers.set('Access-Control-Allow-Headers', 'Content-Type, Authorization');
    
    return response;
  } catch (error) {
    // 全局错误处理
    return new Response(JSON.stringify({
      success: false,
      message: '服务器内部错误',
      error: error.message
    }), {
      status: 500,
      headers: {
        'Content-Type': 'application/json',
        'Access-Control-Allow-Origin': '*'
      }
    });
  }
}

// 检查 IP 黑名单 (KV)
async function checkBlacklist(kv, ip) {
  const key = `blacklist:${ip}`;
  const data = await kv.get(key, 'json');
  
  if (data) {
    // 检查是否永久封禁或临时封禁未到期
    if (!data.blocked_until || new Date(data.blocked_until) > new Date()) {
      return {
        isBlocked: true,
        message: data.blocked_until 
          ? `您的IP已被临时封禁，解封时间: ${new Date(data.blocked_until).toLocaleString('zh-CN')}`
          : '您的IP已被永久封禁，请联系管理员',
        blocked_until: data.blocked_until,
        reason: data.reason
      };
    }
  }
  
  return { isBlocked: false };
}

// Rate Limiting 检查 (KV)
async function checkRateLimit(kv, ip, endpoint) {
  // 不同端点的限流配置
  const rateLimits = {
    '/api/admin/verify': { maxRequests: 5, windowSeconds: 300 },  // 管理员登录: 5分钟5次
    '/api/auth/verify': { maxRequests: 10, windowSeconds: 60 },   // 用户验证: 1分钟10次
    '/api/auth/heartbeat': { maxRequests: 60, windowSeconds: 60 }, // 心跳: 1分钟60次
    '/api/user/register': { maxRequests: 3, windowSeconds: 3600 }, // 注册: 1小时3次
    'default': { maxRequests: 100, windowSeconds: 60 }  // 默认: 1分钟100次
  };

  // 获取当前端点的限流配置
  const config = rateLimits[endpoint] || rateLimits['default'];
  const key = `ratelimit:${ip}:${endpoint}`;
  
  const data = await kv.get(key, 'json');
  const now = Date.now();
  
  if (data) {
    const windowStart = now - config.windowSeconds * 1000;
    
    // 过滤掉窗口外的请求
    const recentRequests = data.requests.filter(time => time > windowStart);
    
    if (recentRequests.length >= config.maxRequests) {
      return {
        allowed: false,
        retryAfter: Math.ceil((recentRequests[0] + config.windowSeconds * 1000 - now) / 1000),
        currentCount: recentRequests.length
      };
    }
    
    // 更新请求记录
    recentRequests.push(now);
    await kv.put(key, JSON.stringify({ requests: recentRequests }), {
      expirationTtl: config.windowSeconds + 60
    });
  } else {
    // 首次请求
    await kv.put(key, JSON.stringify({ requests: [now] }), {
      expirationTtl: config.windowSeconds + 60
    });
  }
  
  return { allowed: true };
}

// 记录失败尝试 (KV + DB)
async function recordFailedAttempt(kv, db, ip, endpoint) {
  const key = `failed:${ip}:${endpoint}`;
  const data = await kv.get(key, 'json') || { count: 0, firstAttempt: Date.now() };
  
  data.count++;
  data.lastAttempt = Date.now();
  
  // 保存到 KV (1小时过期)
  await kv.put(key, JSON.stringify(data), { expirationTtl: 3600 });
  
  // 同时记录到数据库用于审计
  if (db) {
    try {
      await db.prepare(`
        INSERT INTO login_logs (user_id, ip_address, user_agent, success) 
        VALUES (0, ?, ?, 0)
      `).bind(ip, `Rate limit: ${endpoint}`).run();
    } catch (e) {
      // 忽略数据库错误
    }
  }
  
  // 判断是否需要封禁: 5分钟内失败超过20次
  const timeDiff = data.lastAttempt - data.firstAttempt;
  if (data.count >= 20 && timeDiff < 300000) {
    return true; // 需要封禁
  }
  
  return false;
}

// 自动封禁 IP (KV + DB)
async function autoBlockIp(kv, db, ip, endpoint) {
  const blacklistKey = `blacklist:${ip}`;
  const existingBlock = await kv.get(blacklistKey, 'json');
  
  let blockMinutes = 30; // 默认封禁30分钟
  let blockCount = 1;
  
  if (existingBlock) {
    // 已经被封禁过，增加封禁时间
    blockCount = (existingBlock.block_count || 0) + 1;
    blockMinutes = Math.min(blockCount * 30, 1440); // 最多24小时
  }
  
  const blockedUntil = new Date(Date.now() + blockMinutes * 60000).toISOString();
  const blockData = {
    ip_address: ip,
    reason: `自动封禁: ${endpoint} 端点请求过于频繁`,
    blocked_at: new Date().toISOString(),
    blocked_until: blockedUntil,
    auto_blocked: true,
    block_count: blockCount
  };
  
  // 保存到 KV
  await kv.put(blacklistKey, JSON.stringify(blockData), {
    expirationTtl: blockMinutes * 60
  });
  
  // 同时保存到数据库
  if (db) {
    try {
      await db.prepare(`
        INSERT OR REPLACE INTO users (id, username, password_hash, email, expires_at, register_ip, is_active)
        VALUES (0, '系统', 'blocked', ?, datetime('now'), ?, 0)
      `).bind(`${ip}:${endpoint}:${blockCount}`, ip).run();
    } catch (e) {
      // 忽略数据库错误
    }
  }
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    }
  });
}

