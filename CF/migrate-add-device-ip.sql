-- 为 devices 表添加 last_ip 字段，用于记录设备最后访问的IP地址
-- 这个字段用于显示设备的在线状态和IP信息

-- 添加 last_ip 字段
ALTER TABLE devices ADD COLUMN last_ip TEXT;

-- 说明：
-- 1. last_ip 用于记录设备最后访问的IP地址
-- 2. 每次设备登录或心跳时更新此字段
-- 3. 可以为 NULL（对于旧设备或未记录IP的情况）

