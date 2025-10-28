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
    
    // 验证日期格式
    const newExpireDate = new Date(expire_date);
    if (isNaN(newExpireDate.getTime())) {
      return jsonResponse({ success: false, message: '日期格式错误' }, 400);
    }
    
    // 统一设置为0点
    newExpireDate.setHours(0, 0, 0, 0);
    
    // 更新到期时间
    await env.DB.prepare('UPDATE users SET expires_at = ? WHERE id = ?')
      .bind(newExpireDate.toISOString(), user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 到期时间已设置`,
      data: {
        old_expires_at: user.expires_at,
        new_expires_at: newExpireDate.toISOString()
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

