-- 设备指纹增强迁移脚本（可选）
-- 如果决定实施服务器端设备指纹验证，执行此脚本

-- 检查：devices 表已有 last_ip 字段（在 schema.sql 中已定义）
-- 只需添加额外的指纹字段

-- 为 devices 表添加轻量级指纹字段
-- ALTER TABLE devices ADD COLUMN last_ip TEXT;        -- ✅ 已存在，无需添加
ALTER TABLE devices ADD COLUMN last_country TEXT;       -- 最后登录国家（ISO代码，如CN/US）
ALTER TABLE devices ADD COLUMN last_timezone TEXT;      -- 最后登录时区（如Asia/Shanghai）

-- 创建安全日志表（可选）
CREATE TABLE IF NOT EXISTS security_logs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id INTEGER NOT NULL,
  device_id INTEGER,
  event_type TEXT NOT NULL,  -- 'suspicious_location', 'device_switch', etc.
  description TEXT,
  ip_address TEXT,
  country TEXT,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
  FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_security_logs_user ON security_logs(user_id);
CREATE INDEX IF NOT EXISTS idx_security_logs_created ON security_logs(created_at);

-- 说明：
-- 此迁移是可选的，仅在需要实施服务器端设备指纹验证时使用
-- 优点：
--   1. 可以检测异常登录行为（跨国登录、频繁切换设备）
--   2. 提供管理员审计日志
--   3. 不影响用户正常使用（仅记录，不强制退出）
-- 缺点：
--   1. 增加服务器存储成本
--   2. 可能误报（用户出差、VPN）
--   3. 隐私考虑

