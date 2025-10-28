// 重置用户绑定的设备
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 验证参数
    if (!username) {
      return jsonResponse({ success: false, message: '用户名不能为空' }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 获取该用户已绑定的设备数
    const deviceCount = await env.DB.prepare(
      'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
    ).bind(user.id).first();
    
    // 删除该用户的所有绑定设备
    await env.DB.prepare('DELETE FROM devices WHERE user_id = ?')
      .bind(user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的所有绑定设备已清除（共清除 ${deviceCount.count} 台设备）`
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

