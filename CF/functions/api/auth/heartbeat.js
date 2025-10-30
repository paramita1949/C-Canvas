// 心跳验证API - 供外部程序定期检查账号状态
// 使用token进行验证,无需每次传密码
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { 
      token,
      hardware_id  // 客户端混合加密后的硬件ID
    } = await request.json();
    
    if (!token) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '缺少token' 
      }, 400);
    }
    
    if (!hardware_id) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '缺少设备标识' 
      }, 400);
    }
    
    // 获取客户端IP
    const clientIp = request.headers.get('CF-Connecting-IP') || 'unknown';
    
    // 查询会话（包括已失效的）
    const now = Math.floor(Date.now() / 1000);
    const session = await env.DB.prepare(
      `SELECT s.*, u.username, u.is_active as user_active 
       FROM sessions s
       JOIN users u ON s.user_id = u.id
       WHERE s.token = ?`
    ).bind(token).first();
    
    // 检查 session 是否存在
    if (!session) {
      // 可能是用户被删除或 token 无效
      const sessionOnly = await env.DB.prepare(
        'SELECT * FROM sessions WHERE token = ?'
      ).bind(token).first();
      
      if (sessionOnly) {
        // Session 存在但用户被删除了
        return jsonResponse({ 
          success: false,
          valid: false,
          message: '账号不存在，请联系管理员',
          reason: 'user_not_found'
        });
      }
      
      // Token 完全无效
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '会话已过期，请重新登录',
        reason: 'session_expired'
      });
    }
    
    // 检查 session 是否过期 (100天)
    if (session.expires_at <= now) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '会话已过期，请重新登录',
        reason: 'session_expired'
      });
    }
    
    // 检查用户是否激活
    if (!session.user_active) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已被禁用',
        reason: 'disabled'
      });
    }
    
    // 查询用户的有效授权
    const license = await env.DB.prepare(
      `SELECT * FROM licenses 
       WHERE user_id = ? AND is_active = 1 
       AND (expires_at IS NULL OR expires_at > ?)
       ORDER BY expires_at DESC LIMIT 1`
    ).bind(session.user_id, now).first();
    
    if (!license) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已过期',
        reason: 'expired'
      });
    }
    
    // 验证设备（直接用硬件ID查询，最简单直接）
    const device = await env.DB.prepare(
      'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ? AND is_active = 1'
    ).bind(session.user_id, hardware_id).first();
    
    if (!device) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '设备已被解绑，请重新登录',
        reason: 'device_unbound'
      });
    }
    
    // 更新会话心跳时间并续期100天
    const newExpiresAt = now + 86400 * 100; // 续期100天
    await env.DB.prepare(
      'UPDATE sessions SET last_heartbeat_at = ?, expires_at = ?, ip_address = ? WHERE id = ?'
    ).bind(now, newExpiresAt, clientIp, session.id).run();
    
    // 更新设备最后活跃时间
    await env.DB.prepare(
      'UPDATE devices SET last_seen_at = ?, last_ip = ?, updated_at = ? WHERE id = ?'
    ).bind(now, clientIp, now, session.device_id).run();
    
    // 记录心跳日志
    await logAudit(env.DB, session.user_id, 'heartbeat', { device_id: session.device_id }, clientIp);
    
    // 查询设备统计信息
    const deviceCount = await env.DB.prepare(
      'SELECT COUNT(*) as count FROM devices WHERE user_id = ? AND is_active = 1'
    ).bind(session.user_id).first();
    
    // 计算剩余时间
    const remainingDays = license.expires_at ? Math.ceil((license.expires_at - now) / 86400) : null;
    
    // 如果剩余时间少于7天,返回警告
    const warning = remainingDays && remainingDays <= 7 ? `账号即将过期,剩余${remainingDays}天` : null;
    
    return jsonResponse({ 
      success: true,
      valid: true,
      message: warning || '验证通过',
      data: {
        username: session.username,
        license_type: license.license_type,
        expires_at: license.expires_at,
        remaining_days: remainingDays,
        reset_device_count: license.reset_count_remaining ?? 3,  // 剩余解绑次数
        warning: warning,
        device_info: {
          bound_devices: deviceCount.count,
          max_devices: license.max_devices,
          remaining_slots: license.max_devices - deviceCount.count
        }
      }
    });
    
  } catch (error) {
    console.error('Heartbeat error:', error);
    return jsonResponse({ 
      success: false,
      valid: false,
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

// 记录审计日志
async function logAudit(db, userId, action, details, ipAddress) {
  const now = Math.floor(Date.now() / 1000);
  await db.prepare(
    'INSERT INTO audit_logs (user_id, action, details, ip_address, created_at) VALUES (?, ?, ?, ?, ?)'
  ).bind(userId, action, JSON.stringify(details), ipAddress, now).run();
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}
