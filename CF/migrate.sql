-- 迁移脚本 - 添加注册来源和硬件ID必填
-- 2025-10-28

-- 1. 添加 register_source 字段（如果不存在）
ALTER TABLE users ADD COLUMN register_source TEXT DEFAULT 'desktop_client';

-- 2. 为已存在的用户表添加 reset_device_count 字段（如果不存在）
ALTER TABLE users ADD COLUMN reset_device_count INTEGER DEFAULT 3;

-- 3. 更新已有用户的默认值
UPDATE users SET reset_device_count = 3 WHERE reset_device_count IS NULL;
UPDATE users SET register_source = 'legacy' WHERE register_source IS NULL;

-- 4. 为已有用户设置临时hardware_id（如果为空）
-- 注意：旧用户的hardware_id可能为空，给他们一个临时ID
UPDATE users SET hardware_id = 'legacy_' || id WHERE hardware_id IS NULL OR hardware_id = '';

-- 5. 添加索引以优化查询性能
CREATE INDEX IF NOT EXISTS idx_hardware_id ON users(hardware_id);
CREATE INDEX IF NOT EXISTS idx_register_ip ON users(register_ip);

-- 完成
-- 注意：运行此脚本前请备份数据库
-- 新注册的用户必须提供hardware_id
