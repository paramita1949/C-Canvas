// 设置用户最大设备数
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, max_devices } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 验证参数
    if (!username || !max_devices || max_devices < 1) {
      return jsonResponse({ success: false, message: '参数错误' }, 400);
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
    
    const nowTimestamp = Math.floor(Date.now() / 1000);
    
    if (license) {
      // 更新现有license的max_devices
      await env.DB.prepare(
        'UPDATE licenses SET max_devices = ?, updated_at = ? WHERE id = ?'
      ).bind(max_devices, nowTimestamp, license.id).run();
    } else {
      // 创建新license（永久有效）
      await env.DB.prepare(
        `INSERT INTO licenses (user_id, license_type, max_devices, expires_at, created_at, updated_at, is_active, notes)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
      ).bind(user.id, 'admin_set', max_devices, null, nowTimestamp, nowTimestamp, 1, '管理员设置设备数').run();
    }
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的最大设备数已设置为 ${max_devices}台`
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

