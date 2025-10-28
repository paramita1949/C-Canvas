// 管理员API - 设置用户的重置设备次数
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, reset_count } = await request.json();
    
    // 验证管理员密钥
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 验证参数
    if (!username) {
      return jsonResponse({ success: false, message: '用户名不能为空' }, 400);
    }
    
    if (reset_count === undefined || reset_count === null) {
      return jsonResponse({ success: false, message: '重置次数不能为空' }, 400);
    }
    
    // 验证重置次数范围（0-10）
    const newResetCount = parseInt(reset_count);
    if (isNaN(newResetCount) || newResetCount < 0 || newResetCount > 10) {
      return jsonResponse({ 
        success: false, 
        message: '重置次数必须是 0-10 之间的整数' 
      }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?')
      .bind(username).first();
    
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    const oldResetCount = user.reset_device_count ?? 3;
    
    // 更新重置次数
    await env.DB.prepare('UPDATE users SET reset_device_count = ? WHERE id = ?')
      .bind(newResetCount, user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的重置次数已更新`,
      username: username,
      reset_count_before: oldResetCount,
      reset_count_after: newResetCount,
      changed: newResetCount - oldResetCount
    });
    
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

