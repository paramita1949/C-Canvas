---
name: api-doc-generator
description: 为 API 端点生成文档（OpenAPI/Swagger 格式）。使用 /api-doc 触发。
---

# API Doc Generator

为 API 端点自动生成文档。

## Step 1: 扫描 API 端点

查找控制器和路由定义：
- `[ApiController]` 标记的类
- `[HttpGet]`, `[HttpPost]` 等属性
- 路由模板 `[Route("api/...")]`

## Step 2: 提取端点信息

| 信息 | 来源 |
|------|------|
| 路由 | Route 属性 |
| 方法 | Http* 属性 |
| 参数 | 方法参数 |
| 响应 | 返回类型 |

## Step 3: 生成 OpenAPI 格式

```yaml
openapi: 3.0.0
info:
  title: API 文档
  version: 1.0.0
paths:
  /api/projects:
    get:
      summary: 获取所有项目
      responses:
        '200':
          description: 成功
```

## Step 4: 输出

生成 `docs/api.yaml` 或 `docs/api.md` 文件。
