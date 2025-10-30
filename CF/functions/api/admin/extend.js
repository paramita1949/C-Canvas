// 延长用户有效期
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, days } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    if (!username || !days) {
      return jsonResponse({ success: false, message: '缺少必要参数' }, 400);
    }
    
    // 查询用户及其license信息
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 查询用户的license
    const license = await env.DB.prepare(
      'SELECT * FROM licenses WHERE user_id = ? AND is_active = 1 ORDER BY created_at DESC LIMIT 1'
    ).bind(user.id).first();
    
    // 计算新的到期时间（Unix时间戳）
    const nowTimestamp = Math.floor(Date.now() / 1000);
    let baseTimestamp;
    
    if (license && license.expires_at) {
      // 如果当前license未过期，从当前到期时间延长
      baseTimestamp = license.expires_at > nowTimestamp ? license.expires_at : nowTimestamp;
    } else {
      // 否则从现在开始
      baseTimestamp = nowTimestamp;
    }
    
    // 延长天数
    const newExpiresAt = baseTimestamp + (parseInt(days) * 86400);
    
    if (license) {
      // 更新现有license
      await env.DB.prepare(
        'UPDATE licenses SET expires_at = ?, updated_at = ? WHERE id = ?'
      ).bind(newExpiresAt, nowTimestamp, license.id).run();
    } else {
      // 创建新license
      await env.DB.prepare(
        `INSERT INTO licenses (user_id, license_type, max_devices, expires_at, created_at, updated_at, is_active, notes)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
      ).bind(user.id, 'admin_extended', 1, newExpiresAt, nowTimestamp, nowTimestamp, 1, '管理员延期').run();
    }
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 延期成功`,
      data: {
        old_expires_at: license?.expires_at || null,
        new_expires_at: newExpiresAt
      }
    });
    
  } catch (error) {
    return jsonResponse({ success: false, message: '服务器错误: ' + error.message }, 500);
  }
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

