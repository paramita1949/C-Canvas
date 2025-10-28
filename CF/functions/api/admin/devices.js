// 管理员API - 查看所有用户的设备绑定情况
export async function onRequestPost(context) {
  const { request, env } = context;
  
  try {
    const { admin_key } = await request.json();
    
    // 验证管理员
    const ADMIN_KEY = env.ADMIN_KEY || 'admin123456';
    if (admin_key !== ADMIN_KEY) {
      return jsonResponse({ success: false, message: '无权限' }, 403);
    }
    
    // 查询所有用户及其设备信息
    const usersQuery = await env.DB.prepare(`
      SELECT 
        u.id,
        u.username,
        u.email,
        u.is_active,
        u.expires_at,
        u.max_devices,
        u.reset_device_count,
        COUNT(d.id) as bound_devices
      FROM users u
      LEFT JOIN devices d ON u.id = d.user_id
      GROUP BY u.id
      ORDER BY u.username
    `).all();
    
    const users = [];
    
    for (const user of usersQuery.results) {
      // 查询该用户的设备详情
      const devicesQuery = await env.DB.prepare(`
        SELECT 
          id,
          hardware_id,
          first_seen as created_at,
          last_seen,
          last_ip
        FROM devices
        WHERE user_id = ?
        ORDER BY last_seen DESC
      `).bind(user.id).all();
      
      // 计算剩余天数
      const now = new Date();
      const expiresAt = new Date(user.expires_at);
      const remainingDays = Math.ceil((expiresAt - now) / (1000 * 60 * 60 * 24));
      
      users.push({
        id: user.id,
        username: user.username,
        email: user.email,
        is_active: user.is_active,
        expires_at: user.expires_at,
        remaining_days: Math.max(0, remainingDays),
        max_devices: user.max_devices,
        bound_devices: user.bound_devices,
        remaining_slots: Math.max(0, user.max_devices - user.bound_devices),
        reset_device_count: user.reset_device_count ?? 3,
        devices: devicesQuery.results.map(d => ({
          id: d.id,
          hardware_id: d.hardware_id,
          created_at: d.created_at,
          last_seen: d.last_seen,
          last_ip: d.last_ip || '未知',
          // 判断是否在线（5分钟内有活动）
          is_online: isDeviceOnline(d.last_seen)
        }))
      });
    }
    
    return jsonResponse({
      success: true,
      total_users: users.length,
      users: users
    });
    
  } catch (error) {
    return jsonResponse({ 
      success: false, 
      message: '服务器错误: ' + error.message 
    }, 500);
  }
}

// 判断设备是否在线（20分钟内有活动）
function isDeviceOnline(lastSeenStr) {
  if (!lastSeenStr) return false;
  
  try {
    const lastSeen = new Date(lastSeenStr);
    const now = new Date();
    const diffMinutes = (now - lastSeen) / (1000 * 60);
    return diffMinutes <= 20; // 20分钟内视为在线（与心跳间隔一致）
  } catch {
    return false;
  }
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

