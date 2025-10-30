// 验证管理员密钥
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    const clientIp = request.headers.get('CF-Connecting-IP') || 'unknown';
    
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';  // 默认密钥,请在环境变量中修改
    
    if (admin_key === ADMIN_KEY) {
      // 验证成功，清除失败记录
      if (env.SECURITY_KV) {
        const failKey = `admin_fail:${clientIp}`;
        await env.SECURITY_KV.delete(failKey);
      }
      
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
      // 验证失败，记录失败次数
      let waitTime = 0;
      let failCount = 1;
      
      if (env.SECURITY_KV) {
        const failKey = `admin_fail:${clientIp}`;
        const failData = await env.SECURITY_KV.get(failKey, 'json') || { count: 0, firstAttempt: Date.now() };
        
        failCount = failData.count + 1;
        failData.count = failCount;
        failData.lastAttempt = Date.now();
        
        // 保存失败记录 (30分钟过期)
        await env.SECURITY_KV.put(failKey, JSON.stringify(failData), { expirationTtl: 1800 });
        
        // 计算等待时间 (指数退避)
        if (failCount >= 3) {
          waitTime = Math.min(Math.pow(2, failCount - 3), 300); // 最多等待5分钟
        }
        
        // 失败次数过多，自动封禁
        if (failCount >= 10) {
          const blockMinutes = 60; // 封禁1小时
          const blockedUntil = new Date(Date.now() + blockMinutes * 60000).toISOString();
          const blockData = {
            ip_address: clientIp,
            reason: `管理员登录失败次数过多 (${failCount}次)`,
            blocked_at: new Date().toISOString(),
            blocked_until: blockedUntil,
            auto_blocked: true,
            block_count: 1
          };
          
          await env.SECURITY_KV.put(`blacklist:${clientIp}`, JSON.stringify(blockData), {
            expirationTtl: blockMinutes * 60
          });
          
          return new Response(JSON.stringify({ 
            success: false, 
            message: `登录失败次数过多，已被临时封禁 ${blockMinutes} 分钟`,
            blocked_until: blockedUntil
          }), { 
            status: 403,
            headers: { 'Content-Type': 'application/json' }
          });
        }
      }
      
      return new Response(JSON.stringify({ 
        success: false, 
        message: waitTime > 0 
          ? `管理员密钥错误，请等待 ${waitTime} 秒后重试 (失败 ${failCount} 次)`
          : `管理员密钥错误 (失败 ${failCount} 次)`,
        wait_time: waitTime,
        fail_count: failCount
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

