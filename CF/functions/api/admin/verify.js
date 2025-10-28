// 验证管理员密钥
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';  // 默认密钥,请在环境变量中修改
    
    if (admin_key === ADMIN_KEY) {
      // 生成会话token（基于时间戳+密钥的哈希，有效期2小时）
      const now = Date.now();
      const tokenData = `${admin_key}:${now}`;
      const encoder = new TextEncoder();
      const data = encoder.encode(tokenData);
      const hashBuffer = await crypto.subtle.digest('SHA-256', data);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const sessionToken = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
      
      return new Response(JSON.stringify({ 
        success: true, 
        message: '验证成功',
        session_token: sessionToken,
        timestamp: now,
        expires_in: 7200  // 2小时
      }), { 
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      });
    } else {
      return new Response(JSON.stringify({ 
        success: false, 
        message: '管理员密钥错误' 
      }), { 
        status: 403,
        headers: { 'Content-Type': 'application/json' }
      });
    }
    
  } catch (error) {
    return new Response(JSON.stringify({ 
      success: false, 
      message: '服务器错误' 
    }), { 
      status: 500,
      headers: { 'Content-Type': 'application/json' }
    });
  }
}

