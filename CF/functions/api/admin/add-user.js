// 添加用户
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const body = await request.json();
    const { admin_key, username, password, email, days, max_devices } = body;
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 验证参数
    if (!username || !password) {
      return jsonResponse({ success: false, message: '用户名和密码不能为空' }, 400);
    }
    
    // 检查数据库是否绑定
    if (!env.DB) {
      return jsonResponse({ success: false, message: '数据库未绑定，请在 Cloudflare Dashboard 中绑定 D1 数据库' }, 500);
    }
    
    // 检查用户是否已存在
    const existing = await env.DB.prepare('SELECT id FROM users WHERE username = ?').bind(username).first();
    if (existing) {
      return jsonResponse({ success: false, message: '用户名已存在' }, 409);
    }
    
    // 生成密码哈希
    const passwordHash = await hashPassword(password);
    
    // 计算到期时间（Unix时间戳）
    const expiresDate = new Date();
    expiresDate.setDate(expiresDate.getDate() + (days || 30));
    expiresDate.setHours(23, 59, 59, 999);  // 设置为当天结束
    const expiresAt = Math.floor(expiresDate.getTime() / 1000);
    
    // 获取客户端IP
    const clientIP = request.headers.get('CF-Connecting-IP') || 'admin-added';
    
    const now = Math.floor(Date.now() / 1000);
    
    // 插入用户（新结构：不包含expires_at和max_devices）
    const userResult = await env.DB.prepare(
      `INSERT INTO users (username, password_hash, email, register_ip, created_at, updated_at, is_active) 
       VALUES (?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      username,
      passwordHash,
      email || null,
      clientIP,
      now,
      now,
      1
    ).run();
    
    // 获取新创建的用户ID
    const userId = userResult.meta.last_row_id;
    
    // 创建授权记录
    await env.DB.prepare(
      `INSERT INTO licenses (user_id, license_type, max_devices, expires_at, created_at, updated_at, is_active, notes)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      userId,
      'admin_created',
      max_devices || 1,
      expiresAt,
      now,
      now,
      1,
      '管理员添加'
    ).run();
    
    return jsonResponse({
      success: true,
      message: '用户添加成功',
      data: {
        username,
        expires_at: expiresAt,
        max_devices: max_devices || 1
      }
    });
    
  } catch (error) {
    return jsonResponse({ success: false, message: '服务器错误: ' + error.message }, 500);
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

