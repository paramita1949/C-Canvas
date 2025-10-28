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
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 计算新的到期时间
    const currentExpires = new Date(user.expires_at);
    const now = new Date();
    const baseDate = currentExpires > now ? currentExpires : now;
    baseDate.setDate(baseDate.getDate() + parseInt(days));
    baseDate.setHours(0, 0, 0, 0);  // 统一设置为0点
    
    // 更新到期时间
    await env.DB.prepare('UPDATE users SET expires_at = ? WHERE id = ?')
      .bind(baseDate.toISOString(), user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 延期成功`,
      data: {
        old_expires_at: user.expires_at,
        new_expires_at: baseDate.toISOString()
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

