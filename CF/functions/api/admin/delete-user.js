// 删除用户
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    if (!username) {
      return jsonResponse({ success: false, message: '缺少用户名' }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 删除用户相关数据（使用新的表名和结构）
    // 注意：由于使用了外键级联删除（ON DELETE CASCADE），删除用户会自动删除相关数据
    // 但为了明确性和兼容性，还是手动删除
    await env.DB.prepare('DELETE FROM sessions WHERE user_id = ?').bind(user.id).run();
    await env.DB.prepare('DELETE FROM devices WHERE user_id = ?').bind(user.id).run();
    await env.DB.prepare('DELETE FROM licenses WHERE user_id = ?').bind(user.id).run();
    await env.DB.prepare('DELETE FROM audit_logs WHERE user_id = ?').bind(user.id).run();
    await env.DB.prepare('DELETE FROM users WHERE id = ?').bind(user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 已删除`
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

