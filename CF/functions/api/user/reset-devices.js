// 用户自助重置绑定设备（限3次）
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { username, password, hardware_id } = await request.json();
    
    // 验证参数
    if (!username || !password) {
      return jsonResponse({ success: false, message: '用户名和密码不能为空' }, 400);
    }
    
    // 客户端必须提供hardware_id（只能解绑当前设备）
    if (!hardware_id) {
      return jsonResponse({ success: false, message: '缺少硬件ID参数' }, 400);
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
    
    // 查询用户的license（获取剩余解绑次数）
    const license = await env.DB.prepare(
      'SELECT * FROM licenses WHERE user_id = ? AND is_active = 1 ORDER BY created_at DESC LIMIT 1'
    ).bind(user.id).first();
    
    if (!license) {
      return jsonResponse({
        success: false,
        message: '用户没有有效的授权信息'
      }, 404);
    }
    
    // 检查剩余重置次数
    const resetCount = license.reset_count_remaining ?? 3;
    if (resetCount <= 0) {
      return jsonResponse({ 
        success: false, 
        message: '重置次数已用完，请联系管理员',
        reset_count: 0
      }, 403);
    }
    
    const nowTimestamp = Math.floor(Date.now() / 1000);
    
    // 只解绑当前设备（客户端只能解绑自己）
    const currentDevice = await env.DB.prepare(
      'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ?'
    ).bind(user.id, hardware_id).first();
    
    if (!currentDevice) {
      return jsonResponse({ 
        success: false, 
        message: '当前设备未绑定，无需解绑',
        reset_count: resetCount
      });
    }
    
    // 只删除设备，不动 session（让心跳时自然检测到设备不存在）
    await env.DB.prepare('DELETE FROM devices WHERE id = ?')
      .bind(currentDevice.id).run();
    
    // 减少重置次数（更新licenses表）
    const newResetCount = resetCount - 1;
    await env.DB.prepare(
      'UPDATE licenses SET reset_count_remaining = ?, updated_at = ? WHERE id = ?'
    ).bind(newResetCount, nowTimestamp, license.id).run();
    
    return jsonResponse({
      success: true,
      message: '成功解绑当前设备',
      devices_cleared: 1,
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

