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
    
    const now = Math.floor(Date.now() / 1000);
    const todayStart = Math.floor(new Date().setHours(0, 0, 0, 0) / 1000);
    
    // 活跃用户数 (未过期且启用) - 使用licenses表
    const activeUsers = await env.DB.prepare(
      `SELECT COUNT(DISTINCT u.id) as count FROM users u
       INNER JOIN licenses l ON u.id = l.user_id 
       WHERE u.is_active = 1 AND l.is_active = 1 
       AND (l.expires_at IS NULL OR l.expires_at > ?)`
    ).bind(now).first();
    
    // 过期用户数 - 使用licenses表
    const expiredUsers = await env.DB.prepare(
      `SELECT COUNT(DISTINCT u.id) as count FROM users u
       INNER JOIN licenses l ON u.id = l.user_id 
       WHERE l.expires_at IS NOT NULL AND l.expires_at < ?`
    ).bind(now).first();
    
    // 今日登录数 - 使用audit_logs表
    const todayLogins = await env.DB.prepare(
      `SELECT COUNT(*) as count FROM audit_logs 
       WHERE action = 'login' AND created_at >= ?`
    ).bind(todayStart).first();
    
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

