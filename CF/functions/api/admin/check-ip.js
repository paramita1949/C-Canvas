// 检查特定 IP 状态
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key, ip_address } = await request.json();
    
    // 验证管理员权限
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ 
        success: false, 
        message: '权限不足' 
      }, 403);
    }
    
    if (!ip_address) {
      return jsonResponse({ 
        success: false, 
        message: 'IP地址不能为空' 
      }, 400);
    }
    
    if (!env.SECURITY_KV) {
      return jsonResponse({ 
        success: false, 
        message: 'SECURITY_KV 未配置' 
      }, 500);
    }
    
    const result = {
      ip_address: ip_address,
      blocked: false,
      rate_limits: {},
      failed_attempts: {},
      locked_accounts: []
    };
    
    // 检查黑名单
    const blacklistKey = `blacklist:${ip_address}`;
    const blacklistData = await env.SECURITY_KV.get(blacklistKey, 'json');
    if (blacklistData) {
      result.blocked = true;
      result.block_info = blacklistData;
    }
    
    // 检查管理员登录失败
    const adminFailKey = `admin_fail:${ip_address}`;
    const adminFailData = await env.SECURITY_KV.get(adminFailKey, 'json');
    if (adminFailData) {
      result.failed_attempts.admin_login = adminFailData;
    }
    
    // 查询数据库中的相关记录
    if (env.DB) {
      try {
        // 最近的登录记录（使用audit_logs表）
        const recentLogs = await env.DB.prepare(`
          SELECT *, created_at as login_time, 
                 CASE WHEN action = 'login' THEN 1 ELSE 0 END as success
          FROM audit_logs 
          WHERE ip_address = ? AND action IN ('login', 'login_failed')
          ORDER BY created_at DESC 
          LIMIT 20
        `).bind(ip_address).all();
        
        result.recent_logs = recentLogs.results || [];
        
        // 统计信息（使用audit_logs表）
        const stats = await env.DB.prepare(`
          SELECT 
            COUNT(*) as total_attempts,
            COUNT(CASE WHEN action = 'login' THEN 1 END) as successful,
            COUNT(CASE WHEN action = 'login_failed' THEN 1 END) as failed,
            MIN(created_at) as first_seen,
            MAX(created_at) as last_seen
          FROM audit_logs 
          WHERE ip_address = ? AND action IN ('login', 'login_failed')
        `).bind(ip_address).first();
        
        result.statistics = stats;
        
      } catch (e) {
        result.database_error = e.message;
      }
    }
    
    return jsonResponse({
      success: true,
      data: result
    });
    
  } catch (error) {
    return jsonResponse({ 
      success: false, 
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

