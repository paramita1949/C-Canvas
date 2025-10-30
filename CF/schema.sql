-- ============================================
-- CCanvas 授权系统完整数据库架构
-- 可直接在 Cloudflare D1 控制台执行
-- ============================================

-- 用户表
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    email TEXT,
    phone TEXT,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    is_active INTEGER DEFAULT 1,
    
    -- 注册时的硬件指纹（用于防止重复注册，5项分开存储）
    register_cpu_id TEXT,
    register_motherboard_serial TEXT,
    register_disk_serial TEXT,
    register_bios_uuid TEXT,
    register_windows_install_id TEXT,
    register_ip TEXT,
    register_device_name TEXT
);

-- 用户索引
CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_users_phone ON users(phone);
CREATE INDEX IF NOT EXISTS idx_users_created_at ON users(created_at);

-- 硬件指纹索引（用于快速查找是否已注册）
CREATE INDEX IF NOT EXISTS idx_users_register_cpu ON users(register_cpu_id);
CREATE INDEX IF NOT EXISTS idx_users_register_mb ON users(register_motherboard_serial);
CREATE INDEX IF NOT EXISTS idx_users_register_disk ON users(register_disk_serial);
CREATE INDEX IF NOT EXISTS idx_users_register_bios ON users(register_bios_uuid);
CREATE INDEX IF NOT EXISTS idx_users_register_win ON users(register_windows_install_id);
CREATE INDEX IF NOT EXISTS idx_users_register_ip ON users(register_ip);

-- 授权表
CREATE TABLE IF NOT EXISTS licenses (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    license_type TEXT NOT NULL,  -- 'trial', 'basic', 'pro', 'enterprise'
    max_devices INTEGER NOT NULL DEFAULT 1,
    expires_at INTEGER,  -- NULL表示永久，否则为Unix时间戳（秒）
    reset_count_remaining INTEGER DEFAULT 3,  -- 剩余解绑次数，默认3次
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    is_active INTEGER DEFAULT 1,
    notes TEXT,
    
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_licenses_user_id ON licenses(user_id);
CREATE INDEX IF NOT EXISTS idx_licenses_expires_at ON licenses(expires_at);
CREATE INDEX IF NOT EXISTS idx_licenses_type ON licenses(license_type);

-- 设备表（登录设备，只存储混合后的硬件ID）
CREATE TABLE IF NOT EXISTS devices (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    device_name TEXT,
    
    -- 混合后的硬件ID（由5项硬件信息混合加密生成）
    hardware_id TEXT NOT NULL,
    
    -- 设备信息
    os_version TEXT,
    app_version TEXT,
    last_ip TEXT,
    
    -- 时间戳（Unix时间戳，秒）
    first_seen_at INTEGER NOT NULL,
    last_seen_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    
    is_active INTEGER DEFAULT 1,
    
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_devices_user_hw ON devices(user_id, hardware_id);
CREATE INDEX IF NOT EXISTS idx_devices_hardware_id ON devices(hardware_id);
CREATE INDEX IF NOT EXISTS idx_devices_last_seen ON devices(last_seen_at);

-- 会话表（用于登录token管理）
CREATE TABLE IF NOT EXISTS sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    device_id INTEGER,
    token TEXT NOT NULL UNIQUE,
    expires_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    last_heartbeat_at INTEGER NOT NULL,
    ip_address TEXT,
    user_agent TEXT,
    
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_sessions_token ON sessions(token);
CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_sessions_expires_at ON sessions(expires_at);

-- 审计日志表
CREATE TABLE IF NOT EXISTS audit_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER,
    action TEXT NOT NULL,  -- 'login', 'login_failed', 'register', 'logout', 'device_reset'
    details TEXT,  -- JSON格式的详细信息
    ip_address TEXT,
    created_at INTEGER NOT NULL,
    
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_audit_logs_user_id ON audit_logs(user_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_action ON audit_logs(action);
CREATE INDEX IF NOT EXISTS idx_audit_logs_created_at ON audit_logs(created_at);
CREATE INDEX IF NOT EXISTS idx_audit_logs_ip ON audit_logs(ip_address);

-- 注册限制表（用于防止同一设备/IP过度注册）
CREATE TABLE IF NOT EXISTS registration_blocks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    identifier_type TEXT NOT NULL,  -- 'hardware', 'ip'
    identifier_value TEXT NOT NULL,
    reason TEXT,
    blocked_at INTEGER NOT NULL,
    blocked_until INTEGER,  -- NULL表示永久封禁
    
    UNIQUE(identifier_type, identifier_value)
);

CREATE INDEX IF NOT EXISTS idx_registration_blocks_identifier ON registration_blocks(identifier_type, identifier_value);
CREATE INDEX IF NOT EXISTS idx_registration_blocks_until ON registration_blocks(blocked_until);
