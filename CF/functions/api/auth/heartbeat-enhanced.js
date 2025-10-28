// 增强版心跳验证 - 带设备指纹异常检测
// 这是一个可选的增强方案，仅供参考

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
    
    // 🔒 验证设备绑定
    let deviceInfo = null;
    if (hardware_id) {
      const existingDevice = await env.DB.prepare(
        'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ?'
      ).bind(user.id, hardware_id).first();
      
      if (!existingDevice) {
        return jsonResponse({ 
          success: true,
          valid: false,
          message: '设备已被管理员解绑，请重新登录',
          reason: 'device_reset'
        });
      }
      
      // 🆕 轻量级设备指纹检测（可选）
      const clientIP = request.headers.get('CF-Connecting-IP');
      const country = request.cf?.country;
      const timezone = request.cf?.timezone;
      
      // 检测异常登录模式
      const warnings = [];
      
      // 1. 检测地理位置变化（如果之前有记录）
      if (existingDevice.last_country && country && existingDevice.last_country !== country) {
        const lastSeen = new Date(existingDevice.last_seen);
        const timeDiff = (now - lastSeen) / 1000 / 60; // 分钟
        
        // 如果30分钟内跨国登录，标记为可疑
        if (timeDiff < 30) {
          warnings.push(`检测到异常：${timeDiff.toFixed(0)}分钟内从${existingDevice.last_country}切换到${country}`);
        }
      }
      
      // 2. 检测频繁设备切换
      const recentSwitches = await env.DB.prepare(
        `SELECT COUNT(DISTINCT hardware_id) as count 
         FROM devices 
         WHERE user_id = ? 
         AND last_seen > datetime('now', '-1 hour')`
      ).bind(user.id).first();
      
      if (recentSwitches.count > 3) {
        warnings.push(`检测到1小时内活跃${recentSwitches.count}台设备`);
      }
      
      // 更新设备信息（包含轻量级指纹）
      // 注意：last_ip 字段已在 schema.sql 中定义
      // last_country 和 last_timezone 需要先执行迁移脚本
      await env.DB.prepare(
        `UPDATE devices 
         SET last_seen = CURRENT_TIMESTAMP,
             last_ip = ?
             ${country ? ', last_country = ?' : ''}
             ${timezone ? ', last_timezone = ?' : ''}
         WHERE id = ?`
      ).bind(
        clientIP, 
        ...(country ? [country] : []),
        ...(timezone ? [timezone] : []),
        existingDevice.id
      ).run();
      
      // 如果有警告，记录日志（供管理员查看）
      if (warnings.length > 0) {
        // 可以记录到单独的安全日志表
        // await env.DB.prepare(...).run();
        console.log(`[安全警告] 用户${username} 设备${hardware_id}: ${warnings.join(', ')}`);
      }
      
      // 查询设备统计信息
      const deviceCount = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
      ).bind(user.id).first();
      
      deviceInfo = {
        bound_devices: deviceCount.count,
        max_devices: user.max_devices,
        remaining_slots: user.max_devices - deviceCount.count,
        warnings: warnings.length > 0 ? warnings : null  // 仅供参考，不强制退出
      };
    }
    
    // 计算剩余时间
    const remainingDays = Math.ceil((expiresAt - now) / (1000 * 60 * 60 * 24));
    const remainingHours = Math.ceil((expiresAt - now) / (1000 * 60 * 60));
    
    const warning = remainingDays <= 7 ? `账号即将过期,剩余${remainingDays}天` : null;
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
        device_info: deviceInfo,
        reset_device_count: user.reset_device_count || 0
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

