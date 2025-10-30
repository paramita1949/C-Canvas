// 管理员API - 增加用户的重置设备次数
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, add_count } = await request.json();
    
    // 验证管理员密钥
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 验证参数
    if (!username) {
      return jsonResponse({ success: false, message: '用户名不能为空' }, 400);
    }
    
    if (add_count === undefined || add_count === null) {
      return jsonResponse({ success: false, message: '增加次数不能为空' }, 400);
    }
    
    // 验证增加次数（-10 到 10，允许负数表示减少）
    const addValue = parseInt(add_count);
    if (isNaN(addValue) || addValue < -10 || addValue > 10) {
      return jsonResponse({ 
        success: false, 
        message: '增加次数必须是 -10 到 10 之间的整数' 
      }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?')
      .bind(username).first();
    
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 查询用户的license
    const license = await env.DB.prepare(
      'SELECT * FROM licenses WHERE user_id = ? AND is_active = 1 ORDER BY created_at DESC LIMIT 1'
    ).bind(user.id).first();
    
    if (!license) {
      return jsonResponse({
        success: false,
        message: '用户没有有效的授权信息'
      }, 404);
    }
    
    const oldResetCount = license.reset_count_remaining ?? 3;
    const newResetCount = Math.max(0, Math.min(10, oldResetCount + addValue));  // 限制在 0-10 之间
    const nowTimestamp = Math.floor(Date.now() / 1000);
    
    // 更新重置次数
    await env.DB.prepare(
      'UPDATE licenses SET reset_count_remaining = ?, updated_at = ? WHERE id = ?'
    ).bind(newResetCount, nowTimestamp, license.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的重置次数已${addValue > 0 ? '增加' : '减少'}`,
      username: username,
      reset_count_before: oldResetCount,
      reset_count_after: newResetCount,
      added: addValue,
      actual_change: newResetCount - oldResetCount
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

