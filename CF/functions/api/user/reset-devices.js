// 用户自助重置绑定设备（限3次）
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { username, password } = await request.json();
    
    // 验证参数
    if (!username || !password) {
      return jsonResponse({ success: false, message: '用户名和密码不能为空' }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户名或密码错误' }, 401);
    }
    
    // 验证密码
    const passwordHash = await hashPassword(password);
    if (user.password_hash !== passwordHash) {
      return jsonResponse({ success: false, message: '用户名或密码错误' }, 401);
    }
    
    // 检查账号是否激活
    if (!user.is_active) {
      return jsonResponse({ success: false, message: '账号已被禁用' }, 403);
    }
    
    // 检查剩余重置次数
    const resetCount = user.reset_device_count ?? 3;  // 如果字段不存在，默认3次
    if (resetCount <= 0) {
      return jsonResponse({ 
        success: false, 
        message: '重置次数已用完，请联系管理员',
        reset_count: 0
      }, 403);
    }
    
    // 获取该用户已绑定的设备数
    const deviceCount = await env.DB.prepare(
      'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
    ).bind(user.id).first();
    
    if (deviceCount.count === 0) {
      return jsonResponse({ 
        success: false, 
        message: '您还没有绑定任何设备，无需重置',
        reset_count: resetCount
      });
    }
    
    // 删除该用户的所有绑定设备
    await env.DB.prepare('DELETE FROM devices WHERE user_id = ?')
      .bind(user.id).run();
    
    // 减少重置次数
    const newResetCount = resetCount - 1;
    await env.DB.prepare('UPDATE users SET reset_device_count = ? WHERE id = ?')
      .bind(newResetCount, user.id).run();
    
    return jsonResponse({
      success: true,
      message: `成功清除 ${deviceCount.count} 台绑定设备`,
      devices_cleared: deviceCount.count,
      reset_count: newResetCount,
      reset_remaining: newResetCount
    });
    
  } catch (error) {
    return jsonResponse({ success: false, message: '服务器错误: ' + error.message }, 500);
  }
}

// 密码哈希
async function hashPassword(password) {
  const encoder = new TextEncoder();
  const data = encoder.encode(password);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

