// 前台验证API - 供外部程序调用
// 用户使用账号密码验证
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { 
      username, 
      password, 
      hardware_id,  // 客户端混合加密后的硬件ID
      device_name,
      os_version,
      app_version
    } = await request.json();
    
    if (!username || !password) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户名和密码不能为空' 
      }, 400);
    }
    
    if (!hardware_id) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '缺少设备标识' 
      }, 400);
    }
    
    // 获取客户端IP
    const clientIp = request.headers.get('CF-Connecting-IP') || 'unknown';
    
    // 检查账户是否被锁定
    if (env.SECURITY_KV) {
      const lockData = await env.SECURITY_KV.get(`user_lock:${username}`, 'json');
      if (lockData) {
        const lockedUntil = new Date(lockData.locked_until);
        if (lockedUntil > new Date()) {
          return jsonResponse({ 
            success: false,
            valid: false,
            message: `账户已被临时锁定，解锁时间: ${lockedUntil.toLocaleString('zh-CN')}`,
            locked_until: lockData.locked_until
          });
        }
      }
    }
    
    // 查询用户
    const user = await env.DB.prepare(
      'SELECT * FROM users WHERE username = ?'
    ).bind(username).first();
    
    if (!user) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户名或密码错误' 
      });
    }
    
    // 验证密码
    const passwordHash = await hashPassword(password);
    if (user.password_hash !== passwordHash) {
      await logAudit(env.DB, user.id, 'login_failed', { reason: 'wrong_password' }, clientIp);
      
      // 记录密码错误次数
      let waitTime = 0;
      let failCount = 1;
      
      if (env.SECURITY_KV) {
        const failKey = `user_fail:${username}:${clientIp}`;
        const failData = await env.SECURITY_KV.get(failKey, 'json') || { count: 0, firstAttempt: Date.now() };
        
        failCount = failData.count + 1;
        failData.count = failCount;
        failData.lastAttempt = Date.now();
        
        await env.SECURITY_KV.put(failKey, JSON.stringify(failData), { expirationTtl: 1800 });
        
        if (failCount >= 3) {
          waitTime = Math.min(Math.pow(2, failCount - 3), 60);
        }
        
        if (failCount >= 15) {
          const lockMinutes = 30;
          const lockedUntil = new Date(Date.now() + lockMinutes * 60000).toISOString();
          const lockData = {
            username: username,
            ip_address: clientIp,
            locked_at: new Date().toISOString(),
            locked_until: lockedUntil,
            reason: `密码错误次数过多 (${failCount}次)`
          };
          
          await env.SECURITY_KV.put(`user_lock:${username}`, JSON.stringify(lockData), {
            expirationTtl: lockMinutes * 60
          });
          
          return jsonResponse({ 
            success: false,
            valid: false,
            message: `账户已被临时锁定 ${lockMinutes} 分钟，请稍后再试`,
            locked_until: lockedUntil
          });
        }
      }
      
      return jsonResponse({ 
        success: false,
        valid: false,
        message: waitTime > 0 
          ? `用户名或密码错误，请等待 ${waitTime} 秒后重试`
          : '用户名或密码错误'
      });
    }
    
    // 检查账号是否激活
    if (!user.is_active) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已被禁用',
        reason: 'disabled'
      });
    }
    
    // 查询用户的有效授权
    const now = Math.floor(Date.now() / 1000);
    const license = await env.DB.prepare(
      `SELECT * FROM licenses 
       WHERE user_id = ? AND is_active = 1 
       AND (expires_at IS NULL OR expires_at > ?)
       ORDER BY expires_at DESC LIMIT 1`
    ).bind(user.id, now).first();
    
    if (!license) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已过期',
        reason: 'expired'
      });
    }
    
    // 设备检查（简单的硬件ID匹配）
    const deviceCheck = await checkDevice(
      env.DB, 
      user.id, 
      hardware_id,
      license.max_devices,
      { device_name, os_version, app_version },
      clientIp
    );
    
    if (!deviceCheck.allowed) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: deviceCheck.message,
        reason: 'device_limit',
        device_info: deviceCheck.deviceInfo
      });
    }
    
    // 验证通过，清除失败记录
    if (env.SECURITY_KV) {
      const failKey = `user_fail:${username}:${clientIp}`;
      await env.SECURITY_KV.delete(failKey);
    }
    
    // 记录成功登录
    await logAudit(env.DB, user.id, 'login', { 
      device_id: deviceCheck.deviceId,
      is_new_device: deviceCheck.isNewDevice 
    }, clientIp);
    
    // 生成Token并创建会话
    const token = await generateToken();
    const expiresAt = now + 86400 * 100; // 100天
    
    await env.DB.prepare(
      `INSERT INTO sessions (user_id, device_id, token, expires_at, created_at, last_heartbeat_at, ip_address, user_agent)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(user.id, deviceCheck.deviceId, token, expiresAt, now, now, clientIp, request.headers.get('User-Agent')).run();
    
    // 计算剩余时间
    const remainingDays = license.expires_at ? Math.ceil((license.expires_at - now) / 86400) : null;
    
    return jsonResponse({ 
      success: true,
      valid: true,
      message: '验证通过',
      data: {
        username: user.username,
        email: user.email,
        license_type: license.license_type,
        expires_at: license.expires_at,
        remaining_days: remainingDays,
        reset_device_count: license.reset_count_remaining ?? 3,  // 剩余解绑次数
        max_devices: license.max_devices,
        token: token,
        device_info: deviceCheck.deviceInfo
      }
    });
    
  } catch (error) {
    console.error('Verify error:', error);
    return jsonResponse({ 
      success: false,
      valid: false,
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

// 设备检查（简单的硬件ID匹配）
async function checkDevice(db, userId, hardwareId, maxDevices, deviceInfo, clientIp) {
  const { device_name, os_version, app_version } = deviceInfo;
  
  // 查找是否已存在该硬件ID的设备
  const existingDevice = await db.prepare(
    'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ? AND is_active = 1'
  ).bind(userId, hardwareId).first();
  
  const now = Math.floor(Date.now() / 1000);
  
  if (existingDevice) {
    // 设备已存在，更新信息
    await db.prepare(
      `UPDATE devices SET 
        device_name = COALESCE(?, device_name),
        os_version = COALESCE(?, os_version),
        app_version = COALESCE(?, app_version),
        last_ip = ?,
        last_seen_at = ?,
        updated_at = ?
       WHERE id = ?`
    ).bind(
      device_name || null,
      os_version || null,
      app_version || null,
      clientIp,
      now,
      now,
      existingDevice.id
    ).run();
    
    // 查询当前设备总数
    const deviceCount = await db.prepare(
      'SELECT COUNT(*) as count FROM devices WHERE user_id = ? AND is_active = 1'
    ).bind(userId).first();
    
    return {
      allowed: true,
      deviceId: existingDevice.id,
      isNewDevice: false,
      deviceInfo: {
        bound_devices: deviceCount.count,
        max_devices: maxDevices,
        remaining_slots: maxDevices - deviceCount.count
      }
    };
  }
  
  // 新设备，检查是否超出限制
  const deviceCount = await db.prepare(
    'SELECT COUNT(*) as count FROM devices WHERE user_id = ? AND is_active = 1'
  ).bind(userId).first();
  
  if (deviceCount.count >= maxDevices) {
    return {
      allowed: false,
      message: `已达到最大设备数限制(${maxDevices}台)`,
      deviceInfo: {
        bound_devices: deviceCount.count,
        max_devices: maxDevices,
        remaining_slots: 0
      }
    };
  }
  
  // 插入新设备
  const result = await db.prepare(
    `INSERT INTO devices (
      user_id, hardware_id, device_name,
      os_version, app_version, last_ip,
      first_seen_at, last_seen_at, created_at, updated_at, is_active
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).bind(
    userId,
    hardwareId,
    device_name || null,
    os_version || null,
    app_version || null,
    clientIp,
    now,
    now,
    now,
    now,
    1
  ).run();
  
  return {
    allowed: true,
    deviceId: result.meta.last_row_id,
    isNewDevice: true,
    deviceInfo: {
      bound_devices: deviceCount.count + 1,
      max_devices: maxDevices,
      remaining_slots: maxDevices - deviceCount.count - 1
    }
  };
}

// 记录审计日志
async function logAudit(db, userId, action, details, ipAddress) {
  const now = Math.floor(Date.now() / 1000);
  await db.prepare(
    'INSERT INTO audit_logs (user_id, action, details, ip_address, created_at) VALUES (?, ?, ?, ?, ?)'
  ).bind(userId, action, JSON.stringify(details), ipAddress, now).run();
}

// 生成Token
async function generateToken() {
  const data = `${Date.now()}:${Math.random()}`;
  const encoder = new TextEncoder();
  const hashBuffer = await crypto.subtle.digest('SHA-256', encoder.encode(data));
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
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
