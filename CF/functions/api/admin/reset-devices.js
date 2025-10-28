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
    
    // 删除该用户的所有绑定设备
    await env.DB.prepare('DELETE FROM devices WHERE user_id = ?')
      .bind(user.id).run();
    
    // 🔥 管理员手动重置也消耗用户的重置次数（帮客户重置了一次）
    const currentResetCount = user.reset_device_count ?? 3;
    const newResetCount = Math.max(0, currentResetCount - 1);  // 不能为负数
    
    await env.DB.prepare('UPDATE users SET reset_device_count = ? WHERE id = ?')
      .bind(newResetCount, user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 的所有绑定设备已清除（共清除 ${deviceCount.count} 台设备）`,
      devices_cleared: deviceCount.count,
      reset_count_before: currentResetCount,
      reset_count_after: newResetCount,
      reset_consumed: currentResetCount - newResetCount
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

