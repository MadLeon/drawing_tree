# Outlook + AI Skill 邮件助手方案总结

## 目标

- Outlook 中增加按钮：
  - 点击「AI Reply」
  - 自动按照固定规则生成邮件回复

---

# 核心架构


Outlook Add-in
|
↓
本地/服务器 AI 服务
|
↓
OpenAI / Claude API
|
↓
生成回复
|
↓
填入 Outlook


---

# Skill 实现方式

## 简易 Skill

- 不直接调用 ChatGPT Skill
- 自己维护：

Skill配置
+
System Prompt
+
Email内容
+
API调用


示例：

```
name: reply-email

prompt:
  你是客服邮件助手
  规则:
  - 保持专业
  - 不编造信息
  - 使用固定格式
```

调用：

System Prompt
+
Email正文
↓
LLM API
↓
回复


## Outlook 集成方式

本地Windows服务方案

可行：

Outlook Add-in

↓

localhost:8000

↓

Windows AI Service

↓

OpenAI API

优点：

API Key 不暴露
无需服务器
可保存个人Skill
可访问本地文件/数据
Windows服务实现

方式：

简单
Startup Folder
    ↓
启动Python/Node服务
正式
Windows Service

启动系统

↓

AI Service运行
本地服务结构
AI-Mail-Agent

├── server.py
├── skills/
│     ├── reply-email.yaml
│     ├── translate.yaml
│
├── config.json

接口：

POST localhost:8000/reply-email

输入：

email content

输出：

generated reply
推荐演进路线
Phase 1
Outlook按钮

↓

Prompt

↓

API

↓

回复

快速验证。

Phase 2
Skill配置

↓

Prompt模板

↓

多个功能

例如：

Reply
Rewrite
Translate
Summarize
Phase 3
Email

↓

分类

↓

CRM/知识库

↓

AI Agent

↓

验证

↓

回复

企业级方案。

推荐最终架构
                 Outlook
                    |
              Outlook Add-in
                    |
             localhost API
                    |
          Windows AI Assistant
                    |
        ---------------------
        |                   |
     Skill配置          本地数据
        |
        ↓
 OpenAI / Claude API

适合：

个人AI助手
小团队内部工具
Outlook智能回复系统

## 目标

实现一个 AI 辅助 email 生成 Outlook 插件, 具有点击即用的特点
后续实现自动补全功能 (实时搜索当前是否有git开源项目提供自动补全功能, 避免重复造轮子)

## 功能

- 翻译当前邮件
- 润色用户手写内容
- 翻译并润色用户手写内容
- 自定义语言
- 用户自定义技能作为系统提示词
- 用户自定义系统默认提示词
- 支持自定义 API key