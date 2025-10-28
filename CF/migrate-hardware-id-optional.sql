-- 迁移脚本 - 将 hardware_id 改为可选
-- 2025-10-28
-- 说明：允许管理员手动添加账户时不提供 hardware_id

-- SQLite 不支持直接修改列约束，需要重建表

-- 1. 创建新表（hardware_id 为可选）
CREATE TABLE IF NOT EXISTS users_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    email TEXT UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NOT NULL,
    is_active BOOLEAN DEFAULT 1,
    last_login TIMESTAMP,
    hardware_id TEXT,  -- 改为可选
    max_devices INTEGER DEFAULT 1,
    register_ip TEXT,
    register_source TEXT DEFAULT 'desktop_client',
    reset_device_count INTEGER DEFAULT 3
);

-- 2. 复制数据
INSERT INTO users_new SELECT * FROM users;

-- 3. 删除旧表
DROP TABLE users;

-- 4. 重命名新表
ALTER TABLE users_new RENAME TO users;

-- 5. 重建索引
CREATE INDEX IF NOT EXISTS idx_username ON users(username);
CREATE INDEX IF NOT EXISTS idx_expires_at ON users(expires_at);
CREATE INDEX IF NOT EXISTS idx_hardware_id ON users(hardware_id);
CREATE INDEX IF NOT EXISTS idx_register_ip ON users(register_ip);

-- 完成

