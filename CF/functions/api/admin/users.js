// 获取用户列表
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 查询所有用户（包含注册IP和重置次数）
    const { results } = await env.DB.prepare(
      'SELECT id, username, email, created_at, expires_at, is_active, last_login, max_devices, register_ip, reset_device_count FROM users ORDER BY id DESC'
    ).all();
    
    return jsonResponse({
      success: true,
      users: results
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

