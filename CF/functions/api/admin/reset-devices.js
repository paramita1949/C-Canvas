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
    
    if (deviceCount.count === 0) {
      return jsonResponse({
        success: false,
        message: '该用户没有绑定任何设备，无需重置'
      });
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
    
    const currentResetCount = license.reset_count_remaining ?? 3;
    
    // 检查解绑次数
    if (currentResetCount <= 0) {
      return jsonResponse({
        success: false,
        message: `用户 ${username} 的解绑次数已用完（剩余0次），无法重置设备`
      }, 403);
    }
    
    const nowTimestamp = Math.floor(Date.now() / 1000);
    const newResetCount = Math.max(0, currentResetCount - 1);
    
    // 删除该用户的所有绑定设备
    await env.DB.prepare('DELETE FROM devices WHERE user_id = ?')
      .bind(user.id).run();
    
    // 删除该用户的所有会话（强制重新登录）
    await env.DB.prepare('DELETE FROM sessions WHERE user_id = ?')
      .bind(user.id).run();
    
    // 消耗一次解绑次数
    await env.DB.prepare(
      'UPDATE licenses SET reset_count_remaining = ?, updated_at = ? WHERE id = ?'
    ).bind(newResetCount, nowTimestamp, license.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的所有绑定设备已清除（共清除 ${deviceCount.count} 台设备）`,
      devices_cleared: deviceCount.count,
      reset_count_before: currentResetCount,
      reset_count_after: newResetCount,
      reset_consumed: 1
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

