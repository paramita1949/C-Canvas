// 获取登录日志
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 查询最近100条日志,联查用户名（使用新的audit_logs表）
    const { results } = await env.DB.prepare(
      `SELECT a.*, u.username, a.created_at as login_time
       FROM audit_logs a
       LEFT JOIN users u ON a.user_id = u.id 
       WHERE a.action IN ('login', 'login_failed', 'register')
       ORDER BY a.created_at DESC 
       LIMIT 100`
    ).all();
    
    return jsonResponse({
      success: true,
      logs: results
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

