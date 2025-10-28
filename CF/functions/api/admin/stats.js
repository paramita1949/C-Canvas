// 获取统计数据
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 总用户数
    const totalUsers = await env.DB.prepare('SELECT COUNT(*) as count FROM users').first();
    
    // 活跃用户数 (未过期且启用)
    const activeUsers = await env.DB.prepare(
      `SELECT COUNT(*) as count FROM users 
       WHERE is_active = 1 AND expires_at > datetime('now')`
    ).first();
    
    // 过期用户数
    const expiredUsers = await env.DB.prepare(
      `SELECT COUNT(*) as count FROM users 
       WHERE expires_at < datetime('now')`
    ).first();
    
    // 今日登录数
    const todayLogins = await env.DB.prepare(
      `SELECT COUNT(*) as count FROM login_logs 
       WHERE DATE(login_time) = DATE('now') AND success = 1`
    ).first();
    
    return jsonResponse({
      success: true,
      stats: {
        total_users: totalUsers.count,
        active_users: activeUsers.count,
        expired_users: expiredUsers.count,
        today_logins: todayLogins.count
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

