// 管理员重置用户密码
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, username, new_password } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    if (!username || !new_password) {
      return jsonResponse({ success: false, message: '缺少必要参数' }, 400);
    }
    
    if (new_password.length < 6) {
      return jsonResponse({ success: false, message: '密码至少6个字符' }, 400);
    }
    
    // 查询用户
    const user = await env.DB.prepare('SELECT * FROM users WHERE username = ?').bind(username).first();
    if (!user) {
      return jsonResponse({ success: false, message: '用户不存在' }, 404);
    }
    
    // 生成新密码哈希
    const passwordHash = await hashPassword(new_password);
    
    // 更新密码
    await env.DB.prepare('UPDATE users SET password_hash = ? WHERE id = ?')
      .bind(passwordHash, user.id).run();
    
    return jsonResponse({
      success: true,
      message: `用户 ${username} 密码已重置`,
      data: {
        username: username,
        new_password: new_password  // 返回新密码供管理员告知用户
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

