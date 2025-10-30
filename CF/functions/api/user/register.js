// 用户自助注册 API（仅限客户端）
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { 
      username, 
      password, 
      email, 
      phone,
      // 5项硬件指纹
      cpu_id,
      motherboard_serial,
      disk_serial,
      bios_uuid,
      windows_install_id,
      device_name
    } = await request.json();
    
    // 获取客户端IP（只识别IPv4）
    const rawIP = request.headers.get('CF-Connecting-IP') || 'unknown';
    const clientIP = extractIPv4(rawIP);
    
    // 🔒 验证至少有一项硬件指纹
    const hardwareIds = [cpu_id, motherboard_serial, disk_serial, bios_uuid, windows_install_id].filter(id => id && id.length > 0);
    if (hardwareIds.length === 0) {
      return jsonResponse({ 
        success: false, 
        message: '请从客户端软件内注册（缺少硬件标识）' 
      }, 400);
    }
    
    // 🔒 IP注册限制：同一IPv4最多注册3个账号
    if (clientIP !== 'unknown' && isIPv4(clientIP)) {
      const ipRegisterCount = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_ip = ?'
      ).bind(clientIP).first();
      
      if (ipRegisterCount && ipRegisterCount.count >= 3) {
        return jsonResponse({ 
          success: false, 
          message: '该IP地址注册账号已达上限（最多3个）' 
        }, 429);
      }
    }
    
    // 验证基本参数
    if (!username || !password) {
      return jsonResponse({ success: false, message: '用户名和密码不能为空' }, 400);
    }
    
    if (username.length < 3 || username.length > 20) {
      return jsonResponse({ success: false, message: '用户名长度为3-20个字符' }, 400);
    }
    
    if (!/^[a-zA-Z0-9_]+$/.test(username)) {
      return jsonResponse({ success: false, message: '用户名只能包含字母、数字和下划线' }, 400);
    }
    
    if (password.length < 6) {
      return jsonResponse({ success: false, message: '密码至少6个字符' }, 400);
    }
    
    // 检查数据库是否绑定
    if (!env.DB) {
      return jsonResponse({ success: false, message: '系统错误，请联系管理员' }, 500);
    }
    
    // 检查用户名是否已存在
    const existing = await env.DB.prepare(
      'SELECT id FROM users WHERE username = ?'
    ).bind(username).first();
    
    if (existing) {
      return jsonResponse({ success: false, message: '用户名已被注册' }, 409);
    }
    
    // 🔒 检查该设备是否已注册过账号（5项中任意1项匹配即视为同一设备）
    // 优化：分别查询5次，利用索引，而不是OR查询
    let totalCount = 0;
    
    // 查询CPU ID
    if (cpu_id) {
      const result = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_cpu_id = ?'
      ).bind(cpu_id).first();
      if (result && result.count > 0) {
        totalCount = result.count;
      }
    }
    
    // 如果CPU ID已经找到匹配，检查其他项以获取最大计数
    if (motherboard_serial) {
      const result = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_motherboard_serial = ?'
      ).bind(motherboard_serial).first();
      if (result && result.count > totalCount) {
        totalCount = result.count;
      }
    }
    
    if (disk_serial) {
      const result = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_disk_serial = ?'
      ).bind(disk_serial).first();
      if (result && result.count > totalCount) {
        totalCount = result.count;
      }
    }
    
    if (bios_uuid) {
      const result = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_bios_uuid = ?'
      ).bind(bios_uuid).first();
      if (result && result.count > totalCount) {
        totalCount = result.count;
      }
    }
    
    if (windows_install_id) {
      const result = await env.DB.prepare(
        'SELECT COUNT(*) as count FROM users WHERE register_windows_install_id = ?'
      ).bind(windows_install_id).first();
      if (result && result.count > totalCount) {
        totalCount = result.count;
      }
    }
    
    // 限制同一设备最多注册3个账号
    if (totalCount >= 3) {
      return jsonResponse({ 
        success: false, 
        message: '该设备注册账号已达上限（最多3个）' 
      }, 429);
    }
    
    // 生成密码哈希
    const passwordHash = await hashPassword(password);
    
    // 当前时间戳
    const now = Math.floor(Date.now() / 1000);
    
    // 设置到期时间为注册后1天（试用期）
    const expiresAt = now + 86400; // 1天 = 86400秒
    
    // 插入新用户（记录所有硬件指纹）
    await env.DB.prepare(
      `INSERT INTO users (
        username, password_hash, email, phone,
        register_cpu_id, register_motherboard_serial, register_disk_serial, 
        register_bios_uuid, register_windows_install_id,
        register_ip, register_device_name,
        created_at, updated_at, is_active
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      username,
      passwordHash,
      email || null,
      phone || null,
      cpu_id || null,
      motherboard_serial || null,
      disk_serial || null,
      bios_uuid || null,
      windows_install_id || null,
      clientIP,
      device_name || null,
      now,
      now,
      1
    ).run();
    
    // 获取新创建的用户ID
    const newUser = await env.DB.prepare(
      'SELECT id FROM users WHERE username = ?'
    ).bind(username).first();
    
    // 创建默认试用授权（包含默认解绑次数3次）
    await env.DB.prepare(
      `INSERT INTO licenses (
        user_id, license_type, max_devices, expires_at, 
        reset_count_remaining, created_at, updated_at, is_active
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      newUser.id,
      'trial',
      1,
      expiresAt,
      3,  // 默认3次解绑机会
      now,
      now,
      1
    ).run();
    
    // 记录操作日志
    await env.DB.prepare(
      `INSERT INTO audit_logs (user_id, action, details, ip_address, created_at)
       VALUES (?, ?, ?, ?, ?)`
    ).bind(
      newUser.id,
      'register',
      JSON.stringify({ 
        device_name,
        has_cpu_id: !!cpu_id,
        has_mb_serial: !!motherboard_serial,
        has_disk_serial: !!disk_serial,
        has_bios_uuid: !!bios_uuid,
        has_win_install_id: !!windows_install_id
      }),
      clientIP,
      now
    ).run();
    
    return jsonResponse({
      success: true,
      message: '注册成功！您的账号有效期为1天（试用期）。',
      data: {
        username,
        expires_at: expiresAt,
        trial_days: 1,
        max_devices: 1
      }
    }, 201);
    
  } catch (error) {
    console.error('Register error:', error);
    return jsonResponse({ 
      success: false, 
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

async function hashPassword(password) {
  const encoder = new TextEncoder();
  const data = encoder.encode(password);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}

// 判断是否为IPv4地址
function isIPv4(ip) {
  const ipv4Pattern = /^(\d{1,3}\.){3}\d{1,3}$/;
  if (!ipv4Pattern.test(ip)) return false;
  
  // 验证每段数字在0-255之间
  const parts = ip.split('.');
  return parts.every(part => {
    const num = parseInt(part, 10);
    return num >= 0 && num <= 255;
  });
}

// 从可能的IPv6地址中提取IPv4（如果有）
function extractIPv4(ip) {
  if (!ip || ip === 'unknown') return 'unknown';
  
  // 如果已经是IPv4，直接返回
  if (isIPv4(ip)) return ip;
  
  // 如果是IPv6映射的IPv4 (例如: ::ffff:192.168.1.1)
  const ipv4InV6Match = ip.match(/::ffff:(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})/i);
  if (ipv4InV6Match && isIPv4(ipv4InV6Match[1])) {
    return ipv4InV6Match[1];
  }
  
  // 纯IPv6地址，不记录（返回unknown）
  return 'unknown';
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}
