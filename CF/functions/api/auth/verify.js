// 前台验证API - 供外部程序调用
// 用户使用账号密码验证
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { username, password, hardware_id } = await request.json();
    
    if (!username || !password) {
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户名和密码不能为空' 
      }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare(
      'SELECT * FROM users WHERE username = ?'
    ).bind(username).first();
    
    if (!user) {
      // 用户不存在，不记录日志（避免暴露用户名是否存在，同时避免数据库约束错误）
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户名或密码错误' 
      });
    }
    
    // 验证密码
    const passwordHash = await hashPassword(password);
    if (user.password_hash !== passwordHash) {
      await logLogin(env.DB, user.id, request, false);
      return jsonResponse({ 
        success: false,
        valid: false,
        message: '用户名或密码错误' 
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
    
    // 检查是否过期（使用 Cloudflare 服务器时间，防止客户端篡改）
    // Cloudflare Workers 运行在 Cloudflare 边缘服务器上，时间由服务器提供，客户端无法篡改
    const now = new Date();
    const expiresAt = new Date(user.expires_at);
    
    // 记录服务器时间到日志，便于审计
    const serverTime = now.toISOString();
    
    if (now > expiresAt) {
      return jsonResponse({ 
        success: true,
        valid: false,
        message: '账号已过期',
        reason: 'expired',
        expires_at: user.expires_at,
        server_time: serverTime  // 返回服务器时间供客户端参考
      });
    }
    
    // 硬件ID验证(可选)，并获取设备绑定信息
    let deviceInfo = null;
    if (hardware_id) {
      const deviceCheck = await checkDevice(env.DB, user.id, hardware_id, user.max_devices);
      if (!deviceCheck.allowed) {
        return jsonResponse({ 
          success: true,
          valid: false,
          message: deviceCheck.message,
          reason: 'device_limit'
        });
      }
      deviceInfo = deviceCheck.deviceInfo;
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
    
    // 更新最后登录时间
    await env.DB.prepare(
      'UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = ?'
    ).bind(user.id).run();
    
    // 记录成功登录
    await logLogin(env.DB, user.id, request, true);
    
    // 生成Token
    const token = await generateToken(user.id, username);
    
    // 计算剩余天数
    const remainingDays = Math.ceil((expiresAt - now) / (1000 * 60 * 60 * 24));
    const remainingHours = Math.ceil((expiresAt - now) / (1000 * 60 * 60));
    
    return jsonResponse({ 
      success: true,
      valid: true,
      message: '验证通过',
      data: {
        username: user.username,
        email: user.email,
        expires_at: user.expires_at,
        remaining_days: remainingDays,
        remaining_hours: remainingHours,
        token: token,  // 用于后续心跳验证
        server_time: serverTime,  // 返回服务器时间，客户端可用于时间同步校验
        device_info: deviceInfo,  // 设备绑定信息
        reset_device_count: user.reset_device_count ?? 3  // 剩余重置设备次数
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

// 设备检查
async function checkDevice(db, userId, hardwareId, maxDevices) {
  const existingDevice = await db.prepare(
    'SELECT * FROM devices WHERE user_id = ? AND hardware_id = ?'
  ).bind(userId, hardwareId).first();
  
  // 查询当前已绑定设备数
  const deviceCount = await db.prepare(
    'SELECT COUNT(*) as count FROM devices WHERE user_id = ?'
  ).bind(userId).first();
  
  if (existingDevice) {
    // 设备已存在，更新最后活跃时间
    await db.prepare(
      'UPDATE devices SET last_seen = CURRENT_TIMESTAMP WHERE id = ?'
    ).bind(existingDevice.id).run();
    
    return { 
      allowed: true,
      deviceInfo: {
        bound_devices: deviceCount.count,
        max_devices: maxDevices,
        remaining_slots: maxDevices - deviceCount.count,
        is_new_device: false
      }
    };
  }
  
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
  
  // 新设备，插入数据库
  await db.prepare(
    'INSERT INTO devices (user_id, hardware_id) VALUES (?, ?)'
  ).bind(userId, hardwareId).run();
  
  return { 
    allowed: true,
    deviceInfo: {
      bound_devices: deviceCount.count + 1,
      max_devices: maxDevices,
      remaining_slots: maxDevices - deviceCount.count - 1,
      is_new_device: true
    }
  };
}

// 记录登录日志
async function logLogin(db, userId, request, success) {
  const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
  const userAgent = request.headers.get('User-Agent') || 'unknown';
  
  await db.prepare(
    'INSERT INTO login_logs (user_id, ip_address, user_agent, success) VALUES (?, ?, ?, ?)'
  ).bind(userId, ip, userAgent, success ? 1 : 0).run();
}

// 生成Token
async function generateToken(userId, username) {
  const data = `${userId}:${username}:${Date.now()}`;
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

