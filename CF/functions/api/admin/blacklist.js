// IP 黑名单管理 API
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, action, ip_address, reason, block_minutes } = await request.json();
    
    // 验证管理员权限
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ 
        success: false, 
        message: '权限不足' 
      }, 403);
    }
    
    if (!env.SECURITY_KV) {
      return jsonResponse({ 
        success: false, 
        message: 'SECURITY_KV 未配置' 
      }, 500);
    }
    
    switch (action) {
      case 'list':
        return await listBlacklist(env.SECURITY_KV);
      
      case 'add':
        return await addBlacklist(env.SECURITY_KV, ip_address, reason, block_minutes);
      
      case 'remove':
        return await removeBlacklist(env.SECURITY_KV, ip_address);
      
      case 'stats':
        return await getSecurityStats(env.SECURITY_KV, env.DB);
      
      default:
        return jsonResponse({ 
          success: false, 
          message: '未知操作' 
        }, 400);
    }
    
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

// 列出黑名单
async function listBlacklist(kv) {
  try {
    // KV 不支持列表操作，只能通过前缀扫描（需要 Workers Paid 计划）
    // 这里返回一个提示信息
    return jsonResponse({
      success: true,
      message: '黑名单功能已启用，通过 KV 存储',
      note: '由于 KV 限制，无法直接列出所有封禁IP。可以通过查询特定IP来检查是否被封禁。',
      example: '发送 action=check&ip_address=1.2.3.4 来检查特定IP'
    });
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '获取黑名单失败: ' + error.message 
    });
  }
}

// 添加到黑名单
async function addBlacklist(kv, ip, reason = '手动封禁', blockMinutes = null) {
  if (!ip) {
    return jsonResponse({ 
      success: false, 
      message: 'IP地址不能为空' 
    }, 400);
  }
  
  try {
    const blockedUntil = blockMinutes 
      ? new Date(Date.now() + blockMinutes * 60000).toISOString()
      : null; // null 表示永久封禁
    
    const blockData = {
      ip_address: ip,
      reason: reason,
      blocked_at: new Date().toISOString(),
      blocked_until: blockedUntil,
      auto_blocked: false,
      block_count: 1
    };
    
    const key = `blacklist:${ip}`;
    
    if (blockMinutes) {
      await kv.put(key, JSON.stringify(blockData), {
        expirationTtl: blockMinutes * 60
      });
    } else {
      // 永久封禁，设置为1年过期（KV 最长时间）
      await kv.put(key, JSON.stringify(blockData), {
        expirationTtl: 365 * 24 * 60 * 60
      });
    }
    
    return jsonResponse({
      success: true,
      message: blockedUntil 
        ? `已封禁 IP: ${ip}，封禁 ${blockMinutes} 分钟`
        : `已永久封禁 IP: ${ip}`,
      data: blockData
    });
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '封禁失败: ' + error.message 
    }, 500);
  }
}

// 从黑名单移除
async function removeBlacklist(kv, ip) {
  if (!ip) {
    return jsonResponse({ 
      success: false, 
      message: 'IP地址不能为空' 
    }, 400);
  }
  
  try {
    const key = `blacklist:${ip}`;
    await kv.delete(key);
    
    return jsonResponse({
      success: true,
      message: `已解除 IP 封禁: ${ip}`
    });
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '解封失败: ' + error.message 
    }, 500);
  }
}

// 获取安全统计
async function getSecurityStats(kv, db) {
  try {
    const stats = {
      security_enabled: true,
      kv_storage: 'active',
      rate_limiting: 'enabled',
      auto_blocking: 'enabled',
      endpoints_protected: [
        { endpoint: '/api/admin/verify', limit: '5分钟5次', auto_block: '10次失败' },
        { endpoint: '/api/auth/verify', limit: '1分钟10次', auto_block: '15次失败' },
        { endpoint: '/api/auth/heartbeat', limit: '1分钟60次', auto_block: '20次失败' },
        { endpoint: '/api/user/register', limit: '1小时3次', auto_block: '自动' }
      ]
    };
    
    // 如果有数据库，获取最近的日志统计
    if (db) {
      try {
        const last24h = Math.floor(Date.now() / 1000) - 86400;
        const recentLogs = await db.prepare(`
          SELECT 
            COUNT(CASE WHEN action = 'login_failed' THEN 1 END) as failed_attempts,
            COUNT(CASE WHEN action = 'login' THEN 1 END) as successful_logins,
            COUNT(DISTINCT ip_address) as unique_ips
          FROM audit_logs 
          WHERE action IN ('login', 'login_failed') AND created_at > ?
        `).bind(last24h).first();
        
        stats.last_24h = recentLogs;
      } catch (e) {
        stats.last_24h = { error: 'DB查询失败' };
      }
    }
    
    return jsonResponse({
      success: true,
      stats: stats
    });
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '获取统计失败: ' + error.message 
    }, 500);
  }
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

