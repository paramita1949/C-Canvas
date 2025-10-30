// 管理员API - 删除指定设备
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, device_id, user_id } = await request.json();
    
    // 🔒 验证管理员权限
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    if (!device_id) {
      return jsonResponse({ 
        success: false, 
        message: '设备ID不能为空' 
      }, 400);
    }
    
    // 查询设备信息
    let device;
    if (user_id) {
      device = await env.DB.prepare(
        'SELECT * FROM devices WHERE id = ? AND user_id = ?'
      ).bind(device_id, user_id).first();
      
      if (!device) {
        return jsonResponse({ 
          success: false, 
          message: '设备不存在或不属于该用户' 
        }, 404);
      }
    } else {
      device = await env.DB.prepare(
        'SELECT * FROM devices WHERE id = ?'
      ).bind(device_id).first();
      
      if (!device) {
        return jsonResponse({ 
          success: false, 
          message: '设备不存在' 
        }, 404);
      }
    }
    
    // 获取该设备所属的用户信息
    const userId = device.user_id;
    const user = await env.DB.prepare(
      'SELECT * FROM users WHERE id = ?'
    ).bind(userId).first();
    
    if (!user) {
      return jsonResponse({ 
        success: false, 
        message: '用户不存在' 
      }, 404);
    }
    
    // 查询用户的license（获取剩余解绑次数）
    const license = await env.DB.prepare(
      'SELECT * FROM licenses WHERE user_id = ? AND is_active = 1 ORDER BY created_at DESC LIMIT 1'
    ).bind(userId).first();
    
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
        message: `用户 ${user.username} 的解绑次数已用完（剩余0次），无法删除设备`
      }, 403);
    }
    
    const nowTimestamp = Math.floor(Date.now() / 1000);
    const newResetCount = Math.max(0, currentResetCount - 1);
    
    // 只删除设备，不动 session（让心跳时自然检测到设备不存在）
    await env.DB.prepare('DELETE FROM devices WHERE id = ?')
      .bind(device_id).run();
    
    // 消耗一次解绑次数
    await env.DB.prepare(
      'UPDATE licenses SET reset_count_remaining = ?, updated_at = ? WHERE id = ?'
    ).bind(newResetCount, nowTimestamp, license.id).run();
    
    return jsonResponse({
      success: true,
      message: '设备已删除',
      device_id: device_id,
      username: user.username,
      reset_count_before: currentResetCount,
      reset_count_after: newResetCount,
      reset_consumed: 1
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

