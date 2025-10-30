// 直接设置用户到期时间
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, expire_date } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    if (!username || !expire_date) {
      return jsonResponse({ success: false, message: '缺少必要参数' }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 查询用户的license
    const license = await env.DB.prepare(
      'SELECT * FROM licenses WHERE user_id = ? AND is_active = 1 ORDER BY created_at DESC LIMIT 1'
    ).bind(user.id).first();
    
    // 验证日期格式
    const newExpireDate = new Date(expire_date);
    if (isNaN(newExpireDate.getTime())) {
      return jsonResponse({ success: false, message: '日期格式错误' }, 400);
    }
    
    // 设置为当天结束（23:59:59）
    newExpireDate.setHours(23, 59, 59, 999);
    const newExpiresAt = Math.floor(newExpireDate.getTime() / 1000);
    const nowTimestamp = Math.floor(Date.now() / 1000);
    
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
      ).bind(user.id, 'admin_set', 1, newExpiresAt, nowTimestamp, nowTimestamp, 1, '管理员设置到期时间').run();
    }
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 到期时间已设置`,
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

