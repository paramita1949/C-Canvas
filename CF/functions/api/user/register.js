// 用户自助注册 API（仅限客户端）
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { username, password, email, hardware_id, source } = await request.json();
    
    // 获取客户端IP
    const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';
    
    // 🔒 验证硬件ID（必填）
    if (!hardware_id) {
      return jsonResponse({ 
        success: false, 
        message: '请从客户端软件内注册（缺少硬件标识）' 
      }, 400);
    }
    
    if (hardware_id.length < 10) {
      return jsonResponse({ 
        success: false, 
        message: '无效的硬件标识' 
      }, 400);
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
    
    // 检查KV存储是否绑定（用于频率限制）
    if (!env.KV) {
      return jsonResponse({ success: false, message: '系统错误，请联系管理员' }, 500);
    }
    
    // 🔒 频率限制1：IP限制（1小时内最多5个）
    const ipKey = `register:ip:${clientIP}`;
    const ipCountStr = await env.KV.get(ipKey);
    const ipCount = ipCountStr ? parseInt(ipCountStr) : 0;
    
    if (ipCount >= 5) {
      return jsonResponse({ 
        success: false, 
        message: '注册过于频繁，请1小时后重试' 
      }, 429);
    }
    
    // 🔒 频率限制2：硬件ID限制（24小时内最多3个）
    const hwKey = `register:hw:${hardware_id}`;
    const hwCountStr = await env.KV.get(hwKey);
    const hwCount = hwCountStr ? parseInt(hwCountStr) : 0;
    
    if (hwCount >= 3) {
      return jsonResponse({ 
        success: false, 
        message: '该设备注册次数已达上限（24小时内最多3个账号）' 
      }, 429);
    }
    
    // 检查用户是否已存在
    const existing = await env.DB.prepare(
      'SELECT id FROM users WHERE username = ?'
    ).bind(username).first();
    
    if (existing) {
      return jsonResponse({ success: false, message: '用户名已被注册' }, 409);
    }
    
    // 🔒 检查该硬件ID已注册的账号总数
    const hwTotalResult = await env.DB.prepare(
      'SELECT COUNT(*) as count FROM users WHERE hardware_id = ?'
    ).bind(hardware_id).first();
    
    if (hwTotalResult && hwTotalResult.count >= 10) {
      return jsonResponse({ 
        success: false, 
        message: '该设备注册账号已达上限（最多10个）' 
      }, 429);
    }
    
    // 生成密码哈希
    const passwordHash = await hashPassword(password);
    
    // 设置到期时间为当前时间的0点（账号默认已过期，需管理员激活）
    const expiresAt = new Date();
    expiresAt.setHours(0, 0, 0, 0);  // 设置为今天0点
    
    // 插入新用户（记录注册IP和硬件ID）
    await env.DB.prepare(
      `INSERT INTO users (username, password_hash, email, expires_at, max_devices, register_ip, hardware_id, register_source) 
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      username,
      passwordHash,
      email || null,
      expiresAt.toISOString(),
      1,  // 默认1台设备
      clientIP,
      hardware_id,
      source || 'desktop_client'
    ).run();
    
    // 🔒 记录注册次数到KV
    // IP计数（1小时过期）
    await env.KV.put(ipKey, String(ipCount + 1), { expirationTtl: 3600 });
    
    // 硬件ID计数（24小时过期）
    await env.KV.put(hwKey, String(hwCount + 1), { expirationTtl: 86400 });
    
    return jsonResponse({
      success: true,
      message: '注册成功！请等待管理员激活您的账号。',
      data: {
        username,
        expires_at: expiresAt.toISOString(),
        trial_days: 0
      }
    }, 201);
    
  } catch (error) {
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

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

