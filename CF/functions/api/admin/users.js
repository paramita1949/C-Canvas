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
    
    // 查询所有用户及其授权信息
    const { results } = await env.DB.prepare(`
      SELECT 
        u.id, u.username, u.email, u.created_at, u.is_active, u.register_ip,
        l.expires_at, l.max_devices, l.license_type, l.reset_count_remaining
      FROM users u
      LEFT JOIN licenses l ON u.id = l.user_id AND l.is_active = 1
      ORDER BY u.id DESC
    `).all();
    
    // 设置默认值（兼容旧代码）
    const users = results.map(u => ({
      ...u,
      max_devices: u.max_devices || 1,
      reset_device_count: u.reset_count_remaining ?? 3  // 从数据库读取，默认3次
    }));
    
    return jsonResponse({
      success: true,
      users: users
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

