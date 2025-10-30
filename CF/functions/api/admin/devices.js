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
    
    // 查询所有用户及其设备信息（使用licenses表获取expires_at、max_devices和reset_count_remaining）
    const usersQuery = await env.DB.prepare(`
      SELECT 
        u.id,
        u.username,
        u.email,
        u.is_active,
        l.expires_at,
        l.max_devices,
        l.reset_count_remaining,
        COUNT(DISTINCT d.id) as bound_devices
      FROM users u
      LEFT JOIN licenses l ON u.id = l.user_id AND l.is_active = 1
      LEFT JOIN devices d ON u.id = d.user_id
      GROUP BY u.id
      ORDER BY u.username
    `).all();
    
    const users = [];
    const nowTimestamp = Math.floor(Date.now() / 1000);
    
    for (const user of usersQuery.results) {
      // 查询该用户的设备详情
      const devicesQuery = await env.DB.prepare(`
        SELECT 
          id,
          hardware_id,
          device_name,
          os_version,
          app_version,
          created_at,
          last_seen_at,
          last_ip
        FROM devices
        WHERE user_id = ?
        ORDER BY last_seen_at DESC
      `).bind(user.id).all();
      
      // 计算剩余天数（使用Unix时间戳）
      let remainingDays = 0;
      if (user.expires_at) {
        remainingDays = Math.ceil((user.expires_at - nowTimestamp) / 86400);
      }
      
      users.push({
        id: user.id,
        username: user.username,
        email: user.email,
        is_active: user.is_active,
        expires_at: user.expires_at,
        remaining_days: Math.max(0, remainingDays),
        max_devices: user.max_devices || 1,
        bound_devices: user.bound_devices,
        remaining_slots: Math.max(0, (user.max_devices || 1) - user.bound_devices),
        reset_device_count: user.reset_count_remaining ?? 3,  // 剩余解绑次数
        devices: devicesQuery.results.map(d => ({
          id: d.id,
          hardware_id: d.hardware_id,
          device_name: d.device_name || '未知设备',
          os_version: d.os_version || '未知',
          app_version: d.app_version || '未知',
          created_at: d.created_at,
          last_seen: d.last_seen_at,
          last_ip: d.last_ip || '未知',
          // 判断是否在线（使用Unix时间戳）
          is_online: isDeviceOnline(d.last_seen_at, nowTimestamp)
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

// 判断设备是否在线（20分钟内有活动，使用Unix时间戳）
function isDeviceOnline(lastSeenTimestamp, nowTimestamp) {
  if (!lastSeenTimestamp) return false;
  
  try {
    const diffSeconds = nowTimestamp - lastSeenTimestamp;
    const diffMinutes = diffSeconds / 60;
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

