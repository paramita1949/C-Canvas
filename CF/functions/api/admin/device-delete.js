// 管理员API - 删除指定设备
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    // TODO: 添加管理员身份验证
    // 这里应该验证管理员token，暂时省略
    
    const { device_id, user_id } = await request.json();
    
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
    
    // 删除设备
    await env.DB.prepare('DELETE FROM devices WHERE id = ?')
      .bind(device_id).run();
    
    // 🔥 管理员删除设备也消耗用户的重置次数
    const currentResetCount = user.reset_device_count ?? 3;
    const newResetCount = Math.max(0, currentResetCount - 1);  // 不能为负数
    
    await env.DB.prepare('UPDATE users SET reset_device_count = ? WHERE id = ?')
      .bind(newResetCount, userId).run();
    
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

