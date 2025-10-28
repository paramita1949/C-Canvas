// 启用/禁用用户
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, active } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 更新状态
    await env.DB.prepare('UPDATE users SET is_active = ? WHERE id = ?')
      .bind(active ? 1 : 0, user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 已${active ? '启用' : '禁用'}`
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

