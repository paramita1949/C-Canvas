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
    
    // 更新最大设备数
    await env.DB.prepare('UPDATE users SET max_devices = ? WHERE id = ?')
      .bind(max_devices, user.id).run();
    
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

