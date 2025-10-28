// 心跳验证API - 供外部程序定期检查账号状态
// 使用token进行验证,无需每次传密码
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { username, token, hardware_id } = await request.json();
    
    if (!username) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '缺少用户名' 
      }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare(
      'SELECT * FROM users WHERE username = ?'
    ).bind(username).first();
    
    if (!user) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户不存在' 
      }, 404);
    }
    
    // 检查是否激活
    if (!user.is_active) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已被禁用',
        reason: 'disabled'
      });
    }
    
    // 检查是否过期
    const now = new Date();
    const expiresAt = new Date(user.expires_at);
    
    if (now > expiresAt) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已过期',
        reason: 'expired',
        expires_at: user.expires_at
      });
    }
    
    // 🔒 验证设备绑定（如果提供了hardware_id）并获取设备信息
    let deviceInfo = null;
    if (hardware_id) {
      const existingDevice = await env.DB.prepare(
        'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ?'
      ).bind(user.id, hardware_id).first();
      
      if (!existingDevice) {
        // 设备不在绑定列表中，可能被管理员重置
        return jsonResponse({ 
          success: true,
          valid: false,
          message: '设备已被管理员解绑，请重新登录',
          reason: 'device_reset'
        });
      }
      
      // 更新设备最后活跃时间
      await env.DB.prepare(
        'UPDATE devices SET last_seen = CURRENT_TIMESTAMP WHERE id = ?'
      ).bind(existingDevice.id).run();
      
      // 查询设备统计信息
      const deviceCount = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
      ).bind(user.id).first();
      
      deviceInfo = {
        bound_devices: deviceCount.count,
        max_devices: user.max_devices,
        remaining_slots: user.max_devices - deviceCount.count
      };
    } else {
      // 即使没有传hardware_id，也返回设备统计信息
      const deviceCount = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
      ).bind(user.id).first();
      deviceInfo = {
        bound_devices: deviceCount.count,
        max_devices: user.max_devices,
        remaining_slots: user.max_devices - deviceCount.count
      };
    }
    
    // 计算剩余时间
    const remainingDays = Math.ceil((expiresAt - now) / (1000 * 60 * 60 * 24));
    const remainingHours = Math.ceil((expiresAt - now) / (1000 * 60 * 60));
    
    // 如果剩余时间少于7天,返回警告
    const warning = remainingDays <= 7 ? `账号即将过期,剩余${remainingDays}天` : null;
    
    // 返回服务器时间，用于客户端时间同步
    const serverTime = now.toISOString();
    
    return jsonResponse({ 
      success: true,
      valid: true,
      message: warning || '验证通过',
      data: {
        username: user.username,
        expires_at: user.expires_at,
        remaining_days: remainingDays,
        remaining_hours: remainingHours,
        warning: warning,
        server_time: serverTime,
        device_info: deviceInfo,  // 设备绑定信息
        reset_device_count: user.reset_device_count || 0  // 剩余解绑次数
      }
    });
    
  } catch (error) {
    return jsonResponse({ 
      success: false,
      valid: false,
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

