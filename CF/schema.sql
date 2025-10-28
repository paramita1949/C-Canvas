-- 用户表
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    email TEXT UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NOT NULL,
    is_active BOOLEAN DEFAULT 1,
    last_login TIMESTAMP,
    hardware_id TEXT,  -- 注册硬件ID（可选，仅用于记录首次注册设备）
    max_devices INTEGER DEFAULT 1,  -- 最大设备数
    register_ip TEXT,  -- 注册IP地址
    register_source TEXT DEFAULT 'desktop_client',  -- 注册来源
    reset_device_count INTEGER DEFAULT 3  -- 剩余可重置设备次数，默认3次
);

-- 登录日志表
CREATE TABLE IF NOT EXISTS login_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    login_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ip_address TEXT,
    user_agent TEXT,
    success BOOLEAN,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

-- 设备表(用于多设备管理)
CREATE TABLE IF NOT EXISTS devices (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    hardware_id TEXT NOT NULL,
    device_name TEXT,
    first_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_ip TEXT,
    FOREIGN KEY (user_id) REFERENCES users(id),
    UNIQUE(user_id, hardware_id)
);

-- 创建索引
CREATE INDEX IF NOT EXISTS idx_username ON users(username);
CREATE INDEX IF NOT EXISTS idx_expires_at ON users(expires_at);
CREATE INDEX IF NOT EXISTS idx_hardware_id ON users(hardware_id);
CREATE INDEX IF NOT EXISTS idx_register_ip ON users(register_ip);
CREATE INDEX IF NOT EXISTS idx_user_logs ON login_logs(user_id);

