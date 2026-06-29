# WPF 模块化学习项目推荐整理

本文整理了适合学习 WPF 模块化、解耦架构、分层设计的学习项目与学习路径。

---

# 🥇 1. Prism 官方示例（WPF 模块化标准）

GitHub:
https://github.com/PrismLibrary/Prism

## 重点学习内容

### 模块化（Module）
- 每个模块独立 DLL
- LoginModule / OrderModule / Shell 结构

### 依赖注入（DI）
- IContainerProvider

### 事件总线（EventAggregator）
- 模块间通信标准方式

### UI 分区（Region）
- 一个窗口动态加载多个模块 UI

## 你要理解的核心
- 模块之间如何解耦
- 如何避免 ViewModel 互相引用
- 如何实现跨模块通信

---

# 🥈 2. Clean Architecture WPF 示例

GitHub:
https://github.com/jasontaylordev/CleanArchitecture

## 架构结构

Domain
Application
Infrastructure
Presentation (WPF)

## 核心学习点

### 分层依赖
- 内层不依赖外层
- Domain 独立于 UI

### CQRS 思想
- Command / Query 分离

### 接口隔离
- Application 不依赖 Infrastructure

## 你会学到
- Core / Infrastructure / UI 分离
- 依赖反转原则（DIP）
- 业务逻辑独立 UI

---

# 🥉 3. 轻量 WPF MVVM 模块化 Demo（推荐入门）

GitHub 搜索关键词：

WPF MVVM modular sample  
WPF simple navigation MVVM  
WPF clean architecture sample  

## 推荐特征

- 代码量 < 5000 行
- 不依赖复杂框架
- 有 View + ViewModel
- 有 Service 层
- 有简单 Navigation

---

# 🧠 推荐学习路径

## 第 1 阶段：模块概念
学习 Prism
- Module 是什么
- EventAggregator 是什么
- Navigation 如何工作

---

## 第 2 阶段：分层设计
学习 Clean Architecture
- Core / Infrastructure
- Interface 隔离
- 依赖方向控制

---

## 第 3 阶段：动手实践

构建自己的 WPF 模块结构：

App
Core
Infrastructure
Modules
    Login
    Order
Themes

---

# 🔥 推荐练习（非常重要）

只实现一个功能模块（Login）：

LoginModule
- LoginView
- LoginViewModel
- LoginService
- IUserService

要求：
- 不允许跨模块调用
- 必须通过接口通信
- UI 与逻辑分离

---

# 🎯 核心总结

一个好的 WPF 模块化系统必须具备：

✔ Module 独立  
✔ Interface 解耦  
✔ Event 通信  
✔ Navigation 抽象  
✔ Theme 统一管理  

---

# 🚀 建议下一步

可以进一步构建：

👉 极简 WPF 模块化教学项目（500 行代码版）

特点：
- 2 个模块（Login + Order）
- 完整解耦结构
- 适合 Claude Code 使用优化
